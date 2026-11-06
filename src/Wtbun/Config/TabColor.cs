using System.Text.RegularExpressions;

namespace Wtbun.Config;

/// <summary>タブの色（tab_color）の書き方。wt.exe の --tabColor と同じ #RGB か #RRGGBB。</summary>
public static partial class TabColor
{
    [GeneratedRegex("^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$")]
    private static partial Regex Pattern();

    /// <summary>空か、正しい書き方なら true。</summary>
    public static bool IsValid(string? value) => string.IsNullOrWhiteSpace(value) || Pattern().IsMatch(value.Trim());

    /// <summary>前後の空白を除いた値。空か正しくなければ null。</summary>
    public static string? Normalize(string? value) =>
        value?.Trim() is { Length: > 0 } text && Pattern().IsMatch(text) ? text : null;
}
