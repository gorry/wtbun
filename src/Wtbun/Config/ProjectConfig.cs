namespace Wtbun.Config;

/// <summary>
/// プロジェクト直下の構成ファイル（.wtbun.toml）の内容。プロジェクトを開くときの初期構成を表す。
/// </summary>
public sealed class ProjectConfig
{
    public int Version { get; set; } = 1;

    /// <summary>wtbun のタブに出す名前。省略するとフォルダ名。</summary>
    public string? Name { get; set; }

    public List<TabConfig> Tabs { get; set; } = [];
}

/// <summary>
/// タブとペインに共通の設定（wt.exe の new-tab と split-pane に共通のオプション）。省略した項目は Windows Terminal の既定値に従う。
/// </summary>
public abstract class TerminalConfig
{
    public string? Title { get; set; }

    /// <summary>開始フォルダ。相対パスはプロジェクトのフォルダを基準にする。</summary>
    public string? Dir { get; set; }

    /// <summary>実行するコマンドライン。</summary>
    public string? Command { get; set; }

    /// <summary>Windows Terminal のプロファイル名または GUID。</summary>
    public string? Profile { get; set; }

    /// <summary>true なら、command をプロファイルのコマンドラインの後ろに付け足す（--appendCommandLine）。</summary>
    public bool? AppendCommandLine { get; set; }

    /// <summary>タブの色。#RGB か #RRGGBB（--tabColor）。</summary>
    public string? TabColor { get; set; }

    /// <summary>配色の名前。プロファイルの配色の代わりに使う（--colorScheme）。</summary>
    public string? ColorScheme { get; set; }

    /// <summary>
    /// true なら、アプリがタブのタイトルを変えられないようにする（--suppressApplicationTitle）。false なら変えられる（--useApplicationTitle）。
    /// 省略するとプロファイルの設定に従う。
    /// </summary>
    public bool? SuppressApplicationTitle { get; set; }

    /// <summary>
    /// true なら Windows Terminal の環境変数を引き継ぐ（--inheritEnvironment）。false なら新しく作る（--reloadEnvironment）。
    /// 省略すると Windows Terminal の既定（command があれば引き継ぐ）。
    /// </summary>
    public bool? InheritEnvironment { get; set; }

    /// <summary>
    /// true なら、開いたときにこのタブ（ペインなら、タブの中でこのペイン）を選んでおく。
    /// タブは focus-tab、ペインは move-focus で選ぶ。タブを選ばなければ最後のタブ、ペインを選ばなければ最初のペイン（タブ自体）。
    /// </summary>
    public bool? Selected { get; set; }
}

/// <summary>Windows Terminal で開くタブ 1 つぶんの設定。</summary>
public sealed class TabConfig : TerminalConfig
{
    /// <summary>このタブを分割して開くペイン。書いた順に分割する。</summary>
    public List<PaneConfig> Panes { get; set; } = [];
}

/// <summary>タブを分割して開くペイン 1 つぶんの設定（wt.exe の split-pane）。</summary>
public sealed class PaneConfig : TerminalConfig
{
    public const string SplitRight = "right";
    public const string SplitDown = "down";

    /// <summary>
    /// 新しいペインを置く向き。right（右。-V）か down（下。-H）。省略すると Windows Terminal が大きさから決める。
    /// </summary>
    public string? Split { get; set; }

    /// <summary>新しいペインの大きさ。分割する前のペインに対する割合（0 より大きく 1 より小さい）。省略すると半分。</summary>
    public double? Size { get; set; }

    /// <summary>true なら、直前のペインを複製する（-D）。</summary>
    public bool? Duplicate { get; set; }
}
