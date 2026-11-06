namespace Wtbun.Config;

public static class AppStateStore
{
    private const string FileName = "state.json";

    /// <summary>保存した状態を読む。ないか読めなければ null（初めての起動と同じに扱う）。</summary>
    public static AppState? Load() => UserDataFile.Load<AppState>(FileName);

    public static void Save(AppState state) => UserDataFile.Save(FileName, state);
}
