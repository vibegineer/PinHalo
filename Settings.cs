using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

// Хоткей: модификаторы в формате RegisterHotKey + виртуальный код клавиши
static class Hotkey
{
    public const int MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008, MOD_NOREPEAT = 0x4000;
    public const int DefaultMods = MOD_CONTROL | MOD_WIN, DefaultKey = 0x54; // Ctrl+Win+T

    [DllImport("user32.dll")] static extern int MapVirtualKey(int code, int mapType);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetKeyNameText(int lParam, StringBuilder sb, int n);

    static List<string> ModNames(int mods)
    {
        var l = new List<string>();
        if ((mods & MOD_CONTROL) != 0) l.Add("Ctrl");
        if ((mods & MOD_ALT) != 0) l.Add("Alt");
        if ((mods & MOD_SHIFT) != 0) l.Add("Shift");
        if ((mods & MOD_WIN) != 0) l.Add("Win");
        return l;
    }

    // Для показа пользователю: «Ctrl + Win + T», название клавиши — как её подписывает раскладка
    public static string Format(int mods, int vk)
    {
        var l = ModNames(mods);
        l.Add(KeyLabel(vk));
        return string.Join(" + ", l.ToArray());
    }

    // Пока зажаты только модификаторы: «Ctrl + Win + …»
    public static string Preview(int mods)
    {
        var l = ModNames(mods);
        l.Add("…");
        return string.Join(" + ", l.ToArray());
    }

    // Для файла настроек: имена клавиш не зависят от раскладки и языка
    public static string Serialize(int mods, int vk)
    {
        var l = ModNames(mods);
        l.Add(VkName(vk));
        return string.Join("+", l.ToArray());
    }

    // Имена как у enum Keys («T», «D1», «F5»), но без WinForms: основной процесс её не грузит.
    // Для редких клавиш (Oemtilde, NumPad1, Insert…) — Keys, в отдельном методе: библиотека подгрузится только тогда.
    static string VkName(int vk)
    {
        if (vk >= 'A' && vk <= 'Z') return ((char)vk).ToString();
        if (vk >= '0' && vk <= '9') return "D" + (char)vk;
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
        return KeysName(vk);
    }

    static int VkFromName(string n)
    {
        if (n.Length == 1)
        {
            char c = char.ToUpperInvariant(n[0]);
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return c;
        }
        if (n.Length == 2 && (n[0] == 'D' || n[0] == 'd') && n[1] >= '0' && n[1] <= '9') return n[1];
        int f;
        if (n.Length >= 2 && (n[0] == 'F' || n[0] == 'f') && int.TryParse(n.Substring(1), out f) && f >= 1 && f <= 24) return 0x6F + f;
        return KeysParse(n);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string KeysName(int vk) { return ((Keys)vk).ToString(); }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int KeysParse(string n)
    {
        Keys k;
        return Enum.TryParse<Keys>(n, true, out k) ? (int)(k & Keys.KeyCode) : 0;
    }

    public static bool TryParse(string s, out int mods, out int vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrEmpty(s)) return false;
        string[] p = s.Split('+');
        for (int i = 0; i < p.Length - 1; i++)
            switch (p[i].Trim().ToLowerInvariant())
            {
                case "ctrl": mods |= MOD_CONTROL; break;
                case "alt": mods |= MOD_ALT; break;
                case "shift": mods |= MOD_SHIFT; break;
                case "win": mods |= MOD_WIN; break;
                default: return false;
            }
        vk = VkFromName(p[p.Length - 1].Trim());
        return vk != 0 && IsAllowed(mods, vk);
    }

    public static bool IsModifierKey(int vk)
    {
        return (vk >= 0x10 && vk <= 0x12) || vk == 0x5B || vk == 0x5C || (vk >= 0xA0 && vk <= 0xA5);
    }

    // Нужен хотя бы один модификатор — иначе «T» без Ctrl/Win перехватила бы обычный ввод во всех программах.
    // Исключение: F1–F24, их часто ставят на глобальные действия.
    public static bool IsAllowed(int mods, int vk)
    {
        if (vk < 7 || vk > 254 || IsModifierKey(vk)) return false; // 1–6 — кнопки мыши
        return mods != 0 || (vk >= 0x70 && vk <= 0x87);
    }

    public static string KeyLabel(int vk)
    {
        int scan = MapVirtualKey(vk, 0); // MAPVK_VK_TO_VSC
        if (scan != 0)
        {
            // Без флага «расширенная» стрелки, Insert/Delete/Home/End/PgUp/PgDn и др. называются как клавиши цифровой панели
            bool ext = (vk >= 0x21 && vk <= 0x28) || vk == 0x2D || vk == 0x2E || vk == 0x6F || vk == 0x90;
            var sb = new StringBuilder(64);
            if (GetKeyNameText((scan << 16) | (ext ? 1 << 24 : 0), sb, 64) > 0) return sb.ToString();
        }
        return VkName(vk);
    }
}

// Настройки пользователя: %LOCALAPPDATA%\AlwaysOnTop\settings.ini (рядом с установленным exe)
sealed class Settings
{
    public string Language = "auto";
    public int Modifiers = Hotkey.DefaultMods;
    public int Key = Hotkey.DefaultKey;

    static string FilePath
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlwaysOnTop", "settings.ini"); }
    }

    public static Settings Load()
    {
        var s = new Settings();
        try
        {
            if (File.Exists(FilePath))
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    int i = line.IndexOf('=');
                    if (i <= 0 || line[0] == '#' || line[0] == ';') continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    int m, vk;
                    if (k == "Language") s.Language = v;
                    else if (k == "Hotkey" && Hotkey.TryParse(v, out m, out vk)) { s.Modifiers = m; s.Key = vk; }
                }
        }
        catch (Exception ex) { Log.Info("Settings load failed: " + ex.Message); }
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, "Language=" + Language + "\r\nHotkey=" + Hotkey.Serialize(Modifiers, Key) + "\r\n", new UTF8Encoding(false));
        }
        catch (Exception ex) { Log.Info("Settings save failed: " + ex.Message); }
    }
}
