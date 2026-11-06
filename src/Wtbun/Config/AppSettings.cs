namespace Wtbun.Config;

/// <summary>
/// 利用者が選ぶ設定。%APPDATA%\wtbun\settings.json に保存する。
/// </summary>
public sealed class AppSettings
{
    private const string FileName = "settings.json";

    public int Version { get; set; } = 1;

    /// <summary>管理下の Windows Terminal のウィンドウをタスクバーに出すか。</summary>
    public bool ShowTerminalsInTaskbar { get; set; } = true;

    /// <summary>
    /// 表示する言語（"en-US"・"ja-JP"）。初めて起動したときに Windows の表示言語から決めて記録し、以後はこれを使う。
    /// 環境設定のダイアログで変えられる。起動時の --lang は、これを変えずに上書きする。
    /// </summary>
    public string? Language { get; set; }

    public static AppSettings Load() => UserDataFile.Load<AppSettings>(FileName) ?? new AppSettings();

    public void Save() => UserDataFile.Save(FileName, this);
}
