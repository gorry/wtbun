using System.IO;

namespace Wtbun.Config;

/// <summary>
/// 「＋」で開いたプロジェクトのフォルダーの履歴。新しい順。%APPDATA%\wtbun\history.json に保存する。
/// </summary>
public sealed class ProjectHistory
{
    private const string FileName = "history.json";

    public const int MaxCount = 20;

    public int Version { get; set; } = 1;

    public List<string> Directories { get; set; } = [];

    public static ProjectHistory Load()
    {
        var history = UserDataFile.Load<ProjectHistory>(FileName) ?? new ProjectHistory();
        // 手で書き換えられたときのために、空の行・重複・上限を超えた分を除いておく
        var directories = history.Directories;
        history.Directories = [];
        foreach (var directory in Enumerable.Reverse(directories ?? []))
        {
            if (!string.IsNullOrWhiteSpace(directory))
            {
                history.AddFirst(directory);
            }
        }
        return history;
    }

    public void Save() => UserDataFile.Save(FileName, this);

    /// <summary>先頭に置く。すでにあれば先頭へ移す。</summary>
    public void Add(string directory)
    {
        AddFirst(directory);
        Save();
    }

    public void Clear()
    {
        Directories.Clear();
        Save();
    }

    /// <summary>
    /// まだプロジェクトとして使えるフォルダーか。フォルダーがなくなったか、あっても構成ファイル（.wtbun.toml）が
    /// なくなったものは使えないとみなす。
    /// </summary>
    public static bool IsUsable(string directory) =>
        Directory.Exists(directory) && File.Exists(Path.Combine(directory, ProjectConfigStore.FileName));

    /// <summary>使えなくなったフォルダー（<see cref="IsUsable"/>）。</summary>
    public List<string> UnusableDirectories() => Directories.Where(d => !IsUsable(d)).ToList();

    /// <summary>指定したフォルダーを履歴から除く。</summary>
    public void Remove(IEnumerable<string> directories)
    {
        var removing = directories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Directories.RemoveAll(removing.Contains);
        Save();
    }

    private void AddFirst(string directory)
    {
        string full;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }
        Directories.RemoveAll(d => string.Equals(d, full, StringComparison.OrdinalIgnoreCase));
        Directories.Insert(0, full);
        if (Directories.Count > MaxCount)
        {
            Directories.RemoveRange(MaxCount, Directories.Count - MaxCount);
        }
    }
}
