using Wtbun.Native;

namespace Wtbun.Config;

/// <summary>
/// 次の起動で開き直すための、wtbun の状態。%APPDATA%\wtbun\state.json に保存する。
/// </summary>
public sealed class AppState
{
    public int Version { get; set; } = 1;

    /// <summary>開いていたプロジェクト。タブの並び順。</summary>
    public List<ProjectState> Projects { get; set; } = [];

    public int SelectedIndex { get; set; } = -1;

    /// <summary>全プロジェクトのウィンドウの位置とサイズ（物理ピクセル）。</summary>
    public RECT? SharedRect { get; set; }

    /// <summary>タブ列の位置とサイズ（物理ピクセル）。プロジェクトがないときに使う。</summary>
    public RECT? StripRect { get; set; }
}

public sealed class ProjectState
{
    public string Directory { get; set; } = "";

    public string? WindowName { get; set; }

    /// <summary>
    /// 保存したときのウィンドウハンドル。次の起動でもこのウィンドウが残っていれば（閉じる確認で取り消されたときや、
    /// wtbun だけが終わったとき）、新しく開かずにそのまま使う。
    /// </summary>
    public long WindowHandle { get; set; }
}
