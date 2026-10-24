# Atoll

A Dynamic Island for Windows 10 and 11. A black pill at the top of the screen shows what is happening right now (music, timers, downloads, devices), opens into a small panel of quick controls with Alt+Q, and gets out of the way. It never takes your keyboard.

The product brief is in [project.md](project.md).

## Build and run

Needs the .NET 10 SDK. Visual Studio is not required: the Windows App SDK comes from NuGet and the app runs unpackaged.

```bash
dotnet build src/Atoll -c Release -p:Platform=x64
```

```bash
src/Atoll/bin/x64/Release/net10.0-windows10.0.19041.0/Atoll.exe
```

The first launch opens a 4-step setup. Afterwards, the Atoll icon next to the clock opens the island (left click) or a menu with Settings and Quit (right click).

## Keystroke-safety test

Types continuously into its own window while it drives the island through every state: shortcut, clicks on panel buttons, clicks outside, alerts, a clipboard paste done by Atoll, a file dragged onto the shelf and back out, and a fullscreen "game". It passes only if every character arrives and its window never loses the foreground. It takes over the mouse and keyboard for about a minute, and restores your clipboard afterwards.

Start Atoll first (with a settings file present, so setup does not open), then:

```bash
dotnet run --project tests/KeystrokeTest
```

## What is in

| Area | Notes |
|---|---|
| Island window | Never activates (`WS_EX_NOACTIVATE`, `MA_NOACTIVATE`), click-through outside the pill (window region follows the shape), follows the monitor of the active window |
| Motion | Damped springs at display rate (open 340/27, close 520/46), interruptible, content fades before the shape shrinks, reduced motion respected |
| Panel | Media (system media sessions: Spotify, browsers, players), Timers, Shelf, Clipboard |
| Alerts | Track change, download finished, audio output switch, volume (opt-in), low battery and charging, timer done, clipboard copy. 3 s, held while hovered, queued while the panel is open |
| Games | Fullscreen detection, three modes (stay out, music card on Alt+Q, ask per game), held alerts summarised on exit |
| Setup and settings | First-run setup in 4 steps, settings window from the tray, `%LOCALAPPDATA%\Atoll\settings.json` |
| Cost | Idle CPU 0, about 90 MB private memory (WinUI 3 baseline), about 3% of one core while the resting pill animates its waveform |

## Known limits

- Dragging a file toward the top of the screen while the island is hidden does not open the shelf yet: open the panel with Alt+Q first (also works mid-drag). A global drag watcher would fire on browser tab and window drags too.
- The shelf and clipboard history live in memory only and are cleared on quit.
- Monitors with different scaling were not tested (both test monitors were at 100%).
- Native Windows volume and brightness flyouts are not hidden, which is why the volume pop-up is opt-in.
