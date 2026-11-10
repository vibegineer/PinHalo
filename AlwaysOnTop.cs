// AlwaysOnTop v6 — закрепление окна по хоткею (по умолчанию Ctrl+Win+T) + синяя рамка со скруглениями как у окон Windows 11.
// v6: минимум ресурсов. Основной процесс — чистый WinAPI, без WinForms и GDI+: скрытое окно, значок в трее,
// родное меню, рамка рисуется своим кодом прямо в DIB (только пиксели кольца). Окно настроек (WinForms) —
// отдельный короткоживущий процесс того же exe с ключом --settings: после закрытия его память освобождается целиком.
// Рамка = ОДНО layered-окно на закреплённое: попиксельная альфа, сглаженные скруглённые углы, клики проходят насквозь.
// Положение — по событиям системы (SetWinEventHook), а не опросом. Таймер-страховка (раз в секунду, объединяемый)
// работает только пока что-то закреплено. В простое процесс не просыпается вообще.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

static class Program
{
    // Имя mutex; от него же — имена классов окон (тестовая сборка с другим Id не видит рабочую копию)
    public const string Id = "AlwaysOnTop_CtrlWinT_Singleton";

    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    // Без этого на масштабе != 100% DWM отдаёт физические пиксели, а наши окна ставятся в виртуальных — рамка съезжает.
    static void EnableDpiAwareness()
    {
        try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } // PER_MONITOR_AWARE_V2
        catch (EntryPointNotFoundException) { }
        try { SetProcessDPIAware(); } catch { }
    }

    [STAThread]
    static int Main(string[] args)
    {
        EnableDpiAwareness();
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Info("Fatal: " + e.ExceptionObject);
        // Окно настроек — отдельный процесс: WinForms грузится только в нём
        if (Array.IndexOf(args, "--settings") >= 0) return SettingsApp.Run(Array.IndexOf(args, "--hotkey-busy") >= 0);

        bool created;
        using (var m = new Mutex(true, Id, out created))
        {
            if (!created)
            {
                // Повторный запуск exe открывает настройки работающей копии.
                // Автозагрузка стартует с --autostart: дубль из второго пути автозапуска просто тихо выходит.
                if (Array.IndexOf(args, "--autostart") < 0) App.ShowSettingsInRunningInstance();
                return 0;
            }
            return new App().Run();
        }
    }
}

static class Log
{
    static readonly string LogPath = Path.Combine(Path.GetTempPath(), "AlwaysOnTop.log");
    public static void Info(string s)
    {
        try
        {
            var fi = new FileInfo(LogPath);
            if (fi.Exists && fi.Length > 256 * 1024) fi.Delete(); // лог не должен расти бесконечно
            File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss") + " " + s + "\r\n");
        }
        catch { }
    }
}

// Вызовы WinAPI и мелкие помощники. Только mscorlib — никаких System.Drawing/WinForms в основном процессе.
static class Native
{
    public const int GWL_EXSTYLE = -20, WS_EX_TOPMOST = 0x00000008, WS_EX_TOOLWINDOW = 0x80;
    public const uint WS_POPUP = 0x80000000;
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
        public bool Contains(RECT o) { return o.Left >= Left && o.Top >= Top && o.Right <= Right && o.Bottom <= Bottom; }
        public RECT Inflate(int d) { var r = this; r.Left -= d; r.Top -= d; r.Right += d; r.Bottom += d; return r; }
        public static RECT Intersect(RECT a, RECT b)
        {
            var r = new RECT();
            r.Left = Math.Max(a.Left, b.Left); r.Top = Math.Max(a.Top, b.Top);
            r.Right = Math.Min(a.Right, b.Right); r.Bottom = Math.Min(a.Bottom, b.Bottom);
            return r;
        }
        public override string ToString() { return "{X=" + Left + ",Y=" + Top + ",Width=" + Width + ",Height=" + Height + "}"; }
    }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }
    [StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public int cbSize; public uint style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; public IntPtr hIconSm;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb; public string lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)] public struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public uint dwProcessId, dwThreadId; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OSVERSIONINFO
    {
        public int dwOSVersionInfoSize, dwMajorVersion, dwMinorVersion, dwBuildNumber, dwPlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szCSDVersion;
    }

    // --- окна и сообщения ---
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(int exStyle, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr[] handles, uint ms, uint wakeMask, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, int mod, int vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] public static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr id, uint ms, IntPtr proc);
    [DllImport("user32.dll")] public static extern UIntPtr SetCoalescableTimer(IntPtr hWnd, UIntPtr id, uint ms, IntPtr proc, uint toleranceMs);
    [DllImport("user32.dll")] public static extern bool KillTimer(IntPtr hWnd, UIntPtr id);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr ins, int X, int Y, int cx, int cy, uint f);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int n);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int flags);
    // --- графика (только GDI для DIB-секции) ---
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    // --- DWM, DPI, процессы ---
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT r, int cb);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int v, int cb);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr hMon, int type, out uint dpiX, out uint dpiY);
    [DllImport("ntdll.dll")] static extern int RtlGetVersion(ref OSVERSIONINFO v);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern uint GetModuleFileName(IntPtr h, StringBuilder sb, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr procAttr, IntPtr threadAttr, bool inherit, uint flags,
        IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")] public static extern bool GetExitCodeProcess(IntPtr h, out uint code);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr h, IntPtr min, IntPtr max);

    // В простое отдаём системе неиспользуемые страницы (JIT и старт); при следующем действии подтянутся сами
    public static void TrimMemory()
    {
        GC.Collect();
        SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
    }

    public static string ExePath()
    {
        var sb = new StringBuilder(1024);
        GetModuleFileName(IntPtr.Zero, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string Text(IntPtr h, bool cls)
    {
        var sb = new StringBuilder(256);
        if (cls) GetClassName(h, sb, sb.Capacity); else GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    // Окно класса cls, зарегистрированного этим exe (один раз на процесс)
    public static bool RegisterWindowClass(string cls, WndProc proc)
    {
        var wc = new WNDCLASSEX();
        wc.cbSize = Marshal.SizeOf(typeof(WNDCLASSEX));
        wc.lpfnWndProc = proc;
        wc.hInstance = GetModuleHandle(null);
        wc.lpszClassName = cls;
        return RegisterClassEx(ref wc) != 0;
    }

    // Углы окон скруглены только в Windows 11 (build >= 22000)
    public static readonly bool IsWin11 = GetBuild() >= 22000;
    static int GetBuild()
    {
        try
        {
            var v = new OSVERSIONINFO();
            v.dwOSVersionInfoSize = Marshal.SizeOf(typeof(OSVERSIONINFO));
            return RtlGetVersion(ref v) == 0 ? v.dwBuildNumber : 0;
        }
        catch { return 0; }
    }

    public static int DpiOf(IntPtr monitor)
    {
        try
        {
            uint x, y;
            if (GetDpiForMonitor(monitor, 0, out x, out y) == 0 && x > 0) return (int)x; // MDT_EFFECTIVE_DPI
        }
        catch { }
        return 96;
    }

    // Окно на другом виртуальном рабочем столе «cloaked»: IsWindowVisible для него по-прежнему true
    public static bool IsCloaked(IntPtr hwnd)
    {
        int c;
        return DwmGetWindowAttribute(hwnd, 14, out c, 4) == 0 && c != 0; // DWMWA_CLOAKED
    }

    // Видимая граница окна без невидимой зоны ресайза и тени
    public static RECT GetFrameBounds(IntPtr hwnd)
    {
        RECT r;
        if (DwmGetWindowAttribute(hwnd, 9, out r, Marshal.SizeOf(typeof(RECT))) == 0 && r.Width > 0 && r.Height > 0) // DWMWA_EXTENDED_FRAME_BOUNDS
            return r;
        if (GetWindowRect(hwnd, out r)) return r;
        return new RECT();
    }
}

// Рамка вокруг одного закреплённого окна: одно layered-окно с попиксельной альфой.
// Снаружи — скруглённое кольцо, концентричное углам окна (радиус 8px при 100% DPI, как у окон Windows 11).
// Для развёрнутого окна кольцо рисуется внутри (снаружи экрана его не видно), для полноэкранного — скрыто.
// Рисуем сами в DIB-секцию: только пиксели кольца (полосы у краёв), середина буфера всегда остаётся нулевой.
sealed class BorderOverlay : IDisposable
{
    const int Solid = unchecked((int)0xFF0078D7); // premultiplied BGRA, яркий синий PowerToys (0, 120, 215)
    const int T96 = 5;   // толщина при 100% DPI
    const int R96 = 8;   // радиус скругления окон Windows 11 при 100% DPI
    const uint GW_HWNDPREV = 3;
    const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;
    static readonly string ClassName = Program.Id + "_Frame";
    static Native.WndProc frameProc; // поле: делегат не должен собраться GC, пока класс окна зарегистрирован
    static bool classReady;

    public readonly IntPtr Target;
    public IntPtr Handle { get; private set; }
    public IntPtr HookLocation, HookLifetime; // WinEvent-хуки на поток закреплённого окна, ими владеет App

    IntPtr memDc, dib, oldBmp, bits;
    int capW, capH;
    Native.RECT bounds;
    int curOuterR = -1, curInnerR = -1, curHole = -1;
    bool drawn, drawnFull;
    int drawnW, drawnH, drawnBand, drawnHole;
    int lastRenderTick;
    bool shown;
    IntPtr lastMon;
    int dpi = 96;

    public BorderOverlay(IntPtr target)
    {
        Target = target;
        if (!classReady)
        {
            frameProc = FrameProc;
            classReady = Native.RegisterWindowClass(ClassName, frameProc);
        }
        const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
        Handle = Native.CreateWindowEx(Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TRANSPARENT,
            ClassName, "", Native.WS_POPUP, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, Native.GetModuleHandle(null), IntPtr.Zero);
        if (Handle == IntPtr.Zero) Log.Info("Frame window FAILED " + Marshal.GetLastWin32Error());
    }

    static IntPtr FrameProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        if (msg == 0x0084) return new IntPtr(-1); // WM_NCHITTEST -> HTTRANSPARENT
        return Native.DefWindowProc(h, msg, w, l);
    }

    // Подогнать рамку под текущее состояние окна. Дёшево: перерисовка только при смене размера, иначе — сдвиг.
    public void Sync()
    {
        if (Handle == IntPtr.Zero) return;
        if (!Native.IsWindow(Target) || Native.IsIconic(Target) || !Native.IsWindowVisible(Target) || Native.IsCloaked(Target)) { Hide(); return; }
        Native.RECT r = Native.GetFrameBounds(Target);
        if (r.Width <= 0 || r.Height <= 0) { Hide(); return; }

        IntPtr mon = Native.MonitorFromWindow(Target, 2); // MONITOR_DEFAULTTONEAREST
        var mi = new Native.MONITORINFO();
        mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO));
        Native.RECT m = Native.GetMonitorInfo(mon, ref mi) ? mi.rcMonitor : r;
        if (mon != lastMon) { lastMon = mon; dpi = Native.DpiOf(mon); }

        bool inside = Native.IsZoomed(Target);
        if (!inside && r.Contains(m)) { Hide(); return; } // полноэкранное видео/игра — рамка только мешает

        int t = Math.Max(1, (int)Math.Round(T96 * dpi / 96.0));
        Native.RECT o;
        int outerR = 0, innerR = 0, hole;
        if (inside) { o = Native.RECT.Intersect(r, m); hole = t; }
        else
        {
            int rad = CornerRadius();
            o = r.Inflate(t);
            outerR = rad > 0 ? rad + t : 0;
            innerR = Math.Max(rad - 1, 0);
            hole = t + 1; // внутрь на 1px, чтобы не оставалось шва с собственной границей окна
        }
        if (o.Width <= 0 || o.Height <= 0) { Hide(); return; }

        if (o.Width != bounds.Width || o.Height != bounds.Height || outerR != curOuterR || innerR != curInnerR || hole != curHole)
        {
            EnsureSurface(o.Width, o.Height, true);
            if (bits == IntPtr.Zero) { Hide(); return; }
            Render(o.Width, o.Height, outerR, hole, innerR);
            Present(o);
            curOuterR = outerR; curInnerR = innerR; curHole = hole;
            lastRenderTick = Environment.TickCount;
        }
        else if (o.Left != bounds.Left || o.Top != bounds.Top)
            Native.SetWindowPos(Handle, IntPtr.Zero, o.Left, o.Top, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        bounds = o;

        if (!shown)
        {
            Native.ShowWindow(Handle, SW_SHOWNOACTIVATE);
            shown = true;
            EnsureOrder();
        }
    }

    // Рамка лежит в z-порядке прямо над закреплённым окном: окна выше (другие закреплённые, меню Пуск) перекрывают и её
    public void EnsureOrder()
    {
        if (!shown) return;
        IntPtr prev = Native.GetWindow(Target, GW_HWNDPREV);
        if (prev == Handle) return;
        Native.SetWindowPos(Handle, prev == IntPtr.Zero ? Native.HWND_TOPMOST : prev, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    public void Hide()
    {
        if (!shown) return;
        Native.ShowWindow(Handle, SW_HIDE);
        shown = false;
    }

    int CornerRadius()
    {
        if (!Native.IsWin11) return 0;
        int pref = 0;
        // DWMWA_WINDOW_CORNER_PREFERENCE: 0 по умолчанию, 1 не скруглять, 3 малое скругление
        if (Native.DwmGetWindowAttribute(Target, 33, out pref, 4) == 0)
        {
            if (pref == 1) return 0;
            if (pref == 3) return (int)Math.Round(4.0 * dpi / 96.0);
        }
        return (int)Math.Round(R96 * dpi / 96.0);
    }

    // Ресайз закончился (размер не менялся больше секунды) — буфер с запасом пересоздаём точно по окну.
    // Вызывается таймером-страховкой; рисует тем же путём, что и обычная перерисовка.
    public void Compact()
    {
        if (!shown || dib == IntPtr.Zero || Environment.TickCount - lastRenderTick < 1000) return;
        int w = bounds.Width, h = bounds.Height;
        if ((long)capW * capH <= (long)w * h + w * 16) return; // уже почти точно по размеру
        EnsureSurface(w, h, false);
        if (bits == IntPtr.Zero) { Hide(); return; }
        Render(w, h, curOuterR, curHole, curInnerR);
        Present(bounds);
    }

    // DIB-секция. Во время ресайза — с запасом, чтобы не выделять память на каждый кадр; потом Compact() ужмёт её.
    void EnsureSurface(int w, int h, bool withSlack)
    {
        bool fits = dib != IntPtr.Zero && w <= capW && h <= capH;
        bool wasteful = dib != IntPtr.Zero && (long)w * h * 4 < (long)capW * capH; // занято < 1/4 буфера
        if (withSlack && fits && !wasteful) return;
        FreeSurface();
        capW = withSlack ? w + w / 4 + 16 : w;
        capH = withSlack ? h + h / 4 + 16 : h;
        var bmi = new Native.BITMAPINFOHEADER();
        bmi.biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
        bmi.biWidth = capW; bmi.biHeight = -capH; // top-down
        bmi.biPlanes = 1; bmi.biBitCount = 32;
        dib = Native.CreateDIBSection(IntPtr.Zero, ref bmi, 0, out bits, IntPtr.Zero, 0); // память уже обнулена системой
        if (dib == IntPtr.Zero) { bits = IntPtr.Zero; Log.Info("CreateDIBSection FAILED"); return; }
        memDc = Native.CreateCompatibleDC(IntPtr.Zero);
        oldBmp = Native.SelectObject(memDc, dib);
        drawn = false;
    }

    void FreeSurface()
    {
        if (memDc != IntPtr.Zero) { Native.SelectObject(memDc, oldBmp); Native.DeleteDC(memDc); memDc = IntPtr.Zero; }
        if (dib != IntPtr.Zero) { Native.DeleteObject(dib); dib = IntPtr.Zero; }
        bits = IntPtr.Zero;
        drawn = false;
    }

    // ---- своя отрисовка кольца: расстояние до скруглённого прямоугольника -> сглаживание на 1px ----
    static double SdRoundRect(double px, double py, double l, double t, double r, double b, double rad)
    {
        double cx = (l + r) * 0.5, cy = (t + b) * 0.5;
        double hx = (r - l) * 0.5 - rad, hy = (b - t) * 0.5 - rad;
        double qx = Math.Abs(px - cx) - hx, qy = Math.Abs(py - cy) - hy;
        double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
        return Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - rad;
    }

    static int Pixel(double cover)
    {
        if (cover <= 0) return 0;
        if (cover >= 1) return Solid;
        int a = (int)(cover * 255 + 0.5);
        return (a << 24) | ((120 * a / 255) << 8) | (215 * a / 255); // premultiplied alpha
    }

    void Put(int x, int y, int value) { Marshal.WriteInt32(bits, (y * capW + x) * 4, value); }

    // Обходит те же пиксели, что занимает кольцо: верхнюю и нижнюю полосы целиком, а между ними — только левый и правый края
    void Walk(int w, int h, int band, int hole, bool full, Func<int, int, int> value)
    {
        for (int y = 0; y < h; y++)
        {
            if (full || y < band || y >= h - band)
                for (int x = 0; x < w; x++) Put(x, y, value(x, y));
            else
            {
                for (int x = 0; x < hole; x++) Put(x, y, value(x, y));
                for (int x = w - hole; x < w; x++) Put(x, y, value(x, y));
            }
        }
    }

    void Render(int w, int h, int outerR, int hole, int innerR)
    {
        if (drawn) Walk(drawnW, drawnH, drawnBand, drawnHole, drawnFull, (x, y) => 0); // стираем прошлое кольцо, а не весь буфер

        double ro = Math.Min(outerR, Math.Min(w, h) * 0.5);
        double iw = w - 2.0 * hole, ih = h - 2.0 * hole;
        double ri = Math.Min(innerR, Math.Max(0, Math.Min(iw, ih) * 0.5));
        int band = Math.Max(outerR, hole + innerR) + 1;   // строки, где есть скругления
        bool full = band * 2 >= h || hole * 2 + 2 >= w;   // крошечное окно — считаем все пиксели

        Walk(w, h, band, hole, full, (x, y) =>
        {
            double px = x + 0.5, py = y + 0.5;
            double outer = Math.Min(1, Math.Max(0, 0.5 - SdRoundRect(px, py, 0, 0, w, h, ro)));
            if (outer <= 0) return 0;
            if (iw <= 0 || ih <= 0) return Pixel(outer);
            double inner = Math.Min(1, Math.Max(0, 0.5 - SdRoundRect(px, py, hole, hole, w - hole, h - hole, ri)));
            return Pixel(outer * (1 - inner));
        });
        drawn = true; drawnFull = full; drawnW = w; drawnH = h; drawnBand = band; drawnHole = hole;
    }

    void Present(Native.RECT o)
    {
        var dst = new Native.POINT(); dst.X = o.Left; dst.Y = o.Top;
        var size = new Native.SIZE(); size.cx = o.Width; size.cy = o.Height;
        var src = new Native.POINT();
        var bf = new Native.BLENDFUNCTION(); bf.SourceConstantAlpha = 255; bf.AlphaFormat = 1; // AC_SRC_OVER + AC_SRC_ALPHA
        Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, memDc, ref src, 0, ref bf, 2); // ULW_ALPHA
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero) { Native.DestroyWindow(Handle); Handle = IntPtr.Zero; }
        FreeSurface();
    }
}

// Основной процесс: скрытое окно, хоткей, трей, закрепление. Свой цикл сообщений, без WinForms.
sealed class App
{
    public static readonly string WindowClass = Program.Id + "_Main";
    public const uint WM_APP_SETTINGS = 0x8002; // WM_APP + 2: повторный запуск exe просит открыть настройки
    const int HOTKEY_ID = 1;
    const uint WM_DESTROY = 0x0002, WM_ENDSESSION = 0x0016, WM_HOTKEY = 0x0312, WM_TIMER = 0x0113, WM_QUIT = 0x0012;
    const uint WM_CONTEXTMENU = 0x007B, WM_RBUTTONUP = 0x0205, WM_LBUTTONDBLCLK = 0x0203;
    const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_HIDE = 0x8003, EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    const uint WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
    const int OBJID_WINDOW = 0;
    static readonly UIntPtr TimerId = new UIntPtr(1);

    static Native.WndProc wndProc;           // поля: делегаты не должны собраться GC, пока ими пользуется Windows
    static Native.WinEventProc winEventProc;
    static App current;

    readonly Dictionary<IntPtr, BorderOverlay> pinned = new Dictionary<IntPtr, BorderOverlay>();
    Settings settings;
    TrayIcon tray;
    IntPtr hwnd, foregroundHook, child;
    uint childPid, taskbarCreated;
    bool hotkeyOk, timerOn, stopped;
    int trayCount = -1;

    // Повторный запуск: находим окно работающей копии и просим открыть настройки
    public static void ShowSettingsInRunningInstance()
    {
        IntPtr h = Native.FindWindow(WindowClass, null);
        if (h == IntPtr.Zero) return;
        uint pid;
        Native.GetWindowThreadProcessId(h, out pid);
        Native.AllowSetForegroundWindow((int)pid); // иначе Windows не выведет окно настроек на передний план
        Native.PostMessage(h, WM_APP_SETTINGS, IntPtr.Zero, IntPtr.Zero);
    }

    public int Run()
    {
        current = this;
        // Программа почти ничего не выделяет: фоновая (параллельная) сборка мусора и её поток ей не нужны
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.Batch;
        settings = Settings.Load();
        Loc.Init(settings.Language);
        wndProc = WndProcStatic;
        winEventProc = WinEventStatic;
        if (!Native.RegisterWindowClass(WindowClass, wndProc)) { Log.Info("RegisterClass FAILED"); return 1; }
        hwnd = Native.CreateWindowEx(Native.WS_EX_TOOLWINDOW, WindowClass, "Always on Top", Native.WS_POPUP,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, Native.GetModuleHandle(null), IntPtr.Zero);
        if (hwnd == IntPtr.Zero) { Log.Info("Main window FAILED " + Marshal.GetLastWin32Error()); return 1; }
        taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated"); // Проводник перезапустился — значок надо добавить заново

        tray = new TrayIcon(hwnd);
        UpdateTray();
        hotkeyOk = RegisterHotkey(); // NOREPEAT: удержание клавиш не щёлкает pin/unpin по кругу
        if (hotkeyOk) Log.Info("Started, hotkey " + Hotkey.Serialize(settings.Modifiers, settings.Key) + " registered, language " + Loc.Code);
        else
        {
            Log.Info("RegisterHotKey FAILED " + Hotkey.Serialize(settings.Modifiers, settings.Key));
            OpenSettings(true); // окно настроек само сообщит, что сочетание занято
        }
        Native.TrimMemory();
        Loop();
        Stop();
        return 0;
    }

    // Цикл сообщений. Пока открыт процесс настроек — ждём и сообщения, и его завершение (без лишних потоков).
    void Loop()
    {
        Native.MSG msg;
        while (true)
        {
            if (child == IntPtr.Zero)
            {
                if (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) <= 0) return;
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
                continue;
            }
            uint r = Native.MsgWaitForMultipleObjectsEx(1, new[] { child }, 0xFFFFFFFF, 0x04FF, 0x4); // QS_ALLINPUT, MWMO_INPUTAVAILABLE
            if (r == 0) OnSettingsClosed();
            while (Native.PeekMessage(out msg, IntPtr.Zero, 0, 0, 1)) // PM_REMOVE
            {
                if (msg.message == WM_QUIT) return;
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
            }
        }
    }

    static IntPtr WndProcStatic(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        try { if (current != null && current.OnMessage(msg, w, l)) return IntPtr.Zero; }
        catch (Exception ex) { Log.Info("Unhandled: " + ex); } // ошибка не должна ронять программу в трее
        return Native.DefWindowProc(h, msg, w, l);
    }

    bool OnMessage(uint msg, IntPtr w, IntPtr l)
    {
        switch (msg)
        {
            case WM_HOTKEY: if (w.ToInt32() == HOTKEY_ID) ToggleForeground(); return true;
            case WM_TIMER: RefreshBorders(); return true;
            case TrayIcon.CallbackMessage: OnTray(w, l); return true;
            case WM_APP_SETTINGS: OpenSettings(false); return true;
            case WM_ENDSESSION: if (w != IntPtr.Zero) Stop(); return true;
            case WM_DESTROY: Native.PostQuitMessage(0); return true;
        }
        if (taskbarCreated != 0 && msg == taskbarCreated) { if (tray != null) { tray.Add(); trayCount = -1; UpdateTray(); } return true; }
        return false;
    }

    void OnTray(IntPtr w, IntPtr l)
    {
        uint evt = (uint)(l.ToInt64() & 0xFFFF);
        if (evt == WM_LBUTTONDBLCLK) { OpenSettings(false); return; }
        bool menu = tray.Version4 ? evt == WM_CONTEXTMENU : evt == WM_RBUTTONUP;
        if (!menu) return;
        int x, y;
        if (tray.Version4) { x = (short)(w.ToInt64() & 0xFFFF); y = (short)((w.ToInt64() >> 16) & 0xFFFF); }
        else TrayIcon.CursorPos(out x, out y);
        int cmd = tray.ShowMenu(x, y, new[] { Loc.T("Menu.Settings"), Loc.T("Menu.UnpinAll"), null, Loc.T("Menu.Exit") }, Loc.IsRtl);
        if (cmd == 1) OpenSettings(false);
        else if (cmd == 2) UnpinAll();
        else if (cmd == 4) { UnpinAll(); Native.DestroyWindow(hwnd); }
    }

    static void WinEventStatic(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try { if (current != null) current.OnWinEvent(evt, hwnd, idObject, idChild); }
        catch (Exception ex) { Log.Info("Unhandled: " + ex); }
    }

    // Приходит в нашем потоке (WINEVENT_OUTOFCONTEXT). Хуки на перемещение и жизнь окна стоят только на поток закреплённого окна.
    void OnWinEvent(uint evt, IntPtr target, int idObject, int idChild)
    {
        if (evt == EVENT_SYSTEM_FOREGROUND)
        {
            foreach (var ov in pinned.Values) ov.EnsureOrder();
            return;
        }
        if (idObject != OBJID_WINDOW || idChild != 0) return; // каретка, курсор, дочерние элементы
        BorderOverlay o;
        if (!pinned.TryGetValue(target, out o)) return;
        if (evt == EVENT_OBJECT_DESTROY) { Release(target); return; } // окно закрыли — рамка исчезает сразу
        o.Sync(); // перемещение, ресайз, показ/скрытие
    }

    bool RegisterHotkey() { return Native.RegisterHotKey(hwnd, HOTKEY_ID, settings.Modifiers | Hotkey.MOD_NOREPEAT, settings.Key); }

    string Title(IntPtr h)
    {
        string t = Native.Text(h, false);
        return t.Length == 0 ? Loc.T("Toast.Untitled") : t;
    }

    // Рабочий стол, панель задач и её всплывающие окна закреплять незачем — они и так живут поверх всего.
    static bool IsShellWindow(IntPtr h)
    {
        string c = Native.Text(h, true);
        return c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd"
            || c == "NotifyIconOverflowWindow" || c == "TopLevelWindowForOverflowXamlIsland";
    }

    void ToggleForeground()
    {
        IntPtr h = Native.GetForegroundWindow();
        if (h == IntPtr.Zero || h == hwnd) return;
        if (!Native.IsWindow(h) || IsShellWindow(h)) return;
        foreach (var ov in pinned.Values) if (ov.Handle == h) return; // свою рамку не трогаем

        if (pinned.ContainsKey(h)) { Unpin(h); tray.Balloon("Always on Top", Loc.T("Toast.Unpinned", Title(h)), false); Log.Info("Unpin " + h); }
        else if (Pin(h)) { tray.Balloon("Always on Top", Loc.T("Toast.Pinned", Title(h)), false); Log.Info("Pin " + h + " rect=" + Native.GetFrameBounds(h)); }
        else { tray.Balloon("Always on Top", Loc.T("Toast.PinFailed", Title(h)), true); Log.Info("Pin FAILED " + h); }
        UpdateTray();
    }

    bool Pin(IntPtr h)
    {
        Native.SetWindowPos(h, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        // Окна с повышенными правами (UIPI) молча игнорируют SetWindowPos — проверяем результат, а не рисуем рамку впустую.
        if ((Native.GetWindowLong(h, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) == 0) return false;

        var ov = new BorderOverlay(h);
        uint pid;
        uint tid = Native.GetWindowThreadProcessId(h, out pid);
        const uint flags = WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS;
        ov.HookLocation = Native.SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, winEventProc, pid, tid, flags);
        ov.HookLifetime = Native.SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_HIDE, IntPtr.Zero, winEventProc, pid, tid, flags); // закрытие, показ, скрытие
        if (ov.HookLocation == IntPtr.Zero) Log.Info("SetWinEventHook FAILED, рамка обновляется только таймером");
        if (foregroundHook == IntPtr.Zero)
            foregroundHook = Native.SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, winEventProc, 0, 0, flags);
        pinned[h] = ov;
        ov.Sync();
        StartTimer();
        return true;
    }

    // Страховка на то, о чём событий нет (потеря topmost, «cloaked»): раз в секунду, Windows может сдвинуть его на 0,5 с,
    // чтобы разбудить процесс вместе с другими таймерами
    void StartTimer()
    {
        if (timerOn) return;
        try { Native.SetCoalescableTimer(hwnd, TimerId, 1000, IntPtr.Zero, 500); }
        catch (EntryPointNotFoundException) { Native.SetTimer(hwnd, TimerId, 1000, IntPtr.Zero); } // до Windows 8
        timerOn = true;
    }

    void StopTimer()
    {
        if (!timerOn) return;
        Native.KillTimer(hwnd, TimerId);
        timerOn = false;
    }

    // Убрать рамку и хуки (окно остаётся как есть)
    void Release(IntPtr h)
    {
        BorderOverlay ov;
        if (!pinned.TryGetValue(h, out ov)) return;
        pinned.Remove(h);
        if (ov.HookLocation != IntPtr.Zero) Native.UnhookWinEvent(ov.HookLocation);
        if (ov.HookLifetime != IntPtr.Zero) Native.UnhookWinEvent(ov.HookLifetime);
        ov.Dispose();
        if (pinned.Count == 0)
        {
            StopTimer();
            if (foregroundHook != IntPtr.Zero) { Native.UnhookWinEvent(foregroundHook); foregroundHook = IntPtr.Zero; }
            if (!stopped) Native.TrimMemory(); // буфер рамки уже освобождён
        }
        UpdateTray();
    }

    void Unpin(IntPtr h)
    {
        Native.SetWindowPos(h, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        Release(h);
    }

    void UnpinAll()
    {
        foreach (var k in new List<IntPtr>(pinned.Keys)) Unpin(k);
        UpdateTray();
    }

    void UpdateTray()
    {
        if (tray == null || trayCount == pinned.Count) return; // не дёргаем шелл, если ничего не изменилось
        trayCount = pinned.Count;
        tray.SetTip(trayCount == 0 ? "Always on Top (" + Hotkey.Format(settings.Modifiers, settings.Key) + ")" : Loc.T("Tray.Pinned", trayCount));
    }

    void RefreshBorders()
    {
        foreach (var kv in new List<KeyValuePair<IntPtr, BorderOverlay>>(pinned))
        {
            if (!Native.IsWindow(kv.Key) || (Native.GetWindowLong(kv.Key, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) == 0)
            {
                Release(kv.Key);
                continue;
            }
            kv.Value.Sync();
            kv.Value.EnsureOrder();
            kv.Value.Compact();
        }
    }

    // ---- настройки: отдельный процесс того же exe ----
    void OpenSettings(bool hotkeyBusy)
    {
        if (child != IntPtr.Zero) { FocusChild(); return; }
        // Пока окно открыто, хоткей снят: иначе нажатие текущего сочетания сработало бы как команда, а не как ввод
        if (hotkeyOk) { Native.UnregisterHotKey(hwnd, HOTKEY_ID); hotkeyOk = false; }
        var cmd = new StringBuilder("\"" + Native.ExePath() + "\" --settings" + (hotkeyBusy ? " --hotkey-busy" : ""));
        var si = new Native.STARTUPINFO();
        si.cb = Marshal.SizeOf(typeof(Native.STARTUPINFO));
        Native.PROCESS_INFORMATION pi;
        Native.AllowSetForegroundWindow(-1); // ASFW_ANY: окно настроек должно выйти на передний план
        if (!Native.CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, null, ref si, out pi))
        {
            Log.Info("Settings process FAILED " + Marshal.GetLastWin32Error());
            hotkeyOk = RegisterHotkey();
            return;
        }
        Native.CloseHandle(pi.hThread);
        child = pi.hProcess;
        childPid = pi.dwProcessId;
    }

    void FocusChild()
    {
        Native.EnumWindows((h, l) =>
        {
            uint pid;
            Native.GetWindowThreadProcessId(h, out pid);
            if (pid != childPid || !Native.IsWindowVisible(h)) return true;
            Native.SetForegroundWindow(h);
            return false;
        }, IntPtr.Zero);
    }

    void OnSettingsClosed()
    {
        uint code;
        if (!Native.GetExitCodeProcess(child, out code)) code = 1;
        Native.CloseHandle(child);
        child = IntPtr.Zero;
        if (code == 0) { settings = Settings.Load(); Loc.Init(settings.Language); } // 0 — сохранили, иначе всё как было
        hotkeyOk = RegisterHotkey();
        if (!hotkeyOk)
            tray.Balloon("Always on Top", Loc.T("Err.HotkeyRegister", Hotkey.Format(settings.Modifiers, settings.Key)), true);
        trayCount = -1; // пересобрать подсказку: могли поменяться язык и сочетание
        UpdateTray();
    }

    void Stop()
    {
        if (stopped) return;
        stopped = true;
        StopTimer();
        foreach (var k in new List<IntPtr>(pinned.Keys)) Release(k);
        if (hotkeyOk) Native.UnregisterHotKey(hwnd, HOTKEY_ID);
        if (tray != null) tray.Dispose(); // иначе значок «призраком» висит в трее до наведения мыши
        if (child != IntPtr.Zero) { Native.CloseHandle(child); child = IntPtr.Zero; }
    }
}
