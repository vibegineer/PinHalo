# PinHalo

[Русская версия](README.ru.md)

Pin any window on top of everything else with **Ctrl+Win+T**. A blue frame with Windows 11-style rounded corners shows which window is pinned.

The program is a single C# file, has no dependencies and builds with the compiler that ships with Windows — no SDK needed. In the tray it is called *Always on Top*.

## Features

- **Ctrl+Win+T** toggles "always on top" for the active window. Press it again to unpin.
- **Blue frame** hugs the pinned window (5 px at 100% DPI). On Windows 11 its corners follow the window's own rounded corners; on Windows 10 it is square.
- **Click-through frame** — it never blocks the window's resize edges.
- **Follows the window instantly.** Position comes from WinEvent hooks, not polling, so the frame doesn't lag behind while you drag or resize.
- **Stays in the right layer.** The frame sits directly above its window, so windows above the pinned one cover both.
- **Hides when it should:** window minimized, closed or fullscreen (video, games). For maximized windows the frame is drawn inside.
- **Light.** About 0% CPU when idle; a 500 ms safety timer runs only while something is pinned.
- **Per-monitor DPI aware.**
- Tray menu: *Unpin all* and *Exit*. A notification appears on pin/unpin.

## Requirements

64-bit Windows with .NET Framework 4.x, which is preinstalled. Developed and tested on Windows 11. Windows 10 should work too (the frame has square corners there), but it has not been tested.

## Install

```powershell
git clone https://github.com/vibegineer/PinHalo.git
cd PinHalo
powershell -ExecutionPolicy Bypass -File .\Install.ps1
```

`Install.ps1` builds the program, copies it to `%LOCALAPPDATA%\AlwaysOnTop`, adds it to autostart (Startup shortcut and the `HKCU\...\Run` key) and starts it. Running it again updates an existing install.

- Build only: `.\Build.ps1` — produces `AlwaysOnTop.exe` next to the sources.
- Uninstall: `.\Uninstall.ps1` (`-WhatIf` shows what it would do without changing anything). If windows are still pinned, choose *Exit* in the tray menu first so they get unpinned.

## Notes and limitations

- Windows of elevated (administrator) apps can't be pinned by a normal-privilege process; you get a notification instead of a frame. Windows blocks this (UIPI).
- The desktop, taskbar and its flyouts are never pinned.
- The hotkey is fixed. To change it, edit `VK_T` and the modifiers in the `RegisterHotKey` call in `AlwaysOnTop.cs`, then rebuild.
- Virtual desktops are untested. The frame is hidden while Windows cloaks the pinned window, but a frame created on one desktop may not show up on another.
- Multi-monitor setups and display scaling other than 100% are implemented (per-monitor DPI) but have only been tested at 100% on a single monitor.
- Log file: `%TEMP%\AlwaysOnTop.log`.

## How it works

`BorderOverlay` creates one layered, click-through window per pinned window and draws a rounded ring into a DIB section with GDI+ (premultiplied alpha, `UpdateLayeredWindow`). A `SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE)` hook limited to the pinned window's thread moves or redraws the frame; a global `EVENT_SYSTEM_FOREGROUND` hook keeps its z-order right. The ring is concentric with the window: inner radius equals the window corner radius (8 px × DPI scale on Windows 11), outer radius adds the thickness.
