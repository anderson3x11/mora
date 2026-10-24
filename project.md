# Atoll: a Dynamic Island for Windows

Working name: **Atoll**. This file is the project brief. Read it fully before writing code, and treat the "Non-negotiable rules" section as hard requirements.

---

## 1. Goal

Build a viral, premium-feeling Windows app that brings the iPhone Dynamic Island / Mac notch app experience to Windows 10 and 11.

A black, pill-shaped "island" lives at the top center of the screen. It shows what is happening right now (music, timers, downloads, meetings, device events), expands into a small panel of quick controls, and gets out of the way.

The bar for quality is "feels like Apple made it for Windows". Polish, motion and respect for the user's focus are the product.

### How we got here

- The original idea was an app launcher for Windows. Research showed Raycast now ships a mature Windows version (2.0 left beta in August 2026, with AI Chat, extensions, window management, file search). That space is taken.
- We then looked for beloved Mac utilities without a great Windows equivalent. The Dynamic Island concept was picked because it is visual, demo-able in a 10 second clip, and easy to share.
- The founder has never owned a Mac or iPhone, so the research below on why people love (and hate) the island is the foundation of the product decisions.

### Reference prototype

An interactive HTML prototype exists (published as a claude.ai artifact named "Atoll for Windows"). It defines the intended feel: spring animations, states, setup flow, gaming modes, shelf, clipboard, the no-input-stealing test. When in doubt about behavior or motion, match the prototype. Its key parameters are copied into section 7.

---

## 2. Non-negotiable rules

1. **Never take the user's input.** The island must never become the foreground/active window, never take keyboard focus, and never swallow a keystroke or click meant for another app. This applies to auto pop-ups, hover, clicks on the island, and opening with the shortcut. One stolen keypress (mid-sentence, mid-boss-fight) and the user uninstalls.
2. **Respect games.** Behavior during fullscreen games is chosen by the user at install time (see section 6). Default must never interrupt an immersive game.
3. **Never interrupt for long.** Auto pop-ups retract after about 3 seconds. They stay longer only while the mouse hovers them.
4. **No ads, ever.** The island shows only things the user is doing. No promotions, including our own (no "upgrade to Pro" pop-ups in the island).
5. **Motion is the product.** Every state change animates with springs. No linear tweens, no instant jumps (except under reduced-motion settings).
6. **Lightweight.** Idle CPU near zero, low memory. A background utility that drains battery or adds input lag will be called out immediately.

---

## 3. What people love about the Dynamic Island (research)

Sources: iPhone owner forums (MacRumors), reviews (PhoneArena, Digital Trends, BGR), ArcTouch's developer overview, Mac notch app reviews (NotchNook, Alcove, Boring Notch).

1. **Glance, don't switch.** Ongoing activities stay visible without leaving the current app: calls, navigation, timers and alarms, sports scores, rides, flights, screen recordings. This is the most cited reason.
2. **Alerts get a small home.** System events (low battery, Face ID, AirPods connecting) morph into the island instead of covering the screen. One reviewer's favorite feature was the low battery warning no longer blocking the whole screen.
3. **Controls right there.** Pause, skip, stop a timer without opening the app.
4. **Two activities at once.** The island splits: one activity in the pill, a second in a small round bubble beside it.
5. **The animations.** Reviewers of Mac notch apps keep saying much of the appeal is the fluid, physical motion.
6. **The file shelf (Mac notch apps).** NotchNook's tray is often called its most useful feature: drop files into the notch, keep them, drag them out into another app later.
7. **Music with a visualizer.** Boring Notch built its popularity mostly on a nice music control center with a waveform.

## 4. What people hate (and our fix)

| Complaint | Our fix |
|---|---|
| Expands too often, covers part of the screen, distracting | User chooses at setup which activity types may pop up on their own. Pop-ups leave after ~3 s. |
| No control over which apps use it | One toggle per activity type (music, timers, downloads, meetings, devices/battery, volume/brightness, clipboard), editable any time. |
| Swipe gestures dismiss things by accident | No swipe gestures. Things close only on click outside or pressing the shortcut again. |
| Pausing music makes the resume control vanish seconds later | Paused media stays in the pill until dismissed. |
| Apps using it for ads (Duolingo incident) | No ads, rule 4. |
| Windows-specific: overlay apps steal focus, break typing and games | Rule 1, enforced at the window-style level (section 8). |
| Windows-specific: overlays look janky on multi-monitor and mixed DPI | Per-monitor DPI awareness v2, test on mixed scaling setups. |
| Mac notch apps: hangs when hovering, accessibility permission nags | Hover must never block; no scary permission prompts at first run. |

---

## 5. Competition on Windows and how we differentiate

Existing Windows apps (as of October 2026):

- **Notchify**: recently added clipboard history, drag-and-drop file tray, notification panel, app shortcuts.
- **DynamicWin** (Florian Butz, ~570 GitHub stars, source not public): media controller, Google Calendar, file tray, Bluetooth battery view, scroll to switch views.
- **Dynamic Edge** (Microsoft Store): WinUI 3, spring physics, media, volume, clipboard, timers, can dock to screen edges.
- **Isle Notch**: notifications, media, quick settings, hide on fullscreen.
- XWidget "Desktop Dynamic Island": avoid any association, it was reported to bundle malware.

None dominates. Feature lists are converging, so features alone will not win. Our differentiation:

1. **Input safety as a headline promise.** "Atoll never takes your keyboard." Marketed, demoed (live keystroke counter), and engineered at the OS level. Competitors don't make this promise.
2. **Gamer-aware by design.** The fullscreen game choice is part of setup, with per-game memory and a mid-game music card. Windows has a huge gaming audience, Mac apps never had to solve this.
3. **Apple-level motion.** Interruptible spring physics, content fading before shape changes, the split bubble. The 10 second demo video must make people ask "what is that?".
4. **Smart pop-up discipline.** Pop-ups are opt-in per type, short, held during games and summarized once on exit.
5. **Replace ugly Windows flyouts.** Volume, brightness, battery and headphone events shown in the island (see open question about suppressing the native flyouts).
6. **First-run delight.** A 4-step setup that feels premium and ends with the island animating in.
7. **Trust.** Signed binaries, Microsoft Store distribution, clear privacy stance (everything local, no telemetry without consent).

---

## 6. Feature spec

### Island states

| State | Size (logical px) | When |
|---|---|---|
| Hidden | slides above screen edge | nothing active, during setup, during games (unless summoned) |
| Compact pill | 214 x 32, radius 16 | an ongoing activity exists, only if enabled in settings (off by default: without a notch, a resting pill covers real content such as browser tabs) |
| Alert | 304 x 50, radius 25 (scaled with the panel) | a short-lived event (3 s) |
| Expanded panel | 384 x 170 to 190, radius 26 (20% smaller than the prototype, founder's call) | shortcut, click, hover, or file drag toward the top |
| Game music card | 360 x 82, radius 41 | shortcut pressed during a game set to "Only when I ask" |
| Split bubble | 32 x 32 circle beside the pill | two ongoing activities (e.g. music + timer) |

Sizes clamp to screen width minus 24 px.

### Opening and closing

- Shortcut: **Alt+Q** by default, configurable. Do not use Alt+Space (Raycast's default on Windows) or shortcuts PowerToys uses by default.
- Click the pill opens the panel.
- Hover the pill (when the compact pill is enabled) for ~320 ms opens it (setting, on by default). Leaving for ~450 ms closes a hover-opened panel.
- Dragging a file toward the top third of the screen opens the panel on the Shelf tab.
- Click outside or press the shortcut again closes. Escape is NOT captured (rule 1).

### Panel tabs (MVP)

- **Media**: art, title, artist, seekable progress, previous / play-pause / next, output device. Data from Windows' system media sessions, so Spotify, browsers and local players work with no per-app integration.
- **Timers**: quick 1 / 5 / 25 min, active timer with +1 min and cancel. Countdown shows in the pill or the split bubble.
- **Shelf**: drop files in, keep them, drag them out into any app, remove with x.
- **Clipboard**: recent copies, Paste button inserts into the last active app without moving focus.

### Alerts (each type togglable)

Music track change, timer started/finished, download finished (with Show action), meeting soon (with Join action), headphones connected with battery, volume/brightness HUD, low battery (with Energy saver action), clipboard copy. Alerts queue; if the panel is open they wait until it closes.

### Gaming (asked at setup, editable later)

When a fullscreen game runs, the user picks one:

1. **Stay out of my way** (default, recommended for RPGs, shooters, immersive games): island hidden, shortcut disabled, alerts held.
2. **Only when I ask** (chill games with background music): no pop-ups, shortcut opens the game music card, which auto-closes after 4 s without interaction.
3. **Decide per game**: on a game's first launch, ask once with two buttons; remember per game executable.

On exiting the game, held alerts are shown as one summary ("3 updates while you played").

### Setup flow (4 steps)

1. Welcome: what the island is, how to open it.
2. Fullscreen games: the three choices above.
3. What can pop up on its own: checkboxes per type.
4. The promise: never takes your keyboard, never shows promotions. Finish animates the island in and shows a one-time "Press Alt+Q any time" hint.

### Later (post-MVP ideas)

Calendar (Outlook / Google), notifications mirror, Bluetooth device list, screen recording indicator, Pomodoro focus sessions, live sports scores, delivery tracking, AI quick ask, themes, plugin API for third parties ("live activities" for Windows apps).

---

## 7. Motion spec (from the prototype)

Damped spring, mass 1, integrated at 240 Hz sub-steps, animating width, height, corner radius and vertical offset together.

- **Opening** (growing): stiffness 340, damping 27 (slightly underdamped, a small overshoot). Content fades in ~140 ms after the shape starts growing, with a scale from 0.94 to 1.
- **Closing** (shrinking): content fades out first (~140 ms), then after ~90 ms the shape shrinks with stiffness 520, damping 46 (critically damped, no bounce). The founder explicitly wanted the close calmer and cleaner than the open.
- **Interruptible**: a new target mid-animation retargets the springs from current position and velocity. Never restart from zero.
- **Corner radius** clamps to height / 2 at all times.
- **Split bubble**: scales in from 0 with a slight overshoot, positioned 6 px right of the pill.
- **Reduced motion** (Windows "Animation effects" off): near-instant critically damped transitions.
- Target the monitor's refresh rate (120/144 Hz must look smooth).

The island is always pure black with white text regardless of system theme (it reads as hardware).

---

## 8. Technical direction

### Stack

- Production recommendation: **C# + WinUI 3** (Windows App SDK) with the Composition APIs for spring animations at display refresh rate. Native look, low memory, good Win32 interop.
- Acceptable for a fast first prototype: Tauri (Rust + web UI). Lighter than Electron, but matching native smoothness is harder. Avoid Electron.

### Never stealing focus (rule 1), implementation notes

- Island window: borderless, top-most, tool window (no taskbar, no Alt+Tab), with `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST`.
- Show with `SW_SHOWNOACTIVATE`; move/resize with `SetWindowPos(..., SWP_NOACTIVATE)`.
- Handle `WM_MOUSEACTIVATE` returning `MA_NOACTIVATE` so clicks work without activation.
- Never call `SetForegroundWindow` / `SetActiveWindow` / `SetFocus` on the island.
- Transparent area around the pill must be click-through (hit-test only the visible shape, or toggle `WS_EX_TRANSPARENT`).
- Paste from clipboard tab: put text on the clipboard and send Ctrl+V to the previously active window, which stays foreground the whole time.
- Shelf drag-out uses OLE drag and drop from a non-activating window; verify it works with Explorer, browsers, Office, Discord.
- Automated test: a test app types continuously while the island cycles through every state; zero lost or redirected keystrokes.

### Other Windows APIs

- Global shortcut: `RegisterHotKey`.
- Media: `GlobalSystemMediaTransportControlsSessionManager`.
- Fullscreen / game detection: `SHQueryUserNotificationState` (e.g. `QUNS_RUNNING_D3D_FULL_SCREEN`, `QUNS_BUSY`) plus foreground window covering the monitor; identify games per executable for "Decide per game".
- Volume changes: Core Audio `IAudioEndpointVolumeCallback`.
- Battery: power status notifications. Bluetooth headphone battery where available.
- Notifications (later): `UserNotificationListener` (needs user permission).
- DPI: per-monitor DPI awareness v2; island follows the monitor with the active window or a user-chosen monitor.

### Open questions

- Can the native Windows volume/brightness flyouts be hidden reliably and safely? If not, show our HUD only when the user opts in, to avoid double flyouts.
- Behavior with auto-hidden taskbars and top-docked taskbars or other top-edge tools.
- Which monitor hosts the island on multi-monitor setups by default.
- Pricing: generous free tier for virality, Pro for extras (themes, advanced activities, plugins), never ads.

---

## 9. Virality checklist

- The core value must be obvious in a 10 second silent clip: pop-up during typing, music control during a game, file dropped into the island.
- Launch video ideas: "I typed through 8 pop-ups and lost zero keystrokes", "Dynamic Island, but for gamers", Downloads -> shelf -> Discord drag.
- Microsoft Store listing plus direct download, signed.
- Short setup, instant wow at the end of setup.

## 10. Definition of done for MVP

- All states and transitions from section 6 with the motion spec from section 7.
- Setup flow with the gaming choice and pop-up toggles, settings window to change them later.
- Media, timers, shelf, clipboard tabs working with real system data.
- Alerts for track change, timer, download, volume, battery, headphones.
- Fullscreen game handling for all three modes, held alerts summary.
- Keystroke-safety test passes; no focus change ever observed.
- Smooth on 60, 120 and 144 Hz, correct on 100/125/150/200% scaling and multi-monitor.
- Idle CPU ~0%, memory footprint small enough to forget it's running.
