using System.Globalization;
using System.IO;
using System.Text;
using Wtbun.Config;

namespace Wtbun.Terminal;

/// <summary>
/// 構成から wt.exe の引数を組み立てる。
/// </summary>
public static class WtCommand
{
    /// <summary>
    /// 名前付きのウィンドウに、構成のタブ（とそのペイン）を順に開く引数を返す。
    /// 例：<c>-w wtbun-xxxx new-tab -d "H:\proj\web" ; split-pane -V --size 0.4 -d "H:\proj\web" -- npm run dev ; new-tab -d "H:\proj\web\src"</c>
    /// </summary>
    public static string Build(string windowName, string projectDir, ProjectConfig config)
    {
        var sb = new StringBuilder();
        sb.Append("-w ").Append(Quote(windowName));
        for (var i = 0; i < config.Tabs.Count; i++)
        {
            var tab = config.Tabs[i];
            if (i > 0)
            {
                sb.Append(" ;");
            }
            sb.Append(" new-tab");
            AppendTerminal(sb, projectDir, tab, defaultDir: true);

            foreach (var pane in tab.Panes)
            {
                sb.Append(" ; split-pane");
                switch (pane.Split?.Trim())
                {
                    case PaneConfig.SplitRight:
                        sb.Append(" -V");
                        break;
                    case PaneConfig.SplitDown:
                        sb.Append(" -H");
                        break;
                }
                if (pane.Size is { } size)
                {
                    sb.Append(" --size ").Append(size.ToString("0.###", CultureInfo.InvariantCulture));
                }
                if (pane.Duplicate == true)
                {
                    sb.Append(" -D");
                }
                // 複製するときは、開始フォルダーも複製元に合わせる（書いてあるときだけ渡す）
                AppendTerminal(sb, projectDir, pane, defaultDir: pane.Duplicate != true);
            }

            // 分割したままだと最後のペインが選ばれていて、タブにもそのペインのタイトルが出るので、選ぶペインへ移る。
            // 選ぶペインがなければ最初のペイン（タブ自体）。nextInOrder は作った順に次のペインへ移る
            if (tab.Panes.Count > 0)
            {
                sb.Append(" ; move-focus first");
                var selected = tab.Panes.FindIndex(p => p.Selected == true);
                for (var n = 0; n <= selected; n++)
                {
                    sb.Append(" ; move-focus nextInOrder");
                }
            }
        }

        // 選ぶタブがなければ、最後に開いたタブのまま（Windows Terminal の既定）
        var selectedTab = config.Tabs.FindIndex(t => t.Selected == true);
        if (selectedTab >= 0)
        {
            sb.Append(" ; focus-tab -t ").Append(selectedTab);
        }
        return sb.ToString();
    }

    /// <summary>new-tab と split-pane に共通のオプションとコマンドラインを付ける。</summary>
    /// <param name="defaultDir">dir がなくても、プロジェクトのフォルダーを開始フォルダーとして渡す。</param>
    private static void AppendTerminal(StringBuilder sb, string projectDir, TerminalConfig terminal, bool defaultDir)
    {
        var dir = Path.GetFullPath(Path.Combine(projectDir, terminal.Dir?.Trim() is { Length: > 0 } given ? given : "."));
        if (defaultDir || !string.IsNullOrWhiteSpace(terminal.Dir))
        {
            sb.Append(" -d ").Append(Quote(EscapeSemicolons(dir)));
        }
        if (!string.IsNullOrEmpty(terminal.Title))
        {
            sb.Append(" --title ").Append(Quote(EscapeSemicolons(terminal.Title)));
        }
        if (!string.IsNullOrEmpty(terminal.Profile))
        {
            sb.Append(" -p ").Append(Quote(EscapeSemicolons(terminal.Profile)));
        }
        if (TabColor.Normalize(terminal.TabColor) is { } color)
        {
            sb.Append(" --tabColor ").Append(color);
        }
        if (!string.IsNullOrWhiteSpace(terminal.ColorScheme))
        {
            sb.Append(" --colorScheme ").Append(Quote(EscapeSemicolons(terminal.ColorScheme.Trim())));
        }
        if (terminal.SuppressApplicationTitle is { } suppress)
        {
            sb.Append(suppress ? " --suppressApplicationTitle" : " --useApplicationTitle");
        }
        if (terminal.InheritEnvironment is { } inherit)
        {
            sb.Append(inherit ? " --inheritEnvironment" : " --reloadEnvironment");
        }
        if (string.IsNullOrWhiteSpace(terminal.Command))
        {
            return;
        }
        // 付け足すコマンドラインがなければ、何もしないので付けない
        var append = terminal.AppendCommandLine == true;
        if (append)
        {
            sb.Append(" --appendCommandLine");
        }
        // コマンドラインの前に「--」を置く。ないと、-Command や /k のような引数を wt.exe が自分のオプションとして読もうとして、
        // 付け足したときにタブがすぐ閉じる（Windows Terminal 1.24 で確かめた）。
        // コマンドラインは利用者が書いた引用符を活かすため、そのまま後ろに付ける（バッチファイルなら cmd /c で包む）。
        // プロファイルのコマンドラインに付け足すときは引数なので、cmd /c で包まない
        var command = terminal.Command.Trim();
        sb.Append(" -- ").Append(EscapeSemicolons(append ? command : CommandLauncher.Prepare(command, dir)));
    }

    /// <summary>wt.exe は「;」をサブコマンドの区切りに使うので、値の中の「;」は「\;」にする。</summary>
    private static string EscapeSemicolons(string value) => value.Replace(";", @"\;");

    /// <summary>Windows の引数の規則（CommandLineToArgvW）で 1 つの引数として読まれるように囲む。</summary>
    private static string Quote(string value)
    {
        if (value.Length > 0 && value.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return value;
        }

        var sb = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in value)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            // 「"」の直前の「\」は 2 倍にし、「"」自体も「\"」にする
            sb.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
            sb.Append(c);
            backslashes = 0;
        }
        // 閉じの「"」の直前に来る「\」も 2 倍にする（例：H:\ → "H:\\"）
        sb.Append('\\', backslashes * 2);
        sb.Append('"');
        return sb.ToString();
    }
}
