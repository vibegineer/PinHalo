// AlwaysOnTop v4 — Ctrl+Win+T + синяя рамка со скруглениями как у окон Windows 11
// Рамка = ОДНО окно на закреплённое: layered с попиксельной альфой (сглаженные скруглённые углы),
// клики проходят насквозь. Позиция обновляется по событиям системы (SetWinEventHook), а не опросом —
// рамка идёт за окном без отставания. Сторожевой таймер 500мс нужен только пока есть закрепления
// (закрытие окна, потеря topmost, z-order).
// Ресурсы: 0% CPU в простое.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class Program
{
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
    static void Main()
    {
        bool created;
        using (var m = new Mutex(true, "AlwaysOnTop_CtrlWinT_Singleton", out created))
        {
            if (!created) return;
            EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var f = new MainForm())
                Application.Run(f);
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

// Общие вызовы WinAPI и мелкие помощники
static class Native
{
    public const int GWL_EXSTYLE = -20, WS_EX_TOPMOST = 0x00000008;
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; public Rectangle ToRect() { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } }
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
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OSVERSIONINFO
    {
        public int dwOSVersionInfoSize, dwMajorVersion, dwMinorVersion, dwBuildNumber, dwPlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szCSDVersion;
    }

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
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT r, int cb);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int v, int cb);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr hMon, int type, out uint dpiX, out uint dpiY);
    [DllImport("ntdll.dll")] static extern int RtlGetVersion(ref OSVERSIONINFO v);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr h, IntPtr min, IntPtr max);

    // В простое отдаём системе неиспользуемые страницы (JIT и WinForms-хвосты); при следующем действии подтянутся сами
    public static void TrimMemory()
    {
        GC.Collect();
        SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
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
    public static Rectangle GetFrameBounds(IntPtr hwnd)
    {
        RECT r;
        if (DwmGetWindowAttribute(hwnd, 9, out r, Marshal.SizeOf(typeof(RECT))) == 0) // DWMWA_EXTENDED_FRAME_BOUNDS
        {
            var rc = r.ToRect();
            if (rc.Width > 0 && rc.Height > 0) return rc;
        }
        if (GetWindowRect(hwnd, out r)) return r.ToRect();
        return Rectangle.Empty;
    }
}

// Рамка вокруг одного закреплённого окна: одно layered-окно с попиксельной альфой.
// Снаружи — скруглённое кольцо, концентричное углам окна (радиус 8px при 100% DPI, как у окон Windows 11).
// Для развёрнутого окна кольцо рисуется внутри (снаружи экрана его не видно), для полноэкранного — скрыто.
sealed class BorderOverlay : NativeWindow, IDisposable
{
    public static readonly Color BorderColor = Color.FromArgb(0, 120, 215); // яркий синий PowerToys
    const int T96 = 5;   // толщина при 100% DPI
    const int R96 = 8;   // радиус скругления окон Windows 11 при 100% DPI
    const uint GW_HWNDPREV = 3;
    const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;

    public readonly IntPtr Target;
    public IntPtr Hook; // WinEvent-хук на поток закреплённого окна, им владеет MainForm

    IntPtr memDc, dib, oldBmp;
    Bitmap surface;
    int capW, capH;
    Rectangle bounds = Rectangle.Empty;
    int curOuterR, curInnerR, curHole;
    bool shown;
    IntPtr lastMon;
    int dpi = 96;

    public BorderOverlay(IntPtr target)
    {
        Target = target;
        const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
        var cp = new CreateParams();
        cp.ClassName = "Static";
        cp.Style = unchecked((int)0x80000000); // WS_POPUP
        cp.ExStyle = Native.WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TRANSPARENT;
        cp.X = cp.Y = 0; cp.Width = cp.Height = 1;
        CreateHandle(cp);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; } // WM_NCHITTEST -> HTTRANSPARENT
        base.WndProc(ref m);
    }

    // Подогнать рамку под текущее состояние окна. Дёшево: перерисовка только при смене размера, иначе — сдвиг.
    public void Sync()
    {
        if (Handle == IntPtr.Zero) return;
        if (!Native.IsWindow(Target) || Native.IsIconic(Target) || !Native.IsWindowVisible(Target) || Native.IsCloaked(Target)) { Hide(); return; }
        Rectangle r = Native.GetFrameBounds(Target);
        if (r.Width <= 0 || r.Height <= 0) { Hide(); return; }

        IntPtr mon = Native.MonitorFromWindow(Target, 2); // MONITOR_DEFAULTTONEAREST
        var mi = new Native.MONITORINFO();
        mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO));
        Rectangle m = Native.GetMonitorInfo(mon, ref mi) ? mi.rcMonitor.ToRect() : r;
        if (mon != lastMon) { lastMon = mon; dpi = Native.DpiOf(mon); }

        bool inside = Native.IsZoomed(Target);
        if (!inside && r.Contains(m)) { Hide(); return; } // полноэкранное видео/игра — рамка только мешает

        int t = Math.Max(1, (int)Math.Round(T96 * dpi / 96.0));
        Rectangle o;
        int outerR = 0, innerR = 0, hole;
        if (inside) { o = Rectangle.Intersect(r, m); hole = t; }
        else
        {
            int rad = CornerRadius();
            o = Rectangle.Inflate(r, t, t);
            outerR = rad > 0 ? rad + t : 0;
            innerR = Math.Max(rad - 1, 0);
            hole = t + 1; // внутрь на 1px, чтобы не оставалось шва с собственной границей окна
        }

        if (o.Size != bounds.Size || outerR != curOuterR || innerR != curInnerR || hole != curHole)
        {
            EnsureSurface(o.Width, o.Height);
            Render(o.Width, o.Height, outerR, hole, innerR);
            Present(o);
            curOuterR = outerR; curInnerR = innerR; curHole = hole;
        }
        else if (o.Location != bounds.Location)
            Native.SetWindowPos(Handle, IntPtr.Zero, o.X, o.Y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
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

    // DIB-секция с запасом по размеру: при ресайзе не выделяем память на каждый кадр
    void EnsureSurface(int w, int h)
    {
        bool fits = surface != null && w <= capW && h <= capH;
        bool wasteful = surface != null && (long)w * h * 4 < (long)capW * capH; // занято < 1/4 буфера
        if (fits && !wasteful) return;
        FreeSurface();
        capW = w + w / 4 + 16; capH = h + h / 4 + 16;
        var bmi = new Native.BITMAPINFOHEADER();
        bmi.biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
        bmi.biWidth = capW; bmi.biHeight = -capH; // top-down
        bmi.biPlanes = 1; bmi.biBitCount = 32;
        IntPtr bits;
        dib = Native.CreateDIBSection(IntPtr.Zero, ref bmi, 0, out bits, IntPtr.Zero, 0);
        memDc = Native.CreateCompatibleDC(IntPtr.Zero);
        oldBmp = Native.SelectObject(memDc, dib);
        surface = new Bitmap(capW, capH, capW * 4, PixelFormat.Format32bppPArgb, bits); // рисуем прямо в память DIB
    }

    void FreeSurface()
    {
        if (surface != null) { surface.Dispose(); surface = null; }
        if (memDc != IntPtr.Zero) { Native.SelectObject(memDc, oldBmp); Native.DeleteDC(memDc); memDc = IntPtr.Zero; }
        if (dib != IntPtr.Zero) { Native.DeleteObject(dib); dib = IntPtr.Zero; }
    }

    static void AddRound(GraphicsPath p, float x, float y, float w, float h, float r)
    {
        p.StartFigure();
        r = Math.Min(r, Math.Min(w, h) / 2f);
        if (r <= 0.5f) { p.AddRectangle(new RectangleF(x, y, w, h)); return; }
        float d = r * 2;
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
    }

    void Render(int w, int h, int outerR, int hole, int innerR)
    {
        using (var g = Graphics.FromImage(surface))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            using (var clear = new SolidBrush(Color.Transparent)) g.FillRectangle(clear, 0, 0, w, h);
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (var path = new GraphicsPath()) // FillMode.Alternate: внутренний контур вырезает «дырку»
            using (var fill = new SolidBrush(BorderColor))
            {
                AddRound(path, 0, 0, w, h, outerR);
                if (w > hole * 2 && h > hole * 2) AddRound(path, hole, hole, w - hole * 2, h - hole * 2, innerR);
                g.FillPath(fill, path);
            }
        }
    }

    void Present(Rectangle o)
    {
        var dst = new Native.POINT(); dst.X = o.X; dst.Y = o.Y;
        var size = new Native.SIZE(); size.cx = o.Width; size.cy = o.Height;
        var src = new Native.POINT();
        var bf = new Native.BLENDFUNCTION(); bf.SourceConstantAlpha = 255; bf.AlphaFormat = 1; // AC_SRC_OVER + AC_SRC_ALPHA
        Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, memDc, ref src, 0, ref bf, 2); // ULW_ALPHA
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero) DestroyHandle();
        FreeSurface();
    }
}

sealed class MainForm : Form
{
    const int MOD_CONTROL = 0x0002, MOD_WIN = 0x0008, MOD_NOREPEAT = 0x4000; // NOREPEAT: удержание клавиш не щёлкает pin/unpin по кругу
    const int VK_T = 0x54;
    const int WM_HOTKEY = 0x0312;
    const int HOTKEY_ID = 1;
    const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    const uint WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
    const int OBJID_WINDOW = 0;

    delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, int mod, int vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern int GetClassName(IntPtr hWnd, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")]
    static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);

    readonly Dictionary<IntPtr, BorderOverlay> pinned = new Dictionary<IntPtr, BorderOverlay>();
    readonly WinEventProc winEventProc; // поле, чтобы делегат не собрал GC, пока хук жив
    readonly System.Windows.Forms.Timer timer;
    readonly NotifyIcon tray;
    IntPtr foregroundHook;
    int trayCount = -1;

    public MainForm()
    {
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0;
        Size = new Size(1, 1);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-2000, -2000);
        Load += (s, e) => { Hide(); Visible = false; BeginInvoke(new Action(Native.TrimMemory)); };

        winEventProc = OnWinEvent;

        // Страховка: основную работу делают события, таймер ловит закрытие окна, потерю topmost и сбои z-order
        timer = new System.Windows.Forms.Timer();
        timer.Interval = 500;
        timer.Tick += (s, e) => RefreshBorders();

        tray = new NotifyIcon();
        tray.Visible = true;
        tray.Icon = LoadTrayIcon();
        tray.Text = "Always on Top (Ctrl+Win+T)";
        var menu = new ContextMenuStrip();
        menu.Items.Add("Снять все закрепления", null, (s, e) => UnpinAll());
        menu.Items.Add("Выход", null, (s, e) => { UnpinAll(); Application.Exit(); });
        tray.ContextMenuStrip = menu;

        if (!RegisterHotKey(Handle, HOTKEY_ID, MOD_CONTROL | MOD_WIN | MOD_NOREPEAT, VK_T))
        {
            Log.Info("RegisterHotKey FAILED");
            MessageBox.Show("Не удалось зарегистрировать Ctrl+Win+T.", "Always on Top",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else Log.Info("Started, hotkey registered");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID) { ToggleForeground(); return; }
        base.WndProc(ref m);
    }

    // Приходит в нашем UI-потоке (WINEVENT_OUTOFCONTEXT). Хук стоит только на поток закреплённого окна.
    void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (evt == EVENT_SYSTEM_FOREGROUND)
        {
            foreach (var ov in pinned.Values) ov.EnsureOrder();
            return;
        }
        if (idObject != OBJID_WINDOW || idChild != 0) return; // каретка, курсор, дочерние элементы
        BorderOverlay o;
        if (pinned.TryGetValue(hwnd, out o)) o.Sync();
    }

    static Icon LoadTrayIcon()
    {
        try
        {
            using (var s = typeof(Program).Assembly.GetManifestResourceStream("AlwaysOnTop.ico"))
                if (s != null) return new Icon(s, SystemInformation.SmallIconSize);
        }
        catch (Exception ex) { Log.Info("Tray icon load failed: " + ex.Message); }
        return SystemIcons.Application;
    }

    string Title(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetWindowText(h, sb, 256);
        return sb.Length == 0 ? "(окно без названия)" : sb.ToString();
    }

    // Рабочий стол, панель задач и её всплывающие окна закреплять незачем — они и так живут поверх всего.
    static bool IsShellWindow(IntPtr h)
    {
        var sb = new StringBuilder(64);
        GetClassName(h, sb, 64);
        string c = sb.ToString();
        return c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd"
            || c == "NotifyIconOverflowWindow" || c == "TopLevelWindowForOverflowXamlIsland";
    }

    void ToggleForeground()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == Handle) return;
        if (!Native.IsWindow(hwnd) || IsShellWindow(hwnd)) return;
        foreach (var ov in pinned.Values) if (ov.Handle == hwnd) return; // свою рамку не трогаем

        if (pinned.ContainsKey(hwnd)) { Unpin(hwnd); tray.ShowBalloonTip(800, "Always on Top", "Откреплено: " + Title(hwnd), ToolTipIcon.Info); Log.Info("Unpin " + hwnd); }
        else if (Pin(hwnd)) { tray.ShowBalloonTip(800, "Always on Top", "Закреплено поверх всех: " + Title(hwnd), ToolTipIcon.Info); Log.Info("Pin " + hwnd + " rect=" + Native.GetFrameBounds(hwnd)); }
        else { tray.ShowBalloonTip(1500, "Always on Top", "Не удалось закрепить (окно запущено от администратора?): " + Title(hwnd), ToolTipIcon.Warning); Log.Info("Pin FAILED " + hwnd); }
        UpdateTray();
    }

    bool Pin(IntPtr hwnd)
    {
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        // Окна с повышенными правами (UIPI) молча игнорируют SetWindowPos — проверяем результат, а не рисуем рамку впустую.
        if ((Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) == 0) return false;

        var ov = new BorderOverlay(hwnd);
        uint pid;
        uint tid = GetWindowThreadProcessId(hwnd, out pid);
        ov.Hook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, winEventProc,
            pid, tid, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (ov.Hook == IntPtr.Zero) Log.Info("SetWinEventHook FAILED, рамка обновляется только таймером");
        if (foregroundHook == IntPtr.Zero)
            foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, winEventProc,
                0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        pinned[hwnd] = ov;
        ov.Sync();
        if (!timer.Enabled) timer.Start();
        return true;
    }

    // Убрать рамку и хуки (окно остаётся как есть)
    void Release(IntPtr hwnd)
    {
        BorderOverlay ov;
        if (!pinned.TryGetValue(hwnd, out ov)) return;
        pinned.Remove(hwnd);
        if (ov.Hook != IntPtr.Zero) UnhookWinEvent(ov.Hook);
        ov.Dispose();
        if (pinned.Count == 0)
        {
            timer.Stop();
            if (foregroundHook != IntPtr.Zero) { UnhookWinEvent(foregroundHook); foregroundHook = IntPtr.Zero; }
            BeginInvoke(new Action(Native.TrimMemory)); // буфер рамки уже освобождён
        }
        UpdateTray();
    }

    void Unpin(IntPtr hwnd)
    {
        Native.SetWindowPos(hwnd, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        Release(hwnd);
    }

    void UnpinAll()
    {
        foreach (var k in new List<IntPtr>(pinned.Keys)) Unpin(k);
        UpdateTray();
    }

    void UpdateTray()
    {
        if (trayCount == pinned.Count) return; // не дёргаем шелл, если ничего не изменилось
        trayCount = pinned.Count;
        tray.Text = trayCount == 0 ? "Always on Top (Ctrl+Win+T)" : ("Закреплено: " + trayCount);
    }

    void RefreshBorders()
    {
        List<IntPtr> dead = null;
        foreach (var kv in pinned)
        {
            if (!Native.IsWindow(kv.Key) || (Native.GetWindowLong(kv.Key, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) == 0)
            {
                if (dead == null) dead = new List<IntPtr>();
                dead.Add(kv.Key);
                continue;
            }
            kv.Value.Sync();
            kv.Value.EnsureOrder();
        }
        if (dead != null) foreach (var h in dead) Release(h);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try { timer.Stop(); } catch { }
        UnregisterHotKey(Handle, HOTKEY_ID);
        foreach (var k in new List<IntPtr>(pinned.Keys)) Release(k);
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // Иначе иконка «призраком» висит в трее до наведения мыши.
        tray.Visible = false;
        tray.Dispose();
        timer.Dispose();
        base.OnFormClosed(e);
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; }
    }
}
