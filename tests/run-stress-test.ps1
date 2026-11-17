# Stress test: hammers a Debug build of Mora (settings of its own, MORA_DATA) with what real use throws at it,
# faster and more at once: two players playing together and changing tracks, floods of alerts, the panel opened and
# closed on every tab, timers, dozens of coding agent sessions. After each phase: Mora must still run, its error log
# must be empty, and its memory must not keep growing. The installed Mora is stopped during the test and started
# again at the end. Uses no mouse or keyboard. About 4 minutes.
param([int]$Seed = 7)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$data = Join-Path $env:TEMP 'mora-stress-test'
New-Item -ItemType Directory -Force $data | Out-Null
Remove-Item (Join-Path $data '*') -Force -ErrorAction SilentlyContinue
'{ "StartWithWindows": false, "PopVolume": false, "ConnectClaudeCode": true, "AgentsRail": true, "MascotRoams": true, "ShowLyrics": false, "ShowWeather": false }' |
    Set-Content (Join-Path $data 'settings.json') -Encoding utf8
for ($i = 0; $i -lt 8; $i++) { Get-Process Mora -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Milliseconds 400 }

$rng = [Random]::new($Seed)
$fails = 0
$players = @()
$mora = $null
function Fail([string]$what) { $script:fails++; Write-Host "FAIL $what" }
function Sim([string]$cmd) { Start-Process $exe -ArgumentList '--simulate', $cmd -Wait }
function Post($o) { ($o | ConvertTo-Json -Compress -Depth 5) | curl.exe -s -m 2 --data-binary "@-" http://127.0.0.1:47115/claude | Out-Null }
function Player($p, [string]$cmd) { $p.StandardInput.WriteLine($cmd); $null = $p.StandardOutput.ReadLine() }
function Check([string]$phase) {
    Start-Sleep -Milliseconds 800
    $m = Get-Process -Id $mora.Id -ErrorAction SilentlyContinue
    $log = Join-Path $data 'errors.log'
    if (-not $m) { Fail "$phase : Mora is gone" }
    elseif (Test-Path $log) { Fail "$phase : errors.log has entries"; Get-Content $log | Select-Object -First 12 | ForEach-Object { "     $_" }; Remove-Item $log }
    else { Write-Host ("ok   {0,-34} {1,6:0} MB, {2} handles" -f $phase, ($m.WorkingSet64 / 1MB), $m.HandleCount) }
}

try {
    dotnet build (Join-Path $root 'src\Mora') -p:Platform=x64 -v q -nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Mora does not build' }
    dotnet build (Join-Path $root 'tests\DemoPlayer') -v q -nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'DemoPlayer does not build' }
    $exe = Join-Path $root 'src\Mora\bin\x64\Debug\net10.0-windows10.0.19041.0\Mora.exe'
    $playerDir = Join-Path $root 'tests\DemoPlayer\bin\Debug\net10.0-windows10.0.19041.0'
    Copy-Item (Join-Path $playerDir 'DemoPlayer.exe') (Join-Path $playerDir 'Radio.exe') -Force # a second, separate player
    foreach ($name in 'DemoPlayer', 'Radio') {
        $psi = New-Object Diagnostics.ProcessStartInfo (Join-Path $playerDir "$name.exe")
        $psi.UseShellExecute = $false; $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.CreateNoWindow = $true
        $p = [Diagnostics.Process]::Start($psi); $null = $p.StandardOutput.ReadLine(); $players += $p
        Player $p "track Opening $name|Band $name||200|10"
    }
    $env:MORA_DATA = $data
    $mora = Start-Process $exe -PassThru
    Start-Sleep -Seconds 6
    Check 'start, two players playing'

    # 1. Two players at once: tracks, pauses and resumes on both, sometimes a burst.
    for ($i = 0; $i -lt 60; $i++) {
        $p = $players[$rng.Next(2)]
        switch ($rng.Next(6)) {
            0 { Player $p 'pause' } 1 { Player $p 'play' }
            default { Player $p ("track Song {0}|Artist {1}||{2}|{3}" -f $i, $rng.Next(5), (120 + $rng.Next(200)), $rng.Next(60)) }
        }
        Start-Sleep -Milliseconds ($rng.Next(60, 900))
        if ($i % 15 -eq 7) { Sim 'open Media'; Start-Sleep -Milliseconds 400; Sim 'close' }
    }
    Check 'two players, 60 changes'

    # 2. A flood of alerts of every kind, with the panel opened and closed on every tab in between.
    $tabs = 'Home', 'Media', 'Timers', 'Shelf', 'Clipboard', 'Agents'
    for ($i = 0; $i -lt 50; $i++) {
        switch ($rng.Next(8)) {
            0 { Sim ('headphones "Headset {0}" {1}' -f $i, $rng.Next(5, 100)) }
            1 { Sim ("charging {0}" -f $rng.Next(1, 100)) }
            2 { Sim ("battery {0} {1}" -f 21, 19) }
            3 { Sim ('track "Burst {0}" "Nobody"' -f $i) }
            4 { Sim ('agent "api needs you" "Permission for Bash {0}"' -f $i) }
            5 { Sim ("open {0}" -f $tabs[$rng.Next($tabs.Count)]) }
            6 { Sim 'close' }
            7 { Start-Sleep -Milliseconds 300 }
        }
    }
    Sim 'close'
    Check 'alert flood, 50 events'

    # 3. Coding agents: 30 sessions, 600 events in a rush, half of them ending.
    $tools = 'Read', 'Edit', 'Bash', 'Grep', 'Write', 'TodoWrite', 'WebFetch', 'mcp__x__y'
    for ($i = 0; $i -lt 600; $i++) {
        $sid = "stress{0}" -f $rng.Next(30); $cwd = "C:\Dev\project{0}" -f $rng.Next(30)
        switch ($rng.Next(7)) {
            0 { Post @{ session_id = $sid; hook_event_name = 'UserPromptSubmit'; cwd = $cwd; prompt = "Do task $i" } }
            1 { $t = $tools[$rng.Next($tools.Count)]
                $input = if ($t -eq 'TodoWrite') { @{ todos = @(@{ content = 'one'; status = 'completed' }, @{ content = 'two'; status = 'in_progress' }, @{ content = 'three'; status = 'pending' }) } } else { @{ file_path = "$cwd\file$i.cs"; description = "Step $i"; command = 'x' } }
                Post @{ session_id = $sid; hook_event_name = 'PreToolUse'; cwd = $cwd; tool_name = $t; tool_input = $input } }
            2 { Post @{ session_id = $sid; hook_event_name = 'PostToolUse'; cwd = $cwd; tool_name = 'Bash'; tool_response = @{ stdout = ('x' * $rng.Next(1, 5000)) } } }
            3 { Post @{ session_id = $sid; hook_event_name = 'Notification'; cwd = $cwd; notification_type = 'permission_prompt'; message = "Allow step $i?" } }
            4 { Post @{ session_id = $sid; hook_event_name = 'Stop'; cwd = $cwd } }
            5 { if ($rng.Next(4) -eq 0) { Post @{ session_id = $sid; hook_event_name = 'SessionEnd'; cwd = $cwd } } }
            6 { Post @{ session_id = $sid; hook_event_name = 'Notification'; cwd = $cwd; notification_type = 'idle_prompt'; message = 'Waiting' } }
        }
        if ($i % 120 -eq 60) { Sim 'open Agents'; Start-Sleep -Milliseconds 500; Sim 'close' }
    }
    # Garbage on the agents' port must be shrugged off.
    'not json at all' | curl.exe -s -m 2 --data-binary "@-" http://127.0.0.1:47115/claude | Out-Null
    '{"session_id": 5, "hook_event_name": ["x"]}' | curl.exe -s -m 2 --data-binary "@-" http://127.0.0.1:47115/claude | Out-Null
    curl.exe -s -m 2 http://127.0.0.1:47115/ | Out-Null
    Check 'agents, 30 sessions, 600 events'

    # 4. Focus rounds of a few seconds, with the panel moving around them.
    Sim 'focus 3 2'
    for ($i = 0; $i -lt 16; $i++) {
        Sim ("open {0}" -f $tabs[$rng.Next($tabs.Count)]); Start-Sleep -Milliseconds ($rng.Next(200, 1200)); Sim 'close'; Start-Sleep -Milliseconds ($rng.Next(100, 900))
    }
    Check 'focus rounds every few seconds'

    # 5. Everything at once for a minute.
    $end = (Get-Date).AddSeconds(60)
    $n = 0
    while ((Get-Date) -lt $end) {
        $n++
        switch ($rng.Next(6)) {
            0 { Player $players[$rng.Next(2)] ("track Chaos {0}|Mixer||180|5" -f $n) }
            1 { Sim ("open {0}" -f $tabs[$rng.Next($tabs.Count)]) }
            2 { Sim 'close' }
            3 { Post @{ session_id = "chaos{0}" -f $rng.Next(6); hook_event_name = 'PreToolUse'; cwd = 'C:\Dev\chaos'; tool_name = 'Edit'; tool_input = @{ file_path = "C:\Dev\chaos\f$n.cs" } } }
            4 { Sim ('headphones "Chaos {0}"' -f $n) }
            5 { Player $players[$rng.Next(2)] ($(if ($rng.Next(2)) { 'pause' } else { 'play' })) }
        }
    }
    Sim 'close'
    Check "everything at once, $n events"

    # Settled: memory again, after the dust.
    Start-Sleep -Seconds 10
    Check 'ten seconds later'
}
finally {
    foreach ($p in $players) { try { $p.StandardInput.WriteLine('quit') } catch {}; Start-Sleep -Milliseconds 200; if (-not $p.HasExited) { $p.Kill() } }
    if ($mora) { Stop-Process -Id $mora.Id -ErrorAction SilentlyContinue }
    Remove-Item Env:MORA_DATA -ErrorAction SilentlyContinue
    $installed = Join-Path $env:LOCALAPPDATA 'Programs\Mora\Mora.exe'
    if (Test-Path $installed) { Start-Process explorer.exe $installed }
}
if ($fails -eq 0) { Write-Host 'PASS' } else { Write-Host "$fails failure(s)" }
exit [int]($fails -ne 0)
