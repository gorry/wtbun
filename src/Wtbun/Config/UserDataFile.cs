using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Wtbun.Config;

/// <summary>
/// 保存先のフォルダ（既定は %APPDATA%\wtbun\）に置く JSON ファイルの読み書き。
/// </summary>
public static class UserDataFile
{
    /// <summary>
    /// 保存先のフォルダ。起動時の --data-dir で変えられる（試験で、利用者の状態を使わずに動かすため）。
    /// 既定の場所は既知のフォルダの API で決まるので、環境変数 APPDATA を変えても変わらない。
    /// </summary>
    public static string Folder { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppProfile.ShortTitle);

    /// <summary>保存先のフォルダを変える。状態や設定を読む前（起動の最初）に呼ぶ。</summary>
    public static void UseFolder(string folder) => Folder = Path.GetFullPath(folder);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>読む。ないか読めなければ null。</summary>
    public static T? Load<T>(string fileName) where T : class
    {
        var path = Path.Combine(Folder, fileName);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    public static void Save<T>(string fileName, T value)
    {
        var path = Path.Combine(Folder, fileName);
        try
        {
            Directory.CreateDirectory(Folder);
            // 書いている途中で終わっても壊れないよう、別のファイルに書いてから置き換える
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(ex);
        }
    }
}
