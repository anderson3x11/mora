using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace Mora;

// A download is finished when the browser renames its temporary file to the final name
// (Chrome/Edge .crdownload, Firefox .part, Safari .download). Created events are ignored:
// Firefox creates an empty placeholder with the final name when the download starts.
sealed class Downloads
{
    static readonly string[] TempExtensions = [".crdownload", ".part", ".partial", ".download", ".tmp"];

    public event Action<string>? Finished; // full path, raised on the UI thread
    readonly FileSystemWatcher? _watcher;

    public Downloads(DispatcherQueue ui)
    {
        var folder = Folder();
        if (!Directory.Exists(folder)) return;
        _watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName };
        _watcher.Renamed += (_, e) =>
        {
            if (IsTemp(e.OldName) && !IsTemp(e.Name)) ui.TryEnqueue(() => Finished?.Invoke(e.FullPath));
        };
        _watcher.EnableRaisingEvents = true;
    }

    public static bool IsTemp(string? name) =>
        name is null || TempExtensions.Any(x => name.EndsWith(x, StringComparison.OrdinalIgnoreCase));

    // The user may have moved Downloads, so ask the shell rather than guessing %USERPROFILE%\Downloads.
    public static string Folder()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B"); // FOLDERID_Downloads
        if (SHGetKnownFolderPath(ref id, 0, 0, out var p) != 0)
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        try { return Marshal.PtrToStringUni(p)!; }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath(ref Guid id, uint flags, nint token, out nint path);
}
