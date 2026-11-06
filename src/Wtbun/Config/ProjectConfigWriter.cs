using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Wtbun.Config;

/// <summary>今のファイルの書き方では、ダイアログの内容を正しく書き込めない。</summary>
public sealed class ProjectConfigNotEditableException(Exception? inner = null)
    : Exception("The config file could not be updated.", inner);

/// <param name="Text">書き込む内容。</param>
/// <param name="LosesComments">利用者のコメントのうち、書き込むと消えるものがある。</param>
public sealed record ProjectConfigEdit(string Text, bool LosesComments);

/// <summary>
/// 構成ファイルの文字列に、ダイアログで編集した内容を反映する。利用者のコメントや並びをなるべく残すため、ファイル全体を
/// 作り直さず、変わった所だけを置き換える。name は値だけ、タブは最初の [[tabs]] から最後のタブの終わりまでをまとめて置き換える
/// （その範囲の中のコメントは、どのタブのものか決められないので消える）。
/// </summary>
public static class ProjectConfigWriter
{
    private static readonly Regex CommentedName = new(@"^[ \t]*#[ \t]*name[ \t]*=", RegexOptions.Multiline);

    public static bool IsSame(ProjectConfig a, ProjectConfig b) =>
        Clean(a.Name) == Clean(b.Name) && IsSameTabs(a.Tabs, b.Tabs);

    /// <exception cref="ProjectConfigNotEditableException">反映した結果を読み直すと、編集した内容にならない。</exception>
    public static ProjectConfigEdit Apply(string text, ProjectConfig config)
    {
        var current = ProjectConfigStore.Parse(text);
        var document = SyntaxParser.Parse(text, ProjectConfigStore.FileName, true);
        var eol = text.Contains("\r\n") ? "\r\n" : text.Contains('\n') ? "\n" : Environment.NewLine;

        var tables = document.Tables.ToList();
        var keyValues = document.KeyValues.ToList();
        var lastKeyValueEnd = keyValues.Count > 0 ? EndOf(keyValues[^1]) : (int?)null;
        // 最初のタブより前（タブがなければ、最後の項目まで）が、name を置ける範囲
        var headEnd = tables.Count > 0 ? StartOf(tables[0]) : lastKeyValueEnd ?? text.Length;

        // 同じ位置に入れるときは、後に並べたものほど前に来る
        var edits = new List<(int Start, int Length, string Text)>();
        var removed = new List<(int Start, int End)>();

        if (!IsSameTabs(current.Tabs, config.Tabs))
        {
            if (tables.Count > 0)
            {
                var end = EndOf(tables[^1]);
                edits.Add((headEnd, end - headEnd, TabsText(config.Tabs, eol)));
                removed.Add((headEnd, end));
            }
            else
            {
                edits.Add((headEnd, 0, Separated(text, headEnd, TabsText(config.Tabs, eol), eol)));
            }
        }

        var name = Clean(config.Name);
        if (name != Clean(current.Name))
        {
            var entry = keyValues.FirstOrDefault(IsNameKey);
            if (entry is { Value: { } value } && name is not null)
            {
                edits.Add((StartOf(value), EndOf(value) - StartOf(value), Quote(name)));
            }
            else if (entry is not null)
            {
                edits.Add((StartOf(entry), EndOf(entry) - StartOf(entry), ""));
                removed.Add((StartOf(entry), EndOf(entry)));
            }
            else
            {
                edits.Add(NameInsertion(text, name!, headEnd, lastKeyValueEnd, eol));
            }
        }

        var result = new StringBuilder(text);
        foreach (var edit in edits.OrderByDescending(e => e.Start))
        {
            result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
        }
        var updated = result.ToString();

        Verify(updated, config);
        return new ProjectConfigEdit(updated, Comments(document).Any(offset => removed.Any(r => offset >= r.Start && offset < r.End)));
    }

    /// <summary>name がまだないとき。説明のコメント（# name = …）があればその下、なければ最後の項目の下か、最初のタブの前に入れる。</summary>
    private static (int, int, string) NameInsertion(string text, string name, int headEnd, int? lastKeyValueEnd, string eol)
    {
        var line = $"name = {Quote(name)}{eol}";
        var commented = CommentedName.Match(text, 0, headEnd);
        if (commented.Success)
        {
            var lineEnd = text.IndexOf('\n', commented.Index);
            return lineEnd >= 0 ? (lineEnd + 1, 0, line) : (text.Length, 0, eol + line);
        }
        if (lastKeyValueEnd is { } end)
        {
            return (end, 0, end > 0 && text[end - 1] != '\n' ? eol + line : line);
        }
        return (headEnd, 0, Separated(text, headEnd, line, eol));
    }

    /// <summary>前後の行と続かないよう、1 行空けて入れる形にする。</summary>
    private static string Separated(string text, int offset, string block, string eol)
    {
        var sb = new StringBuilder();
        var before = text[..offset];
        if (before.Length > 0)
        {
            if (!before.EndsWith('\n'))
            {
                sb.Append(eol);
            }
            if (!before.EndsWith("\n\n") && !before.EndsWith("\n\r\n"))
            {
                sb.Append(eol);
            }
        }
        sb.Append(block);
        var after = text[offset..];
        if (after.Length > 0 && !after.StartsWith('\n') && !after.StartsWith("\r\n"))
        {
            sb.Append(eol);
        }
        return sb.ToString();
    }

    /// <summary>タブとペインを、[[tabs]] と [[tabs.panes]] の並びで書く。タブとペインの間は 1 行空ける。</summary>
    private static string TabsText(List<TabConfig> tabs, string eol) =>
        string.Join(eol, tabs.Select(tab =>
        {
            var sb = new StringBuilder("[[tabs]]").Append(eol);
            AppendTerminal(sb, tab, eol);
            foreach (var pane in tab.Panes)
            {
                sb.Append(eol).Append("[[tabs.panes]]").Append(eol);
                AppendValue(sb, "split", pane.Split, eol);
                if (pane.Size is { } size)
                {
                    sb.Append("size = ").Append(FormatSize(size)).Append(eol);
                }
                AppendValue(sb, "duplicate", pane.Duplicate, eol);
                AppendTerminal(sb, pane, eol);
            }
            return sb.ToString();
        }));

    private static void AppendTerminal(StringBuilder sb, TerminalConfig terminal, string eol)
    {
        AppendValue(sb, "title", terminal.Title, eol);
        AppendValue(sb, "dir", terminal.Dir, eol);
        AppendValue(sb, "profile", terminal.Profile, eol);
        AppendValue(sb, "command", terminal.Command, eol);
        AppendValue(sb, "append_command_line", terminal.AppendCommandLine, eol);
        AppendValue(sb, "tab_color", terminal.TabColor, eol);
        AppendValue(sb, "color_scheme", terminal.ColorScheme, eol);
        AppendValue(sb, "suppress_application_title", terminal.SuppressApplicationTitle, eol);
        AppendValue(sb, "inherit_environment", terminal.InheritEnvironment, eol);
        AppendValue(sb, "selected", terminal.Selected, eol);
    }

    /// <summary>TOML の浮動小数点数として書く（0.4 など。整数に見える書き方にしない）。</summary>
    private static string FormatSize(double size)
    {
        var text = size.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') ? text : text + ".0";
    }

    private static void AppendValue(StringBuilder sb, string key, string? value, string eol)
    {
        if (Clean(value) is { } clean)
        {
            sb.Append(key).Append(" = ").Append(Quote(clean)).Append(eol);
        }
    }

    private static void AppendValue(StringBuilder sb, string key, bool? value, string eol)
    {
        if (value is { } flag)
        {
            sb.Append(key).Append(" = ").Append(flag ? "true" : "false").Append(eol);
        }
    }

    /// <summary>
    /// 手で書くときと同じ、'…' の形にする（\ や " をそのまま書ける）。' や制御文字を含むときだけ "…" にしてエスケープする。
    /// </summary>
    private static string Quote(string value)
    {
        if (!value.Any(c => c == '\'' || c == 0x7F || (c < 0x20 && c != '\t')))
        {
            return $"'{value}'";
        }
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append(@"\n"); break;
                case '\r': sb.Append(@"\r"); break;
                case '\t': sb.Append(@"\t"); break;
                case < ' ' or (char)0x7F: sb.Append($"\\u{(int)c:X4}"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

    private static void Verify(string text, ProjectConfig expected)
    {
        ProjectConfig actual;
        try
        {
            actual = ProjectConfigStore.Parse(text);
        }
        catch (Exception ex) when (ex is TomlException or UnknownConfigKeyException or InvalidConfigValueException)
        {
            throw new ProjectConfigNotEditableException(ex);
        }
        if (!IsSame(actual, expected))
        {
            throw new ProjectConfigNotEditableException();
        }
    }

    private static bool IsNameKey(KeyValueSyntax entry) =>
        entry.Key?.ToString()?.Trim().Trim('"', '\'') == "name";

    private static int StartOf(SyntaxNode node) => node.Span.Start.Offset;

    private static int EndOf(SyntaxNode node) => node.Span.End.Offset + 1;

    /// <summary>ファイルの中のコメントの位置を返す。</summary>
    private static IEnumerable<int> Comments(SyntaxNode node)
    {
        var trivia = (node.LeadingTrivia ?? []).Concat(node.TrailingTrivia ?? []);
        foreach (var item in trivia.Where(t => t.Kind == TokenKind.Comment))
        {
            yield return item.Span.Start.Offset;
        }
        for (var i = 0; i < node.ChildrenCount; i++)
        {
            if (node.GetChild(i) is { } child)
            {
                foreach (var offset in Comments(child))
                {
                    yield return offset;
                }
            }
        }
    }

    private static bool IsSameTabs(List<TabConfig> a, List<TabConfig> b) =>
        a.Count == b.Count && a.Zip(b).All(p => IsSameTerminal(p.First, p.Second) && IsSamePanes(p.First.Panes, p.Second.Panes));

    private static bool IsSamePanes(List<PaneConfig> a, List<PaneConfig> b) =>
        a.Count == b.Count && a.Zip(b).All(p =>
            IsSameTerminal(p.First, p.Second)
            && Clean(p.First.Split) == Clean(p.Second.Split)
            && p.First.Size == p.Second.Size
            && p.First.Duplicate == p.Second.Duplicate);

    private static bool IsSameTerminal(TerminalConfig a, TerminalConfig b) =>
        Clean(a.Title) == Clean(b.Title)
        && Clean(a.Dir) == Clean(b.Dir)
        && Clean(a.Command) == Clean(b.Command)
        && Clean(a.Profile) == Clean(b.Profile)
        && a.AppendCommandLine == b.AppendCommandLine
        && Clean(a.TabColor) == Clean(b.TabColor)
        && Clean(a.ColorScheme) == Clean(b.ColorScheme)
        && a.SuppressApplicationTitle == b.SuppressApplicationTitle
        && a.InheritEnvironment == b.InheritEnvironment
        && a.Selected == b.Selected;

    private static string? Clean(string? value) => value?.Trim() is { Length: > 0 } text ? text : null;
}
