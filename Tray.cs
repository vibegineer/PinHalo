using System;
using System.Runtime.InteropServices;

// Значок в трее на чистом Shell_NotifyIcon и родное меню Windows (TrackPopupMenu).
// WinForms NotifyIcon/ContextMenuStrip тянули за собой десятки МБ библиотек ради одного значка.
sealed class TrayIcon : IDisposable
{
    public const uint CallbackMessage = 0x8001; // WM_APP + 1
    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    const uint NIIF_INFO = 0x1, NIIF_WARNING = 0x2;
    const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800;
    const uint TPM_RIGHTBUTTON = 0x2, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100, TPM_LAYOUTRTL = 0x8000;
    const int IDI_APP_ICON = 32512; // так csc /win32icon называет группу иконок в exe

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public int cbSize; public IntPtr hWnd; public uint uID, uFlags, uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadImage(IntPtr inst, IntPtr name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern IntPtr LoadIcon(IntPtr inst, IntPtr name);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
    [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);

    readonly IntPtr hwnd;
    IntPtr icon;
    bool ownIcon;
    string tip = string.Empty;

    // NOTIFYICON_VERSION_4: правый щелчок приходит как WM_CONTEXTMENU с координатами (и с клавиатуры тоже)
    public bool Version4 { get; private set; }

    public TrayIcon(IntPtr hwnd)
    {
        this.hwnd = hwnd;
        // Иконка нужного для трея размера прямо из ресурсов exe (16 px при 100%, больше при крупном масштабе)
        icon = LoadImage(Native.GetModuleHandle(null), new IntPtr(IDI_APP_ICON), 1, GetSystemMetrics(49), GetSystemMetrics(50), 0); // IMAGE_ICON, SM_CXSMICON, SM_CYSMICON
        ownIcon = icon != IntPtr.Zero;
        if (!ownIcon) icon = LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION — запасной вариант
        Add();
    }

    NOTIFYICONDATA Data(uint flags)
    {
        var d = new NOTIFYICONDATA();
        d.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA));
        d.hWnd = hwnd; d.uID = 1; d.uFlags = flags;
        d.szTip = string.Empty; d.szInfo = string.Empty; d.szInfoTitle = string.Empty;
        return d;
    }

    static string Cut(string s, int max) { return s.Length < max ? s : s.Substring(0, max - 1); }

    // Добавить значок. Вызывается и после перезапуска Проводника (сообщение TaskbarCreated): старые значки он забывает.
    public void Add()
    {
        var d = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        d.uCallbackMessage = CallbackMessage;
        d.hIcon = icon;
        d.szTip = tip;
        if (!Shell_NotifyIcon(NIM_ADD, ref d)) Log.Info("Tray icon add failed (taskbar not ready yet?)");
        d.uVersion = 4;
        Version4 = Shell_NotifyIcon(NIM_SETVERSION, ref d);
    }

    public void SetTip(string text)
    {
        tip = Cut(text, 128);
        var d = Data(NIF_TIP | NIF_SHOWTIP);
        d.szTip = tip;
        Shell_NotifyIcon(NIM_MODIFY, ref d);
    }

    // Уведомление у значка (в Windows 10/11 — системный тост)
    public void Balloon(string title, string text, bool warning)
    {
        var d = Data(NIF_INFO);
        d.szInfoTitle = Cut(title, 64);
        d.szInfo = Cut(text, 256);
        d.dwInfoFlags = warning ? NIIF_WARNING : NIIF_INFO;
        Shell_NotifyIcon(NIM_MODIFY, ref d);
    }

    public static void CursorPos(out int x, out int y)
    {
        POINT p;
        GetCursorPos(out p);
        x = p.X; y = p.Y;
    }

    // Родное меню Windows. items: null — разделитель. Возвращает номер пункта с 1 (0 — меню закрыли).
    public int ShowMenu(int x, int y, string[] items, bool rtl)
    {
        IntPtr m = CreatePopupMenu();
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] == null) AppendMenu(m, MF_SEPARATOR, UIntPtr.Zero, null);
            else AppendMenu(m, MF_STRING, new UIntPtr((uint)(i + 1)), items[i]);
        }
        Native.SetForegroundWindow(hwnd); // без этого меню не закрывается щелчком мимо
        int cmd = TrackPopupMenuEx(m, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY | (rtl ? TPM_LAYOUTRTL : 0), x, y, hwnd, IntPtr.Zero);
        Native.PostMessage(hwnd, 0, IntPtr.Zero, IntPtr.Zero); // WM_NULL: известный обход, чтобы меню корректно закрылось
        DestroyMenu(m);
        return cmd;
    }

    public void Dispose()
    {
        var d = Data(0);
        Shell_NotifyIcon(NIM_DELETE, ref d);
        if (ownIcon) DestroyIcon(icon);
        icon = IntPtr.Zero;
    }
}
