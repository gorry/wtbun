using System.IO;
using System.Text.Json;
using Tomlyn;
using Tomlyn.Model;
using Wtbun.Localization;

namespace Wtbun.Config;

/// <param name="misplacedInTab">ファイル全体の項目（name など）を [[tabs]] の後ろに書いたため、タブの項目と見なされた。</param>
public sealed class UnknownConfigKeyException(string key, bool misplacedInTab) : Exception($"Unknown key: {key}")
{
    public string Key { get; } = key;

    public bool MisplacedInTab { get; } = misplacedInTab;
}

/// <summary>項目の値が正しくない（tab_color の書き方など）。</summary>
public sealed class InvalidConfigValueException(string key) : Exception($"Invalid value: {key}")
{
    public string Key { get; } = key;
}

/// <param name="Path">構成ファイルの場所。</param>
/// <param name="Text">読んだときのファイルの文字列。ファイルがなければ null。</param>
/// <param name="Config">ダイアログに出す構成。タブが 1 つもないファイルなら、既定のタブで補ってある。</param>
public sealed record ProjectConfigDocument(string Path, string? Text, ProjectConfig Config);

public static class ProjectConfigStore
{
    public const string FileName = $".{AppProfile.ShortTitle}.toml";

    private static readonly TomlSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// 構成ファイルを読み込む。なければ、説明のコメント付きの既定の構成を作ってから読み込む。
    /// </summary>
    /// <exception cref="TomlException">構成ファイルの書式が正しくない。</exception>
    /// <exception cref="IOException">構成ファイルを読み書きできない。</exception>
    public static ProjectConfig LoadOrCreate(string projectDir)
    {
        var path = Path.Combine(projectDir, FileName);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, DefaultText);
        }

        var config = Load(path);

        // タブが 1 つもないとウィンドウを開けないので、既定のタブで補う（ファイルは書き換えない）
        if (config.Tabs.Count == 0)
        {
            config.Tabs.Add(new TabConfig { Dir = "." });
        }
        return config;
    }

    /// <summary>
    /// タブに出す名前だけを読む。前回のウィンドウを引き取るとき（ウィンドウは開かない）に使う。
    /// ファイルがない・読めないときは null（フォルダ名を出す）。ファイルは作らない。
    /// </summary>
    public static string? TryLoadName(string projectDir)
    {
        var path = Path.Combine(projectDir, FileName);
        try
        {
            return File.Exists(path) ? Load(path).Name : null;
        }
        catch (Exception ex) when (ex is TomlException or UnknownConfigKeyException or InvalidConfigValueException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// ダイアログで編集するために読む。ファイルは作らない。ファイルがなければ既定の構成を返す。
    /// </summary>
    /// <exception cref="TomlException">構成ファイルの書式が正しくない。</exception>
    /// <exception cref="InvalidConfigValueException">値が正しくない（tab_color の書き方など）。</exception>
    /// <exception cref="UnknownConfigKeyException">知らない項目がある。</exception>
    /// <exception cref="IOException">構成ファイルを読めない。</exception>
    public static ProjectConfigDocument LoadForEdit(string projectDir)
    {
        var path = Path.Combine(projectDir, FileName);
        var text = ReadTextOrNull(path);
        var config = Parse(text ?? DefaultText);
        if (config.Tabs.Count == 0)
        {
            config.Tabs.Add(new TabConfig { Dir = "." });
        }
        return new ProjectConfigDocument(path, text, config);
    }

    /// <summary>ファイルの文字列を返す。なければ null。</summary>
    public static string? ReadTextOrNull(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    /// <summary>書いている途中で終わっても壊れないよう、別のファイルに書いてから置き換える。</summary>
    public static void WriteText(string path, string text)
    {
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, text);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    /// <summary>構成ファイルの文字列を読む。タブが 1 つもないときも、そのまま返す。</summary>
    public static ProjectConfig Parse(string text)
    {
        CheckKeys(TomlSerializer.Deserialize<TomlTable>(text, Options));
        var config = TomlSerializer.Deserialize<ProjectConfig>(text, Options) ?? new ProjectConfig();
        CheckValues(config);
        return config;
    }

    /// <summary>ファイルがないときに作る内容。コメントは、作るときの表示の言語で書く（項目と値は同じ）。</summary>
    public static string DefaultText =>
        (Loc.Current == "ja-JP" ? DefaultFileTextJa : DefaultFileTextEn).ReplaceLineEndings();

    private static ProjectConfig Load(string path) => Parse(File.ReadAllText(path));

    private static readonly string[] RootKeys = ["version", "name", "tabs"];
    // タブとペインに共通の項目
    private static readonly string[] TerminalKeys =
    [
        "title", "dir", "command", "profile",
        "append_command_line", "tab_color", "color_scheme", "suppress_application_title", "inherit_environment", "selected",
    ];
    private static readonly string[] TabKeys = [.. TerminalKeys, "panes"];
    private static readonly string[] PaneKeys = [.. TerminalKeys, "split", "size", "duplicate"];

    /// <summary>値が正しいか確かめる。</summary>
    /// <exception cref="InvalidConfigValueException">値が正しくない。</exception>
    private static void CheckValues(ProjectConfig config)
    {
        var panes = config.Tabs.SelectMany(tab => tab.Panes).ToList();
        if (config.Tabs.Cast<TerminalConfig>().Concat(panes).Any(t => !TabColor.IsValid(t.TabColor)))
        {
            throw new InvalidConfigValueException("tab_color");
        }
        if (panes.Any(p => p.Split?.Trim() is { Length: > 0 } split && split is not (PaneConfig.SplitRight or PaneConfig.SplitDown)))
        {
            throw new InvalidConfigValueException("split");
        }
        if (panes.Any(p => p.Size is { } size && !(size > 0 && size < 1)))
        {
            throw new InvalidConfigValueException("size");
        }
        // 選べるのは、タブは 1 つ、ペインはタブごとに 1 つ
        if (config.Tabs.Count(t => t.Selected == true) > 1 || config.Tabs.Any(t => t.Panes.Count(p => p.Selected == true) > 1))
        {
            throw new InvalidConfigValueException("selected");
        }
    }

    /// <summary>
    /// 知らない項目があれば止める。Tomlyn は知らない項目を黙って読み飛ばすので、書き間違い（titel など）に気づけるようにする。
    /// </summary>
    /// <exception cref="UnknownConfigKeyException">知らない項目がある。</exception>
    private static void CheckKeys(TomlTable? root)
    {
        if (root is null)
        {
            return;
        }
        foreach (var key in root.Keys.Where(k => !RootKeys.Contains(k)))
        {
            throw new UnknownConfigKeyException(key, misplacedInTab: false);
        }
        if (root.TryGetValue("tabs", out var tabs) && tabs is TomlTableArray tables)
        {
            foreach (var key in tables.SelectMany(t => t.Keys).Where(k => !TabKeys.Contains(k)))
            {
                // TOML では [[tabs]] より後ろの項目はそのタブの項目になるので、name などを後ろに書くとここに来る
                throw new UnknownConfigKeyException(key, misplacedInTab: RootKeys.Contains(key));
            }
            var panes = tables.Select(t => t.TryGetValue("panes", out var value) ? value : null).ToList();
            if (panes.Any(p => p is not (null or TomlTableArray)))
            {
                // panes = 'x' のように、[[tabs.panes]] 以外の形で書いた
                throw new InvalidConfigValueException("panes");
            }
            foreach (var key in panes.OfType<TomlTableArray>().SelectMany(p => p).SelectMany(t => t.Keys).Where(k => !PaneKeys.Contains(k)))
            {
                throw new UnknownConfigKeyException(key, misplacedInTab: RootKeys.Contains(key));
            }
        }
    }

    // 利用者が README を読まなくても書き換えられるよう、項目の説明と例をコメントで入れておく。
    // 英語と日本語で、行の並びと有効な行（version と [[tabs]]）をそろえる
    private const string DefaultFileTextJa = $$"""
        # {{AppProfile.ShortTitle}} のプロジェクト構成ファイルです。
        # {{AppProfile.ShortTitle}} でこのフォルダーを開くと、ここに書いた構成で Windows Terminal を開きます。
        # 変更は次回プロジェクトを開いたときに反映されます。
        # Windows Terminal で追加したタブはこのファイルに保存されません。

        # ファイル形式のバージョンです。変更しないでください。
        version = 1

        # {{AppProfile.ShortTitle}} のタブに表示する名前です。省略するとフォルダー名を表示します。
        # 使用するには行頭の「# 」を削除します。[[tabs]] より前に書いてください。
        # name = 'my project'

        # 開くタブを [[tabs]] の行から書きます。上から順に開きます。
        # 次の項目を指定できます。どれも省略できます。
        #   title   … タブのタイトル。省略すると Windows Terminal が設定します。
        #   dir     … 開始フォルダー。相対パスはこのファイルのフォルダーが基準です。
        #              省略するとこのファイルのフォルダーです。
        #   profile … Windows Terminal のプロファイル名。省略すると既定のプロファイルを使用します。
        #   command … 実行するコマンド。省略するとプロファイルの設定に従います。
        #              例：npm run dev
        #              バッチファイル（.cmd・.bat）も指定できます。
        #              コマンドが終了するとタブも閉じます。閉じないようにするには前に cmd /k を付けます
        #              （PowerShell の場合は powershell -NoExit -Command）。
        #   append_command_line … true にすると、command をプロファイルのコマンドの後ろに追加します。
        #              例：profile = 'Windows PowerShell' と command = '-NoExit -Command Get-Date'
        #   tab_color … タブの色。'#RRGGBB' か '#RGB' で書きます。例：'#2E7D32'
        #   color_scheme … 配色の名前。プロファイルの配色の代わりに使用します。例：'One Half Dark'
        #   suppress_application_title … true にすると、実行中のプログラムがタブのタイトルを変更できません。
        #              false にすると変更できます。省略するとプロファイルの設定に従います。
        #   inherit_environment … true にすると、Windows Terminal の環境変数を引き継ぎます。
        #              false にすると、環境変数を新しく読み込みます。
        #              省略した場合、command があれば引き継ぎます。
        #   selected … true にすると、開いたときにこのタブを選びます。1 つのタブにだけ書けます。
        #              省略すると最後のタブを選びます。
        # タブを分割するには、[[tabs]] の後ろに [[tabs.panes]] を書きます。書いた順に、直前のペインを分割します。
        # [[tabs.panes]] には、上の項目に加えて次の項目を指定できます。どれも省略できます。
        #   split … 新しいペインを置く向き。'right'（右）か 'down'（下）。省略すると縦横の長さで決まります。
        #   size … 新しいペインの大きさ。分割する前のペインに対する割合を、0 より大きく 1 より小さい数で書きます。
        #              例：0.4。省略すると半分です。
        #   duplicate … true にすると、直前のペインと同じプロファイルとフォルダーで開きます。
        #   [[tabs.panes]] の selected は、タブの中で選ぶペインです。1 つのタブで 1 つのペインにだけ書けます。
        #              省略すると最初のペイン（[[tabs]] で開いたもの）を選びます。
        # true・false 以外の値は '…'（半角の単一引用符）で囲みます。中の \ や " はそのまま書けます。
        # 値に ' を含める場合は "…" で囲み、\ は \\、" は \" と書きます。

        [[tabs]]
        dir = '.'

        # タブを追加する例です。行頭の「# 」を削除すると使用できます。
        # Windows PowerShell でファイルの一覧を表示して、プロンプトで待機します。
        # [[tabs]]
        # title = 'files'
        # dir = '.'
        # profile = 'Windows PowerShell'
        # command = 'powershell -NoExit -Command Get-ChildItem'

        # タブを分割する例です。行頭の「# 」を削除すると使用できます。
        # 直前の [[tabs]] のタブを右に分割し、Windows PowerShell で今日の日付を表示して、プロンプトで待機します。
        # [[tabs.panes]]
        # split = 'right'
        # size = 0.4
        # profile = 'Windows PowerShell'
        # command = 'powershell -NoExit -Command Get-Date'

        """;

    private const string DefaultFileTextEn = $$"""
        # Project configuration file for {{AppProfile.ShortTitle}}.
        # When you open this folder in {{AppProfile.ShortTitle}}, Windows Terminal opens with the layout written here.
        # Changes take effect the next time you open the project.
        # Tabs you add in Windows Terminal are not saved to this file.

        # File format version. Do not change it.
        version = 1

        # Name shown on the {{AppProfile.ShortTitle}} tab. If omitted, the folder name is shown.
        # To use it, remove the "# " at the start of the line. Write it before [[tabs]].
        # name = 'my project'

        # Write the tabs to open, each starting with a [[tabs]] line. They open from top to bottom.
        # You can set the following keys. All of them are optional.
        #   title   - Tab title. If omitted, Windows Terminal sets it.
        #   dir     - Starting folder. Relative paths are relative to the folder of this file.
        #              If omitted, the folder of this file is used.
        #   profile - Windows Terminal profile name. If omitted, the default profile is used.
        #   command - Command to run. If omitted, the profile setting is used.
        #              Example: npm run dev
        #              Batch files (.cmd, .bat) can also be used.
        #              The tab closes when the command ends. To keep it open, put cmd /k before the command
        #              (for PowerShell, powershell -NoExit -Command).
        #   append_command_line - If true, command is appended to the command line of the profile.
        #              Example: profile = 'Windows PowerShell' and command = '-NoExit -Command Get-Date'
        #   tab_color - Tab color. Write it as '#RRGGBB' or '#RGB'. Example: '#2E7D32'
        #   color_scheme - Name of a color scheme to use instead of the profile's. Example: 'One Half Dark'
        #   suppress_application_title - If true, running programs can't change the tab title.
        #              If false, they can. If omitted, the profile setting is used.
        #   inherit_environment - If true, the environment variables of Windows Terminal are inherited.
        #              If false, the environment variables are loaded fresh.
        #              If omitted, they are inherited when command is set.
        #   selected - If true, this tab is selected when opened. Only one tab can have it.
        #              If omitted, the last tab is selected.
        # To split a tab, write [[tabs.panes]] after its [[tabs]]. Each one splits the previous pane, in order.
        # In [[tabs.panes]], you can set the keys above and also the following keys. All of them are optional.
        #   split - Where to put the new pane: 'right' or 'down'. If omitted, it depends on the pane's width and height.
        #   size - Size of the new pane, as a ratio of the pane before splitting. Write a number greater than 0 and less than 1.
        #              Example: 0.4. If omitted, half.
        #   duplicate - If true, the pane opens with the same profile and folder as the previous pane.
        #   selected in [[tabs.panes]] selects the pane within the tab. Only one pane per tab can have it.
        #              If omitted, the first pane (the one opened by [[tabs]]) is selected.
        # Enclose values other than true and false in '...' (single quotes). \ and " inside can be written as is.
        # To include ' in a value, enclose it in "..." and write \ as \\ and " as \".

        [[tabs]]
        dir = '.'

        # Example of adding a tab. Remove the "# " at the start of the lines to use it.
        # Lists the files in Windows PowerShell and waits at the prompt.
        # [[tabs]]
        # title = 'files'
        # dir = '.'
        # profile = 'Windows PowerShell'
        # command = 'powershell -NoExit -Command Get-ChildItem'

        # Example of splitting a tab. Remove the "# " at the start of the lines to use it.
        # Splits the tab of the previous [[tabs]] to the right, shows today's date in Windows PowerShell, and waits at the prompt.
        # [[tabs.panes]]
        # split = 'right'
        # size = 0.4
        # profile = 'Windows PowerShell'
        # command = 'powershell -NoExit -Command Get-Date'

        """;
}
