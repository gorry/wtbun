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

    public static AppSettings Load() => UserDataFile.Load<AppSettings>(FileName) ?? new AppSettings();

    public void Save() => UserDataFile.Save(FileName, this);
}
