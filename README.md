# PinHalo

[Русская версия](README.ru.md)

Pin any window on top of everything else with a hotkey (**Ctrl+Win+T** by default). A blue frame with Windows 11-style rounded corners shows which window is pinned.

A small C# tray app with no dependencies. It builds with the compiler that ships with Windows — no SDK needed. In the tray it is called *Always on Top*.

> **Tested on Windows 11 only — not tested on Windows 10.** The code has a separate Windows 10 path (square frame corners, since windows there are square), but it was only checked by forcing it on Windows 11. Reports from Windows 10 users are welcome.

## Features

- **Hotkey** toggles "always on top" for the active window. Press it again to unpin. Ctrl+Win+T by default; you can set your own in Settings.
- **Second hotkey unpins all windows at once** (Ctrl+Shift+Win+T by default). It is also shown next to *Unpin all* in the tray menu, and can be turned off.
- **20 interface languages.** The Windows display language is picked automatically; if it isn't one of the 20, the app uses English. You can also choose a language in Settings.
- **Blue frame** hugs the pinned window (5 px at 100% DPI). On Windows 11 its corners follow the window's own rounded corners; on Windows 10 it is square, like the windows there.
- **Click-through frame** — it never blocks the window's resize edges.
- **Follows the window instantly.** Position comes from WinEvent hooks, not polling, so the frame doesn't lag behind while you drag or resize.
- **Stays in the right layer.** The frame sits directly above its window, so windows above the pinned one cover both.
- **Hides when it should:** window minimized, closed or fullscreen (video, games). For maximized windows the frame is drawn inside.
- **Light.** When idle: about 0% CPU and ~10 MB of memory. The background process is plain Win32 — no WinForms or GDI+ — and the Settings window runs as a separate short-lived process. A 1-second safety timer runs only while something is pinned.
- **Per-monitor DPI aware.**
- Tray icon: double-click opens Settings; the menu has *Settings…*, *Start with Windows*, *Unpin all* and *Exit*. A notification appears on pin/unpin.

## Settings

Open Settings by double-clicking the tray icon, choosing *Settings…* in its menu, or starting `AlwaysOnTop.exe` again while it is already running.

- **Hotkeys:** *Pin or unpin the active window* and *Unpin all windows*. Click *Change* next to one, then press the combination; *Esc* or clicking the button again cancels. A combination needs Ctrl, Alt, Shift or Win plus another key (F1–F24 also work on their own), and the two must differ. *Backspace* turns the *Unpin all* hotkey off; *Default* restores both.
- While waiting for a combination, the window reads keys before other programs and Windows, so a combination that is already taken doesn't trigger — *Save* tells you it is taken instead. Waiting stops as soon as you switch to another window. Combinations Windows reserves for itself (Ctrl+Alt+Del, Win+L) can't be captured.
- **Language:** *Automatic (system language)* or one of the 20 languages.

Settings are stored in `%LOCALAPPDATA%\AlwaysOnTop\settings.ini`. If the pin hotkey is taken by another program at startup, the app says so and opens Settings; if only the *Unpin all* hotkey is taken, you get a notification.

## Languages

English, 简体中文, हिन्दी, Español, Français, العربية, বাংলা, Português, Русский, اردو, Bahasa Indonesia, Deutsch, 日本語, मराठी, తెలుగు, Türkçe, தமிழ், 繁體中文, Tiếng Việt, 한국어.

These are the most spoken languages in the world (by total speakers) that Windows offers as a display language: Nigerian Pidgin, which Windows doesn't have, is replaced by Korean, and Cantonese is covered by Traditional Chinese. Arabic and Urdu use a right-to-left layout.

Translations live in `lang/*.lang` — plain `key=value` text in UTF-8 — and are embedded into the exe at build time. Any string missing from a translation falls back to English. The translations were machine-made and have not been reviewed by native speakers, so corrections are welcome.

To add a language: copy `lang/en.lang` to `lang/<code>.lang`, translate the values, add the code to `Codes` in `Strings.cs` and rebuild.

## Requirements

Windows 10 or 11 with .NET Framework 4.x, which is preinstalled. Developed and tested on Windows 11 (64-bit) only; Windows 10 is untested (see the note at the top).

## Download

Get `AlwaysOnTop.exe` from [Releases](https://github.com/vibegineer/PinHalo/releases/latest) and run it — it is portable, nothing to install, and lives in the tray. To start it with Windows, tick *Start with Windows* in its tray menu. Autostart points to where the exe is right now, so move the exe to its final folder first. Untick the item to remove autostart (this also removes the one `Install.ps1` sets up).

The exe is not code-signed, so Windows SmartScreen may warn on the first run: click *More info* → *Run anyway*. The release notes list its SHA-256 checksum.

## Install from source

```powershell
git clone https://github.com/vibegineer/PinHalo.git
cd PinHalo
powershell -ExecutionPolicy Bypass -File .\Install.ps1
```

`Install.ps1` builds the program, copies it to `%LOCALAPPDATA%\AlwaysOnTop`, adds it to autostart (Startup shortcut and the `HKCU\...\Run` key) and starts it. Running it again updates an existing install and keeps your settings. Autostart entries pass `--autostart`, so the second of the two entries quietly exits instead of opening Settings.

- Build only: `.\Build.ps1` — produces `AlwaysOnTop.exe` next to the sources.
- Uninstall: `.\Uninstall.ps1` (`-WhatIf` shows what it would do without changing anything). It also removes `settings.ini`. If windows are still pinned, choose *Exit* in the tray menu first so they get unpinned.

## Notes and limitations

- Windows of elevated (administrator) apps can't be pinned by a normal-privilege process; you get a notification instead of a frame. Windows blocks this (UIPI).
- The desktop, taskbar and its flyouts are never pinned.
- Virtual desktops are untested. The frame is hidden while Windows cloaks the pinned window, but a frame created on one desktop may not show up on another.
- Multi-monitor setups and display scaling other than 100% are implemented (per-monitor DPI) but have only been tested at 100% on a single monitor.
- Log file: `%TEMP%\AlwaysOnTop.log`.

## Project layout

| File | What it does |
|---|---|
| `AlwaysOnTop.cs` | Entry point, background process (hidden window, hotkey, pinning), the frame overlay |
| `Tray.cs` | Tray icon and its menu |
| `Settings.cs` | Hotkey parsing and formatting, `settings.ini` |
| `SettingsForm.cs` | Settings window, hotkey input fields and the settings process |
| `Strings.cs` | Loads translations, detects the system language |
| `AssemblyInfo.cs` | Version and file properties of the exe |
| `lang/*.lang` | Translations |
| `Build.ps1`, `Install.ps1`, `Uninstall.ps1` | Build, install, uninstall |

## How it works

The background process uses plain Win32 and loads nothing beyond `mscorlib`: a hidden window with its own message loop, `Shell_NotifyIcon` for the tray icon and `TrackPopupMenu` for its menu. The Settings window is the same exe started with `--settings`; WinForms is loaded only in that process, which exits when the window closes. The background process unregisters its hotkeys while Settings is open and reloads `settings.ini` afterwards.

`BorderOverlay` creates one layered, click-through window per pinned window and draws the ring itself into a DIB section: only the ring's pixels, antialiased from the distance to a rounded rectangle (premultiplied alpha, `UpdateLayeredWindow`). A `SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE)` hook limited to the pinned window's thread moves or redraws the frame; a global `EVENT_SYSTEM_FOREGROUND` hook keeps its z-order right. The ring is concentric with the window: inner radius equals the window corner radius (8 px × DPI scale on Windows 11), outer radius adds the thickness.

A hotkey field in Settings installs a `WH_KEYBOARD_LL` hook only while it waits for a combination (after *Change* is clicked), so it sees the combination before any `RegisterHotKey` owner and before the shell. The hook stays until the modifier keys are released, otherwise releasing Win would open the Start menu. Whether a combination is free is checked with a trial `RegisterHotKey`.

## Support the author

If PinHalo is useful to you, you can support the author on [Patreon](https://www.patreon.com/cw/vibegineer).

## License

[MIT](LICENSE)
