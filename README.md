# PinHalo

[Русская версия](README.ru.md)

Pin any window on top of everything else with a hotkey (**Ctrl+Win+T** by default). A blue frame with Windows 11-style rounded corners shows which window is pinned.

A small C# tray app with no dependencies. It builds with the compiler that ships with Windows — no SDK needed. In the tray it is called *Always on Top*.

> **Tested on Windows 11 only — not tested on Windows 10.** The code has a separate Windows 10 path (square frame corners, since windows there are square), but it was only checked by forcing it on Windows 11. Reports from Windows 10 users are welcome.

## Features

- **Hotkey** toggles "always on top" for the active window. Press it again to unpin. Ctrl+Win+T by default; you can set your own in Settings.
- **20 interface languages.** The Windows display language is picked automatically; if it isn't one of the 20, the app uses English. You can also choose a language in Settings.
- **Blue frame** hugs the pinned window (5 px at 100% DPI). On Windows 11 its corners follow the window's own rounded corners; on Windows 10 it is square, like the windows there.
- **Click-through frame** — it never blocks the window's resize edges.
- **Follows the window instantly.** Position comes from WinEvent hooks, not polling, so the frame doesn't lag behind while you drag or resize.
- **Stays in the right layer.** The frame sits directly above its window, so windows above the pinned one cover both.
- **Hides when it should:** window minimized, closed or fullscreen (video, games). For maximized windows the frame is drawn inside.
- **Light.** About 0% CPU when idle; a 500 ms safety timer runs only while something is pinned.
- **Per-monitor DPI aware.**
- Tray icon: double-click opens Settings; the menu has *Settings…*, *Unpin all* and *Exit*. A notification appears on pin/unpin.

## Settings

Open Settings by double-clicking the tray icon, choosing *Settings…* in its menu, or starting `AlwaysOnTop.exe` again while it is already running.

- **Hotkey:** click the box and press the combination. It needs Ctrl, Alt, Shift or Win plus another key (F1–F24 also work on their own). While the box has focus it reads keys before other programs and Windows, so pressing a combination that is already taken doesn't trigger it — *Save* tells you it is taken instead. Combinations Windows reserves for itself (Ctrl+Alt+Del, Win+L) can't be captured.
- **Language:** *Automatic (system language)* or one of the 20 languages.

Settings are stored in `%LOCALAPPDATA%\AlwaysOnTop\settings.ini`. If the saved hotkey is taken by another program at startup, the app says so and opens Settings.

## Languages

English, 简体中文, हिन्दी, Español, Français, العربية, বাংলা, Português, Русский, اردو, Bahasa Indonesia, Deutsch, 日本語, मराठी, తెలుగు, Türkçe, தமிழ், 繁體中文, Tiếng Việt, 한국어.

These are the most spoken languages in the world (by total speakers) that Windows offers as a display language: Nigerian Pidgin, which Windows doesn't have, is replaced by Korean, and Cantonese is covered by Traditional Chinese. Arabic and Urdu use a right-to-left layout.

Translations live in `lang/*.lang` — plain `key=value` text in UTF-8 — and are embedded into the exe at build time. Any string missing from a translation falls back to English. The translations were machine-made and have not been reviewed by native speakers, so corrections are welcome.

To add a language: copy `lang/en.lang` to `lang/<code>.lang`, translate the values, add the code to `Codes` in `Strings.cs` and rebuild.

## Requirements

Windows 10 or 11 with .NET Framework 4.x, which is preinstalled. Developed and tested on Windows 11 (64-bit) only; Windows 10 is untested (see the note at the top).

## Install

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
| `AlwaysOnTop.cs` | Entry point, tray icon, hotkey, pinning, the frame overlay |
| `Settings.cs` | Hotkey parsing and formatting, `settings.ini` |
| `SettingsForm.cs` | Settings window and the hotkey capture box |
| `Strings.cs` | Loads translations, detects the system language |
| `lang/*.lang` | Translations |
| `Build.ps1`, `Install.ps1`, `Uninstall.ps1` | Build, install, uninstall |

## How it works

`BorderOverlay` creates one layered, click-through window per pinned window and draws a rounded ring into a DIB section with GDI+ (premultiplied alpha, `UpdateLayeredWindow`). A `SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE)` hook limited to the pinned window's thread moves or redraws the frame; a global `EVENT_SYSTEM_FOREGROUND` hook keeps its z-order right. The ring is concentric with the window: inner radius equals the window corner radius (8 px × DPI scale on Windows 11), outer radius adds the thickness.

The hotkey box in Settings installs a `WH_KEYBOARD_LL` hook only while it has focus, so it sees a combination before any `RegisterHotKey` owner and before the shell. Whether a combination is free is checked with a trial `RegisterHotKey`.
