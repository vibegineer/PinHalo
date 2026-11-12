using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Одно сочетание в окне настроек: поле с текущим сочетанием и кнопка «Изменить».
// Сочетание ждём только после нажатия кнопки (или щелчка по полю). На время ввода ставится низкоуровневый
// перехват клавиатуры (WH_KEYBOARD_LL): он видит сочетание раньше чужих хоткеев и самой Windows и не даёт им
// сработать — иначе занятое сочетание просто «не вводилось бы», а Win+D при попытке ввода свернул бы все окна.
// Esc, Enter или повторное нажатие кнопки отменяют ввод; Backspace отключает необязательное сочетание.
sealed class HotkeyField
{
    delegate IntPtr LowLevelProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public int vkCode, scanCode, flags, time; public IntPtr extra; }
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, LowLevelProc proc, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;

    public readonly Label Box;
    public readonly Button Button;
    public readonly bool Optional;             // можно отключить (Backspace во время ввода)
    public int Modifiers { get; private set; }
    public int Key { get; private set; }       // 0 — сочетание отключено
    public bool Capturing { get; private set; }
    public bool HookActive { get { return hook != IntPtr.Zero; } }
    public event Action<string> Message;       // текст для общей подписи; "" — вернуть подсказку
    public event EventHandler Started, Finished;

    readonly LowLevelProc hookProc;                  // поле: делегат не должен собраться GC, пока хук стоит
    readonly HashSet<int> held = new HashSet<int>(); // зажатые модификаторы (левый и правый отдельно)
    IntPtr hook;
    bool draining;                                   // сочетание принято, ждём отпускания модификаторов

    public HotkeyField(int mods, int key, bool optional)
    {
        Modifiers = mods; Key = key; Optional = optional;
        hookProc = OnHook;
        Box = new Label
        {
            AutoSize = false, BorderStyle = BorderStyle.FixedSingle, BackColor = SystemColors.Window,
            TextAlign = ContentAlignment.MiddleCenter, UseMnemonic = false,
            RightToLeft = RightToLeft.No // «Ctrl + Win + T» всегда слева направо, даже в арабском и урду
        };
        Box.Click += (o, e) => Start();
        Button = new Button { Text = Loc.T("Settings.Change"), UseVisualStyleBackColor = true };
        Button.Click += (o, e) => { if (Capturing) Stop(); else Start(); };
        ShowCurrent();
    }

    public void Set(int mods, int key) { Stop(); Modifiers = mods; Key = key; ShowCurrent(); }

    void ShowCurrent() { Box.ForeColor = SystemColors.WindowText; Box.Text = Key == 0 ? Loc.T("Settings.None") : Hotkey.Format(Modifiers, Key); }
    void ShowPrompt() { Box.ForeColor = SystemColors.GrayText; Box.Text = Loc.T("Settings.Press"); }
    void Say(string text) { if (Message != null) Message(text); }

    public void Start()
    {
        if (Capturing) return;
        Unhook(); // мог остаться от прошлого ввода, пока отпускали модификаторы
        Capturing = true;
        held.Clear();
        Button.Text = Loc.T("Settings.Cancel");
        ShowPrompt();
        hook = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) Log.Info("Keyboard hook FAILED, hotkey input falls back to normal key events");
        Say(string.Empty);
        if (Started != null) Started(this, EventArgs.Empty);
    }

    // Закончить ввод. drain: если модификаторы ещё зажаты, перехват остаётся до их отпускания —
    // иначе отпускание Win дошло бы до системы и открыло меню «Пуск».
    void Finish(bool drain)
    {
        if (!Capturing) return;
        Capturing = false;
        if (drain && held.Count > 0) draining = true; else Unhook();
        Button.Text = Loc.T("Settings.Change");
        ShowCurrent();
        if (Finished != null) Finished(this, EventArgs.Empty);
    }

    public void Stop() { Finish(false); }

    void Unhook()
    {
        draining = false;
        if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
    }

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

    int HeldMods()
    {
        int mods = 0;
        foreach (int vk in held) mods |= ModBit(vk);
        return mods;
    }

    IntPtr OnHook(int code, IntPtr wParam, IntPtr lParam)
    {
        IntPtr h = hook;
        if (code < 0) return CallNextHookEx(h, code, wParam, lParam);
        var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
        int msg = wParam.ToInt32();
        bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
        int vk = k.vkCode;

        if (draining)
        {
            if (ModBit(vk) != 0 && !down)
            {
                held.Remove(vk);
                if (held.Count == 0) Unhook();
                return new IntPtr(1); // глотаем отпускание модификатора, нажатие которого тоже проглотили
            }
            Unhook();
            return CallNextHookEx(h, code, wParam, lParam);
        }
        if (!Capturing) return CallNextHookEx(h, code, wParam, lParam);

        if (ModBit(vk) != 0)
        {
            if (down) { held.Add(vk); Preview(HeldMods()); }
            else { held.Remove(vk); if (held.Count == 0) ShowPrompt(); } // отпустили модификаторы, не выбрав клавишу
            return new IntPtr(1);
        }
        if (held.Count == 0)
        {
            if (vk == 0x09) { if (down) Stop(); return CallNextHookEx(h, code, wParam, lParam); } // Tab — дальше по окну
            if (vk == 0x1B || vk == 0x0D) { if (down) Stop(); return new IntPtr(1); }          // Esc, Enter — отменить ввод
            if (vk == 0x08 || vk == 0x2E) { if (down) Clear(); return new IntPtr(1); }          // Backspace, Delete
        }
        if (down) Combo(HeldMods(), vk);
        return new IntPtr(1); // проглатываем: пока ждём сочетание, чужие хоткеи и сочетания Windows не срабатывают
    }

    void Preview(int mods)
    {
        Box.ForeColor = SystemColors.GrayText;
        Box.Text = Hotkey.Preview(mods);
    }

    void Clear()
    {
        if (!Optional) { Say(Loc.T("Err.NeedModifier")); return; }
        Modifiers = 0; Key = 0;
        Finish(false);
        Say(string.Empty);
    }

    void Combo(int mods, int vk)
    {
        if (!Hotkey.IsAllowed(mods, vk))
        {
            ShowPrompt();
            Say(Loc.T("Err.NeedModifier"));
            return;
        }
        Modifiers = mods; Key = vk;
        Finish(true);
        Say(string.Empty);
    }

    // Запасной путь, если перехват не поставился: клавиши приходят окну настроек обычным образом
    public bool FallbackKey(Keys keyData)
    {
        if (!Capturing || HookActive) return false;
        int vk = (int)(keyData & Keys.KeyCode);
        bool win = GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0;
        int mods = ((keyData & Keys.Control) != 0 ? Hotkey.MOD_CONTROL : 0) | ((keyData & Keys.Alt) != 0 ? Hotkey.MOD_ALT : 0)
                 | ((keyData & Keys.Shift) != 0 ? Hotkey.MOD_SHIFT : 0) | (win ? Hotkey.MOD_WIN : 0);
        if (Hotkey.IsModifierKey(vk)) { Preview(mods); return true; }
        if (mods == 0)
        {
            if (vk == 0x09) { Stop(); return false; }
            if (vk == 0x1B || vk == 0x0D) { Stop(); return true; }
            if (vk == 0x08 || vk == 0x2E) { Clear(); return true; }
        }
        Combo(mods, vk);
        return true;
    }

    public void Dispose() { Capturing = false; Unhook(); }
}

// Окно настроек: два сочетания и язык. Раскладка считается вручную от DPI и размера шрифта, чтобы работать и при масштабе экрана > 100%.
sealed class SettingsForm : Form
{
    sealed class LangItem
    {
        public readonly string Code, Text;
        public LangItem(string code, string text) { Code = code; Text = text; }
        public override string ToString() { return Text; }
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    readonly Func<int, int, bool> canRegister;
    readonly HotkeyField pin, unpin;
    readonly ComboBox lang;
    readonly Label note;
    readonly Button save;
    readonly string hint;
    readonly bool reopened;

    public int Modifiers { get { return pin.Modifiers; } }
    public int Key { get { return pin.Key; } }
    public int UnpinModifiers { get { return unpin.Modifiers; } }
    public int UnpinKey { get { return unpin.Key; } }
    public string Language { get; private set; }

    // Окно всегда строится на текущем языке Loc. Выбор другого языка закрывает окно с DialogResult.Retry,
    // и SettingsApp сразу открывает его заново на новом языке: так верно зеркалятся и арабский/урду, которые Windows
    // разворачивает только при создании окна.
    // canRegister: свободно ли сочетание в системе.
    // location: где стояло прежнее окно при пересоздании (null — первое открытие, по центру экрана).
    public SettingsForm(int mods, int key, int unpinMods, int unpinKey, string language, Icon icon, Func<int, int, bool> canRegister, Point? location)
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
        KeyPreview = true;
        if (reopened) { StartPosition = FormStartPosition.Manual; Location = location.Value; }
        else StartPosition = FormStartPosition.CenterScreen;
        RightToLeft = Loc.IsRtl ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = Loc.IsRtl; // зеркалит окно целиком, поэтому координаты ниже задаём как для LTR

        int W = S(420), M = S(16), CW = W - 2 * M, y = M;
        int line = Font.Height;
        int fieldH = line + S(12);
        var flags = TextFormatFlags.WordBreak;

        // Кнопка меняет надпись «Изменить» ↔ «Отмена» — ширина по более длинной
        int btnW = Math.Max(S(96), Math.Max(TextRenderer.MeasureText(Loc.T("Settings.Change"), Font).Width,
                                             TextRenderer.MeasureText(Loc.T("Settings.Cancel"), Font).Width) + S(28));
        pin = new HotkeyField(mods, key, false);
        unpin = new HotkeyField(unpinMods, unpinKey, true);
        foreach (var f in new[] { pin, unpin })
        {
            string caption = Loc.T(f == pin ? "Settings.HotkeyPin" : "Settings.HotkeyUnpinAll");
            int capH = TextRenderer.MeasureText(caption, Font, new Size(CW, 0), flags).Height;
            Controls.Add(MakeLabel(caption, M, y, CW, capH, SystemColors.ControlText));
            y += capH + S(6);
            f.Box.SetBounds(M, y, CW - btnW - S(8), fieldH);
            f.Button.SetBounds(M + CW - btnW, y - 1, btnW, fieldH + 2);
            Controls.Add(f.Box);
            Controls.Add(f.Button);
            y += fieldH + S(14);
        }
        HotkeyField a = pin, b = unpin;
        a.Started += (o, e) => b.Stop(); // ждём только одно сочетание за раз
        b.Started += (o, e) => a.Stop();

        // Подсказка и ошибки — в одной подписи; высота по самому длинному тексту, чтобы окно не прыгало
        hint = Loc.T("Settings.HotkeyHint");
        int noteH = 0;
        foreach (string sample in new[] { hint, Loc.T("Err.NeedModifier"), Loc.T("Err.HotkeyBusy", "Ctrl + Alt + Shift + Win + F12"), Loc.T("Err.HotkeySame") })
            noteH = Math.Max(noteH, TextRenderer.MeasureText(sample, Font, new Size(CW, 0), flags).Height);
        note = MakeLabel(hint, M, y - S(4), CW, noteH, SystemColors.GrayText);
        Controls.Add(note);
        y += noteH + S(10);

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

        string saveText = Loc.T("Settings.Save"), cancelText = Loc.T("Settings.Cancel"), defText = Loc.T("Settings.Default");
        int bh = fieldH + S(4);
        int cw = Math.Max(S(88), TextRenderer.MeasureText(cancelText, Font).Width + S(28));
        int sw = Math.Max(S(88), TextRenderer.MeasureText(saveText, Font).Width + S(28));
        int dw = Math.Max(S(88), TextRenderer.MeasureText(defText, Font).Width + S(28));
        var reset = new Button { Text = defText, UseVisualStyleBackColor = true };
        reset.SetBounds(M, y, dw, bh);
        reset.Click += (o, e) =>
        {
            pin.Set(Hotkey.DefaultMods, Hotkey.DefaultKey);
            unpin.Set(Hotkey.DefaultUnpinMods, Hotkey.DefaultUnpinKey);
            ShowError(string.Empty);
        };
        var cancel = new Button { Text = cancelText, DialogResult = DialogResult.Cancel, UseVisualStyleBackColor = true };
        cancel.SetBounds(W - M - cw, y, cw, bh);
        save = new Button { Text = saveText, UseVisualStyleBackColor = true };
        save.SetBounds(W - M - cw - S(8) - sw, y, sw, bh);
        save.Click += OnSave;
        Controls.Add(reset);
        Controls.Add(cancel);
        Controls.Add(save);
        AcceptButton = save;
        CancelButton = cancel;
        y += bh + M;

        ClientSize = new Size(W, y);
        foreach (var f in new[] { pin, unpin })
        {
            f.Message += ShowError;
            f.Finished += (o, e) => { if (Visible && ContainsFocus) save.Focus(); }; // Enter после ввода = «Сохранить»
        }
        // Сочетание ждём только по кнопке: при открытии фокус на списке языков, а не в поле
        ActiveControl = lang;
        Deactivate += (o, e) => { pin.Stop(); unpin.Stop(); };   // ушли в другое окно — перестаём перехватывать клавиши
        FormClosing += (o, e) => { pin.Dispose(); unpin.Dispose(); };
        Shown += (o, e) => { TopMost = true; TopMost = false; Activate(); }; // окно открывается из трея — выводим его вперёд
    }

    // Запасной ввод сочетания, если перехват клавиатуры не поставился
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (pin.FallbackKey(keyData) || unpin.FallbackKey(keyData)) return true;
        return base.ProcessCmdKey(ref msg, keyData);
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
        DialogResult = DialogResult.Retry; // SettingsApp откроет окно заново на выбранном языке
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
        pin.Stop(); unpin.Stop();
        if (unpin.Key != 0 && unpin.Key == pin.Key && unpin.Modifiers == pin.Modifiers) { ShowError(Loc.T("Err.HotkeySame")); return; }
        if (!canRegister(pin.Modifiers, pin.Key)) { ShowError(Loc.T("Err.HotkeyBusy", Hotkey.Format(pin.Modifiers, pin.Key))); return; }
        if (unpin.Key != 0 && !canRegister(unpin.Modifiers, unpin.Key)) { ShowError(Loc.T("Err.HotkeyBusy", Hotkey.Format(unpin.Modifiers, unpin.Key))); return; }
        Language = ((LangItem)lang.SelectedItem).Code;
        DialogResult = DialogResult.OK;
    }
}

// Процесс окна настроек: AlwaysOnTop.exe --settings [--hotkey-busy]. WinForms и GDI+ грузятся только здесь
// и уходят вместе с процессом. Код выхода: 0 — сохранено, 1 — отмена. Основной процесс на это время снимает свои хоткеи.
static class SettingsApp
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, int mod, int vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    const int TEST_HOTKEY_ID = 0xBFFF;

    public static int Run(bool hotkeyBusy)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => Log.Info("Unhandled (settings): " + e.Exception);

        var settings = Settings.Load();
        Loc.Init(settings.Language);
        Icon icon = SystemIcons.Application;
        try
        {
            using (var st = typeof(SettingsApp).Assembly.GetManifestResourceStream("AlwaysOnTop.ico"))
                if (st != null) icon = new Icon(st, SystemInformation.SmallIconSize);
        }
        catch (Exception ex) { Log.Info("Icon load failed: " + ex.Message); }

        if (hotkeyBusy)
            MessageBox.Show(Loc.T("Err.HotkeyRegister", Hotkey.Format(settings.Modifiers, settings.Key)), "Always on Top",
                MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1,
                Loc.IsRtl ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : (MessageBoxOptions)0);

        // Несохранённое состояние окна: переживает пересоздание окна при смене языка
        int mods = settings.Modifiers, key = settings.Key, umods = settings.UnpinModifiers, ukey = settings.UnpinKey;
        string lang = settings.Language;
        Point? at = null;
        while (true)
        {
            Loc.Init(lang); // окно показывается на выбранном, но ещё не сохранённом языке
            using (var f = new SettingsForm(mods, key, umods, ukey, lang, icon, CanRegister, at))
            {
                DialogResult r = f.ShowDialog();
                if (r == DialogResult.Retry)
                {
                    mods = f.Modifiers; key = f.Key; umods = f.UnpinModifiers; ukey = f.UnpinKey;
                    lang = f.Language; at = f.Location;
                    continue;
                }
                if (r != DialogResult.OK) return 1;
                settings.Language = f.Language;
                settings.Modifiers = f.Modifiers;
                settings.Key = f.Key;
                settings.UnpinModifiers = f.UnpinModifiers;
                settings.UnpinKey = f.UnpinKey;
                settings.Save();
                Log.Info("Settings saved: hotkey " + Hotkey.Serialize(settings.Modifiers, settings.Key)
                    + ", unpin all " + (settings.UnpinKey == 0 ? "None" : Hotkey.Serialize(settings.UnpinModifiers, settings.UnpinKey))
                    + ", language " + Loc.Code);
                return 0;
            }
        }
    }

    // Свободно ли сочетание в системе: пробная регистрация (основной процесс свои хоткеи на это время снял)
    static bool CanRegister(int mods, int key)
    {
        bool ok = RegisterHotKey(IntPtr.Zero, TEST_HOTKEY_ID, mods | Hotkey.MOD_NOREPEAT, key);
        if (ok) UnregisterHotKey(IntPtr.Zero, TEST_HOTKEY_ID);
        return ok;
    }
}
