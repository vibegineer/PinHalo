using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Поле захвата сочетания клавиш: показывает и запоминает нажатую комбинацию.
// Пока поле в фокусе, стоит низкоуровневый перехват клавиатуры (WH_KEYBOARD_LL). Он видит сочетание раньше
// чужих хоткеев и самой Windows и не даёт им сработать: иначе занятое сочетание просто «не вводилось бы»,
// а Win+D при попытке ввода свернул бы все окна.
sealed class HotkeyBox : TextBox
{
    delegate IntPtr LowLevelProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public int vkCode, scanCode, flags, time; public IntPtr extra; }
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, LowLevelProc proc, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern bool HideCaret(IntPtr hWnd);
    const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;

    public int Modifiers { get; private set; }
    public int Key { get; private set; }
    public event Action<string> Rejected;
    readonly LowLevelProc hookProc;                  // поле: делегат не должен собраться GC, пока хук стоит
    readonly HashSet<int> held = new HashSet<int>(); // зажатые модификаторы (левый и правый отдельно)
    IntPtr hook;
    bool captured;

    public HotkeyBox(int mods, int key)
    {
        Modifiers = mods; Key = key;
        hookProc = OnHook;
        ReadOnly = true;
        ShortcutsEnabled = false;
        BackColor = SystemColors.Window;
        TextAlign = HorizontalAlignment.Center;
        RightToLeft = RightToLeft.No; // «Ctrl + Win + T» всегда слева направо, даже в арабском и урду
        ShowCurrent();
    }

    public void Set(int mods, int key) { Modifiers = mods; Key = key; captured = true; ShowCurrent(); }

    void ShowCurrent() { ForeColor = SystemColors.WindowText; Text = Hotkey.Format(Modifiers, Key); }
    void ShowPrompt() { ForeColor = SystemColors.GrayText; Text = Loc.T("Settings.Press"); }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        HideCaret(Handle); // печатать в поле нельзя — мигающий курсор только путает
        captured = false;
        held.Clear();
        ShowPrompt();
        if (hook == IntPtr.Zero) hook = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) Log.Info("Keyboard hook FAILED, hotkey box falls back to normal key events");
    }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Unhook(); ShowCurrent(); }
    protected override void OnHandleDestroyed(EventArgs e) { Unhook(); base.OnHandleDestroyed(e); }
    void Unhook() { if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } }

    static int ModBit(int vk)
    {
        switch (vk)
        {
            case 0x11: case 0xA2: case 0xA3: return Hotkey.MOD_CONTROL;
            case 0x12: case 0xA4: case 0xA5: return Hotkey.MOD_ALT;
            case 0x10: case 0xA0: case 0xA1: return Hotkey.MOD_SHIFT;
            case 0x5B: case 0x5C: return Hotkey.MOD_WIN;
            default: return 0;
        }
    }

    IntPtr OnHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return CallNextHookEx(hook, code, wParam, lParam);
        var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
        int msg = wParam.ToInt32();
        bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
        if (ModBit(k.vkCode) != 0)
        {
            if (down) held.Add(k.vkCode); else held.Remove(k.vkCode);
            int mods = 0;
            foreach (int vk in held) mods |= ModBit(vk);
            if (down) OnModifiers(mods);
            else if (held.Count == 0 && !captured) ShowPrompt(); // отпустили модификаторы, не выбрав клавишу
            return new IntPtr(1);
        }
        // Голые Tab/Esc/Enter — навигация по диалогу (следующее поле, отмена, сохранить)
        if (held.Count == 0 && (k.vkCode == 0x09 || k.vkCode == 0x1B || k.vkCode == 0x0D))
            return CallNextHookEx(hook, code, wParam, lParam);
        if (down)
        {
            int mods = 0;
            foreach (int vk in held) mods |= ModBit(vk);
            OnCombo(mods, k.vkCode);
        }
        return new IntPtr(1); // проглатываем: пока выбираем, чужие хоткеи и сочетания Windows не срабатывают
    }

    void OnModifiers(int mods)
    {
        if (captured) return;
        ForeColor = SystemColors.GrayText;
        Text = Hotkey.Preview(mods);
    }

    void OnCombo(int mods, int vk)
    {
        if (!Hotkey.IsAllowed(mods, vk))
        {
            ShowPrompt();
            if (Rejected != null) Rejected(Loc.T("Err.NeedModifier"));
            return;
        }
        Set(mods, vk);
        if (Rejected != null) Rejected(string.Empty);
    }

    // Запасной путь — обычные события клавиатуры (если перехват не поставился). Голые Tab/Esc/Enter остаются навигацией.
    protected override bool IsInputKey(Keys keyData)
    {
        Keys k = keyData & Keys.KeyCode;
        if ((keyData & Keys.Modifiers) == 0 && (k == Keys.Tab || k == Keys.Escape || k == Keys.Return)) return false;
        return true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;
        if (hook != IntPtr.Zero) return; // всё уже обработал перехват
        bool win = GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0;
        int mods = (e.Control ? Hotkey.MOD_CONTROL : 0) | (e.Alt ? Hotkey.MOD_ALT : 0)
                 | (e.Shift ? Hotkey.MOD_SHIFT : 0) | (win ? Hotkey.MOD_WIN : 0);
        int vk = (int)e.KeyCode;
        if (Hotkey.IsModifierKey(vk)) OnModifiers(mods);
        else OnCombo(mods, vk);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (hook == IntPtr.Zero && !captured && Focused) ShowPrompt();
    }
}

// Окно настроек: хоткей и язык. Раскладка считается вручную от DPI и размера шрифта, чтобы работать и при масштабе экрана > 100%.
sealed class SettingsForm : Form
{
    sealed class LangItem
    {
        public readonly string Code, Text;
        public LangItem(string code, string text) { Code = code; Text = text; }
        public override string ToString() { return Text; }
    }

    readonly Func<int, int, bool> canRegister;
    readonly HotkeyBox box;
    readonly ComboBox lang;
    readonly Label note;
    readonly string hint;

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    readonly bool reopened;

    public int Modifiers { get { return box.Modifiers; } }
    public int Key { get { return box.Key; } }
    public string Language { get; private set; }

    // Окно всегда строится на текущем языке Loc. Выбор другого языка закрывает окно с DialogResult.Retry,
    // и MainForm сразу открывает его заново на новом языке: так верно зеркалятся и арабский/урду, которые Windows
    // разворачивает только при создании окна.
    // canRegister: свободно ли сочетание в системе (его проверяет главное окно).
    // location: где стояло прежнее окно при пересоздании (null — первое открытие, по центру экрана).
    public SettingsForm(int mods, int key, string language, Icon icon, Func<int, int, bool> canRegister, Point? location)
    {
        this.canRegister = canRegister;
        Language = language;
        reopened = location.HasValue;

        float dpi;
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX;
        Func<int, int> S = v => (int)Math.Round(v * dpi / 96f);

        Font = SystemFonts.MessageBoxFont;
        AutoScaleMode = AutoScaleMode.None;
        Text = "Always on Top — " + Loc.T("Settings.Title");
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        if (reopened) { StartPosition = FormStartPosition.Manual; Location = location.Value; }
        else StartPosition = FormStartPosition.CenterScreen;
        RightToLeft = Loc.IsRtl ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = Loc.IsRtl; // зеркалит окно целиком, поэтому координаты ниже задаём как для LTR

        int W = S(400), M = S(16), CW = W - 2 * M, y = M;
        int line = Font.Height;
        int fieldH = line + S(10);
        var flags = TextFormatFlags.WordBreak;

        Controls.Add(MakeLabel(Loc.T("Settings.Hotkey"), M, y, CW, line, SystemColors.ControlText));
        y += line + S(6);

        string defText = Loc.T("Settings.Default");
        int defW = Math.Max(S(90), TextRenderer.MeasureText(defText, Font).Width + S(28));
        box = new HotkeyBox(mods, key);
        box.SetBounds(M, y, CW - defW - S(8), fieldH);
        Controls.Add(box);
        int boxH = box.Height; // высоту однострочного TextBox задаёт шрифт, а не SetBounds
        var reset = new Button { Text = defText, UseVisualStyleBackColor = true };
        reset.SetBounds(M + CW - defW, y - 1, defW, boxH + 2);
        reset.Click += (o, e) => { box.Set(Hotkey.DefaultMods, Hotkey.DefaultKey); ShowError(string.Empty); };
        Controls.Add(reset);
        y += boxH + S(8);

        // Подсказка и ошибки — в одной подписи; высота по самому длинному тексту, чтобы окно не прыгало
        hint = Loc.T("Settings.HotkeyHint");
        int noteH = 0;
        foreach (string sample in new[] { hint, Loc.T("Err.NeedModifier"), Loc.T("Err.HotkeyBusy", "Ctrl + Alt + Shift + Win + F12") })
            noteH = Math.Max(noteH, TextRenderer.MeasureText(sample, Font, new Size(CW, 0), flags).Height);
        note = MakeLabel(hint, M, y, CW, noteH, SystemColors.GrayText);
        Controls.Add(note);
        y += noteH + S(14);

        Controls.Add(MakeLabel(Loc.T("Settings.Language"), M, y, CW, line, SystemColors.ControlText));
        y += line + S(6);

        lang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        lang.Items.Add(new LangItem("auto", Loc.T("Settings.LangAuto")));
        foreach (string c in Loc.AllCodes) lang.Items.Add(new LangItem(c, Loc.NativeName(c)));
        lang.SelectedIndex = 0;
        foreach (LangItem it in lang.Items) if (it.Code == language) lang.SelectedItem = it;
        lang.SetBounds(M, y, CW, fieldH);
        lang.SelectionChangeCommitted += OnLanguagePicked;
        Controls.Add(lang);
        y += lang.Height + S(20);

        string saveText = Loc.T("Settings.Save"), cancelText = Loc.T("Settings.Cancel");
        int bh = fieldH + S(6);
        int cw = Math.Max(S(88), TextRenderer.MeasureText(cancelText, Font).Width + S(28));
        int sw = Math.Max(S(88), TextRenderer.MeasureText(saveText, Font).Width + S(28));
        var cancel = new Button { Text = cancelText, DialogResult = DialogResult.Cancel, UseVisualStyleBackColor = true };
        cancel.SetBounds(W - M - cw, y, cw, bh);
        var save = new Button { Text = saveText, UseVisualStyleBackColor = true };
        save.SetBounds(W - M - cw - S(8) - sw, y, sw, bh);
        save.Click += OnSave;
        Controls.Add(cancel);
        Controls.Add(save);
        AcceptButton = save;
        CancelButton = cancel;
        y += bh + M;

        ClientSize = new Size(W, y);
        box.Rejected += ShowError;
        if (reopened) ActiveControl = lang; // фокус остаётся в списке: стрелками можно листать языки дальше
        Shown += (o, e) => { TopMost = true; TopMost = false; Activate(); }; // окно открывается из трея — выводим его вперёд
    }

    // Пересоздание окна на том же месте не должно «мигать» анимацией закрытия/открытия
    void DisableTransitions()
    {
        int on = 1;
        try { DwmSetWindowAttribute(Handle, 3, ref on, 4); } catch (DllNotFoundException) { } // DWMWA_TRANSITIONS_FORCEDISABLED
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (reopened) DisableTransitions();
    }

    void OnLanguagePicked(object sender, EventArgs e)
    {
        string code = ((LangItem)lang.SelectedItem).Code;
        if (code == Language) return;
        Language = code;
        DisableTransitions();
        DialogResult = DialogResult.Retry; // MainForm откроет окно заново на выбранном языке
    }

    Label MakeLabel(string text, int x, int y, int w, int h, Color color)
    {
        var l = new Label { Text = text, AutoSize = false, ForeColor = color };
        l.SetBounds(x, y, w, h);
        return l;
    }

    void ShowError(string text)
    {
        bool isError = !string.IsNullOrEmpty(text);
        note.Text = isError ? text : hint;
        note.ForeColor = isError ? Color.Firebrick : SystemColors.GrayText;
    }

    void OnSave(object sender, EventArgs e)
    {
        if (!canRegister(box.Modifiers, box.Key))
        {
            ShowError(Loc.T("Err.HotkeyBusy", Hotkey.Format(box.Modifiers, box.Key)));
            return;
        }
        Language = ((LangItem)lang.SelectedItem).Code;
        DialogResult = DialogResult.OK;
    }
}
