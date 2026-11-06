using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Wtbun.Localization;

/// <summary>
/// 画面の文言（Strings.resx・Strings.ja.resx）と、表示する言語。
/// XAML からは <see cref="TrExtension"/>（<c>{l:Tr キー}</c>）で使う。言語を変えると、その場で表示も変わる。
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    /// <summary>XAML の Binding の元。インデクサーで文言を返す。</summary>
    public static Loc Instance { get; } = new();

    private static readonly ResourceManager Resources =
        new("Wtbun.Localization.Strings", typeof(Loc).Assembly);

    /// <summary>
    /// 選べる言語（設定ファイルと --lang に書く名前）と、その言語自身での名前。
    /// 先頭が既定（どの言語にも当てはまらないとき）。
    /// </summary>
    public static IReadOnlyList<(string Name, string DisplayName)> Languages { get; } =
    [
        ("en-US", "English"),
        ("ja-JP", "日本語"),
    ];

    /// <summary>今の言語（<see cref="Languages"/> の名前）。</summary>
    public static string Current { get; private set; } = Languages[0].Name;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Get(key);

    /// <summary>文言。キーがなければ、見て分かるように [キー] を返す。</summary>
    public static string Get(string key) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture) ?? $"[{key}]";

    /// <summary>{0} などに値を入れた文言。</summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>
    /// 言語の名前を、<see cref="Languages"/> の名前にそろえる（大文字・小文字は区別しない。「ja」「en」だけでもよい）。
    /// 選べない言語なら null。
    /// </summary>
    public static string? Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        name = name.Trim();
        foreach (var (language, _) in Languages)
        {
            if (string.Equals(language, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(language.Split('-')[0], name, StringComparison.OrdinalIgnoreCase))
            {
                return language;
            }
        }
        return null;
    }

    /// <summary>Windows の表示言語から選ぶ。選べる言語になければ既定（英語）。</summary>
    public static string Detect(CultureInfo uiCulture)
    {
        for (var culture = uiCulture; !string.IsNullOrEmpty(culture.Name); culture = culture.Parent)
        {
            if (Normalize(culture.Name) is { } language)
            {
                return language;
            }
        }
        return Languages[0].Name;
    }

    /// <summary>
    /// 表示する言語を変える。開いている画面の文言もその場で変わる。
    /// 日付や数の書き方（CurrentCulture）は Windows の設定のままにする。
    /// </summary>
    public static void Apply(string language)
    {
        Current = Normalize(language) ?? Languages[0].Name;
        var culture = CultureInfo.GetCultureInfo(Current);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        // インデクサーの Binding をすべて読み直させる
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
    }
}
