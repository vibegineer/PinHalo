using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// Локализация. Строки лежат в lang\*.lang (key=value, UTF-8) и вшиваются в exe ресурсами "Lang.<код>.lang".
// Нет ключа в выбранном языке — берётся английская строка, нет и её — показывается сам ключ.
static class Loc
{
    // Порядок = порядок в списке выбора: по числу носителей
    static readonly string[] Codes = { "en", "zh-Hans", "hi", "es", "fr", "ar", "bn", "pt", "ru", "ur",
                                       "id", "de", "ja", "mr", "te", "tr", "ta", "zh-Hant", "vi", "ko" };
    static readonly string[] RtlCodes = { "ar", "ur" };
    static readonly Dictionary<string, Dictionary<string, string>> cache = new Dictionary<string, Dictionary<string, string>>();
    static Dictionary<string, string> english, current;
    static string code = "en";

    public static string[] AllCodes { get { return Codes; } }
    public static string Code { get { return code; } }
    public static bool IsRtl { get { return Array.IndexOf(RtlCodes, code) >= 0; } }

    // setting: "auto" (язык интерфейса Windows) или код языка из списка
    public static void Init(string setting)
    {
        english = Load("en");
        code = Resolve(setting);
        current = Load(code);
    }

    public static string Resolve(string setting)
    {
        if (!string.IsNullOrEmpty(setting) && Array.IndexOf(Codes, setting) >= 0) return setting;
        return Detect();
    }

    // Язык интерфейса Windows у текущего пользователя (не язык установки системы)
    public static string Detect()
    {
        var c = CultureInfo.CurrentUICulture;
        string two = c.TwoLetterISOLanguageName, name = c.Name;
        if (two == "zh")
        {
            bool traditional = name.IndexOf("TW", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("HK", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("MO", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Hant", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("CHT", StringComparison.OrdinalIgnoreCase) >= 0;
            return traditional ? "zh-Hant" : "zh-Hans";
        }
        return Array.IndexOf(Codes, two) >= 0 ? two : "en";
    }

    // Название языка на нём самом — для списка выбора
    public static string NativeName(string langCode)
    {
        string n;
        return Load(langCode).TryGetValue("Lang.Name", out n) ? n : langCode;
    }

    public static string T(string key, params object[] args)
    {
        string v;
        if (current == null || !current.TryGetValue(key, out v))
            if (english == null || !english.TryGetValue(key, out v)) v = key;
        if (args.Length == 0) return v;
        try { return string.Format(v, args); }
        catch (FormatException) { return v; } // кривой перевод не должен ронять программу
    }

    static Dictionary<string, string> Load(string langCode)
    {
        Dictionary<string, string> d;
        if (cache.TryGetValue(langCode, out d)) return d;
        d = new Dictionary<string, string>();
        try
        {
            using (var s = typeof(Loc).Assembly.GetManifestResourceStream("Lang." + langCode + ".lang"))
            {
                if (s != null)
                    using (var r = new StreamReader(s, Encoding.UTF8))
                    {
                        string line;
                        while ((line = r.ReadLine()) != null)
                        {
                            line = line.Trim();
                            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                            int i = line.IndexOf('=');
                            if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                        }
                    }
            }
        }
        catch (Exception ex) { Log.Info("Lang load failed (" + langCode + "): " + ex.Message); }
        cache[langCode] = d;
        return d;
    }
}
