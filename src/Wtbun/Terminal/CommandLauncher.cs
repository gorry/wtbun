using System.IO;

namespace Wtbun.Terminal;

/// <summary>
/// 構成ファイルの command を、Windows Terminal が起動できる形にする。
/// </summary>
public static class CommandLauncher
{
    private static readonly string[] BatchExtensions = [".cmd", ".bat"];

    /// <summary>
    /// Windows Terminal はコマンドラインをそのまま CreateProcess に渡すので、拡張子のないプログラム名は .exe しか探さない。
    /// npm（実体は npm.cmd）のようなバッチファイルは見つからないので、そのときは cmd /c で包む。
    /// </summary>
    /// <param name="startDir">タブの開始フォルダ。相対パスのプログラム名はここから探す。</param>
    public static string Prepare(string command, string startDir)
    {
        var program = FirstToken(command);
        if (program.Length == 0 || !IsBatchFile(program, startDir))
        {
            return command;
        }
        // cmd /c は、続く文字列が引用符で始まると最初と最後の引用符を外すので、全体を引用符で囲めば元のまま渡る
        return $"cmd /c \"{command}\"";
    }

    private static string FirstToken(string command)
    {
        var text = command.TrimStart();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end < 0 ? text[1..] : text[1..end];
        }
        var space = text.IndexOfAny([' ', '\t']);
        return space < 0 ? text : text[..space];
    }

    private static bool IsBatchFile(string program, string startDir)
    {
        var extension = Path.GetExtension(program);
        if (extension.Length > 0)
        {
            return BatchExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }
        // 拡張子がないとき、CreateProcess は .exe を足して探す。.exe がどこかにあれば、そのまま起動できる
        var name = Path.GetFileName(program);
        var directories = SearchDirectories(program, startDir).ToList();
        if (directories.Any(dir => File.Exists(Path.Combine(dir, name + ".exe"))))
        {
            return false;
        }
        return directories.Any(dir => BatchExtensions.Any(ext => File.Exists(Path.Combine(dir, name + ext))));
    }

    private static IEnumerable<string> SearchDirectories(string program, string startDir)
    {
        // パスを含む名前（.\build や C:\tools\x）は、その場所だけを探す
        if (program.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(startDir, program)));
            return directory is null ? [] : [directory];
        }
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return new[] { startDir, Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) }
            .Concat(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(dir => Environment.ExpandEnvironmentVariables(dir.Trim('"')));
    }
}
