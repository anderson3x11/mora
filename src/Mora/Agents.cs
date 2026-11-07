using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Dispatching;

namespace Mora;

// Coding agents in the island. Claude Code runs its hooks on each step (a prompt sent, a tool about to run, a
// question for the user, the end of a turn); Mora adds hooks that post the step's JSON with curl to this
// loopback port. In the background, so they never slow the agent, and they fail silently while Mora is off.
// Nothing leaves the PC.
sealed class Agents(DispatcherQueue ui)
{
    const int Port = 47115;
    const string Url = "http://127.0.0.1:47115/claude";

    public enum State { Working, NeedsYou, Done }

    public sealed class Session
    {
        public string Name = "";
        public State State;
        public string Action = "";
        public DateTime Started = DateTime.Now;
    }

    public readonly Dictionary<string, Session> Sessions = []; // by session id
    public event Action<Session?, string>? Changed;            // the session (null once ended), and the hook event

    public void Start()
    {
        TcpListener listener;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, Port);
            listener.Start();
        }
        catch (SocketException) { return; } // the port is taken (a second Mora): no agents
        _ = Task.Run(async () =>
        {
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                _ = Serve(client);
            }
        });
    }

    // Just enough HTTP for curl: one POST, a Content-Length, an empty answer.
    async Task Serve(TcpClient client)
    {
        using var _ = client;
        try
        {
            var s = client.GetStream();
            var head = new byte[8192];
            int n = 0, end = -1;
            while (end < 0 && n < head.Length)
            {
                int r = await s.ReadAsync(head.AsMemory(n));
                if (r == 0) return;
                n += r;
                end = head.AsSpan(0, n).IndexOf("\r\n\r\n"u8);
            }
            if (end < 0) return;
            var length = Encoding.ASCII.GetString(head, 0, end).Split("\r\n")
                .Select(l => l.Split(':', 2)).Where(p => p.Length == 2 && p[0].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                .Select(p => int.TryParse(p[1], out var v) ? v : 0).FirstOrDefault();
            if (length is <= 0 or > 16 << 20) return; // a tool's output can be big; past 16 MB, skipped
            var body = new byte[length];
            int have = Math.Min(n - end - 4, length);
            head.AsSpan(end + 4, have).CopyTo(body);
            while (have < length)
            {
                int r = await s.ReadAsync(body.AsMemory(have));
                if (r == 0) return;
                have += r;
            }
            await s.WriteAsync("HTTP/1.1 204 No Content\r\nConnection: close\r\n\r\n"u8.ToArray());
            var json = Encoding.UTF8.GetString(body);
            ui.TryEnqueue(() => Handle(json));
        }
        catch (Exception e) when (e is IOException or SocketException) { }
    }

    void Handle(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return; }
        using var _ = doc;
        var r = doc.RootElement;
        string Str(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        var id = Str(r, "session_id");
        var ev = Str(r, "hook_event_name");
        if (id == "") return;
        if (ev == "SessionEnd")
        {
            if (Sessions.Remove(id)) Changed?.Invoke(null, ev);
            return;
        }
        if (!Sessions.TryGetValue(id, out var s)) Sessions[id] = s = new();
        s.Name = Str(r, "session_title") is { Length: > 0 } title ? title : Path.GetFileName(Str(r, "cwd").TrimEnd('\\', '/'));
        switch (ev)
        {
            case "UserPromptSubmit":
                (s.State, s.Action, s.Started) = (State.Working, "Thinking", DateTime.Now);
                break;
            case "PreToolUse":
                (s.State, s.Action) = (State.Working, Describe(Str(r, "tool_name"), r.TryGetProperty("tool_input", out var input) ? input : default));
                break;
            case "PostToolUse" when s.State == State.NeedsYou: // allowed: back to work
                s.State = State.Working;
                break;
            case "Notification" when Str(r, "notification_type") != "idle_prompt": // a permission or a question, not "still waiting"
                (s.State, s.Action) = (State.NeedsYou, Str(r, "message") is { Length: > 0 } m ? m : "Waiting for you");
                break;
            case "Stop":
                (s.State, s.Action) = (State.Done, "Done");
                break;
            default:
                return;
        }
        Changed?.Invoke(s, ev);

        string Describe(string tool, JsonElement i)
        {
            var file = Path.GetFileName(Str(i, "file_path"));
            return tool switch
            {
                "Edit" or "MultiEdit" or "Write" or "NotebookEdit" => $"Editing {file}",
                "Read" => $"Reading {file}",
                "Bash" or "PowerShell" => Str(i, "description") is { Length: > 0 } d ? d : "Running a command",
                "Grep" or "Glob" => "Searching the code",
                "WebFetch" or "WebSearch" => "Searching the web",
                "Agent" or "Task" => "Running a subagent",
                _ => tool.StartsWith("mcp__") ? "Using a connector" : tool,
            };
        }
    }

    // ---------- Claude Code's hooks, in ~/.claude/settings.json ----------

    static readonly string[] Events = ["UserPromptSubmit", "PreToolUse", "PostToolUse", "Notification", "Stop", "SessionEnd"];
    static readonly string ClaudeSettings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    // Adds or removes Mora's hooks, leaving everything else in the file as it was. Ours are the ones posting to Url.
    public static void ConnectClaude(bool on)
    {
        JsonObject root;
        try { root = JsonNode.Parse(File.ReadAllText(ClaudeSettings)) as JsonObject ?? []; }
        catch (FileNotFoundException) { root = []; }
        catch (DirectoryNotFoundException) { root = []; }
        catch (JsonException) { return; } // a file being edited by hand: left alone
        if (root["hooks"] is not JsonObject hooks) root["hooks"] = hooks = [];
        foreach (var ev in Events)
        {
            if (hooks[ev] is not JsonArray groups) hooks[ev] = groups = [];
            foreach (var g in groups.ToList())
                if (g?["hooks"] is JsonArray list && list.Any(h => h?["args"] is JsonArray a && a.Any(x => x?.ToString() == Url)))
                    groups.Remove(g);
            if (on)
                groups.Add(new JsonObject
                {
                    ["hooks"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = "curl.exe",
                        ["args"] = new JsonArray("-s", "-m", "2", "--data-binary", "@-", Url),
                        ["async"] = true,
                    }),
                });
            if (groups.Count == 0) hooks.Remove(ev);
        }
        if (hooks.Count == 0) root.Remove("hooks");
        Directory.CreateDirectory(Path.GetDirectoryName(ClaudeSettings)!);
        File.WriteAllText(ClaudeSettings, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
}
