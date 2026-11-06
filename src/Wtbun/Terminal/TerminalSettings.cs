using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Wtbun.Terminal;

/// <summary>Windows Terminal の設定ファイルを読む。書き換えはしない。</summary>
public static class TerminalSettings
{
    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// プロファイルの名前を読む。ダイアログの候補に出すだけなので、読めなければ空を返す（名前は手でも入力できる）。
    /// </summary>
    public static IReadOnlyList<string> LoadProfileNames()
    {
        var names = new List<string>();
        foreach (var path in SettingsFiles())
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path), Options);
                names.AddRange(NamesIn(document.RootElement));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine(ex);
            }
        }
        return names.Distinct().ToList();
    }

    /// <summary>
    /// Windows Terminal に最初からある配色。設定ファイルには書かれていない（Windows Terminal 1.24 の defaults.json から）。
    /// </summary>
    private static readonly string[] BuiltInColorSchemes =
    [
        "Campbell", "Campbell Powershell", "Vintage", "One Half Dark", "One Half Light", "Solarized Dark", "Solarized Light",
        "Tango Dark", "Tango Light", "Dark+", "CGA", "IBM 5153", "Ottosson", "Dimidium",
    ];

    /// <summary>
    /// 配色の名前を読む（最初からあるものと、設定ファイルで追加したもの）。ダイアログの候補に出すだけなので、読めなければ最初からあるものだけ。
    /// </summary>
    public static IReadOnlyList<string> LoadColorSchemeNames()
    {
        var names = new List<string>(BuiltInColorSchemes);
        foreach (var path in SettingsFiles())
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path), Options);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("schemes", out var schemes)
                    && schemes.ValueKind == JsonValueKind.Array)
                {
                    names.AddRange(schemes.EnumerateArray()
                        .Where(s => s.ValueKind == JsonValueKind.Object
                            && s.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                        .Select(s => s.GetProperty("name").GetString()!));
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine(ex);
            }
        }
        return names.Distinct().ToList();
    }

    /// <summary>
    /// 「複数のタブを閉じるときに警告する」がオンか。Windows Terminal の既定はオンなので、読めないときもオンとみなす。
    /// 複数の版が入っていれば、先頭（ストア版）の設定を見る。
    /// </summary>
    public static bool WarnsWhenClosingTabs()
    {
        var path = SettingsFiles().FirstOrDefault();
        if (path is null)
        {
            return true;
        }
        try
        {
            return WarnsWhenClosingTabs(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(ex);
            return true;
        }
    }

    // キーは新しい名前と、古い Windows Terminal の名前がある
    public static bool WarnsWhenClosingTabs(string settingsJson)
    {
        using var document = JsonDocument.Parse(settingsJson, Options);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return true;
        }
        return !(root.TryGetProperty("warning.confirmCloseAllTabs", out var value)
                || root.TryGetProperty("confirmCloseAllTabs", out value))
            || value.ValueKind != JsonValueKind.False;
    }

    // ストア版（プレビュー版なども含む）と、インストーラーなしで使う版の場所
    private static IEnumerable<string> SettingsFiles()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new List<string> { Path.Combine(local, "Microsoft", "Windows Terminal", "settings.json") };
        try
        {
            candidates.InsertRange(0, Directory
                .GetDirectories(Path.Combine(local, "Packages"), "Microsoft.WindowsTerminal*_8wekyb3d8bbwe")
                .OrderBy(dir => dir.Length)
                .Select(dir => Path.Combine(dir, "LocalState", "settings.json")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(ex);
        }
        return candidates.Where(File.Exists);
    }

    // profiles は { "list": [...] } の形と、配列そのものの形がある
    private static IEnumerable<string> NamesIn(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("profiles", out var profiles))
        {
            return [];
        }
        if (profiles.ValueKind == JsonValueKind.Object && profiles.TryGetProperty("list", out var list))
        {
            profiles = list;
        }
        if (profiles.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return profiles.EnumerateArray()
            .Where(p => p.ValueKind == JsonValueKind.Object
                && !(p.TryGetProperty("hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True)
                && p.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            .Select(p => p.GetProperty("name").GetString()!)
            .ToList();
    }
}
