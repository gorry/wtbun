using System.ComponentModel;
using System.IO;
using System.Windows.Media;

namespace Wtbun.Projects;

/// <summary>
/// wtbun で開いているプロジェクト 1 つぶん。
/// </summary>
/// <param name="displayName">タブに出す名前（構成ファイルの name）。空ならフォルダ名を出す。</param>
/// <param name="windowName">前回のウィンドウを引き継ぐときは、そのときの名前。</param>
public sealed class Project(string directory, string? displayName = null, string? windowName = null) : INotifyPropertyChanged
{
    private string? _displayName = displayName;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Directory { get; } = Path.GetFullPath(directory);

    /// <summary>タブに出すアイコン。開いたとき・開き直したとき・構成の編集ダイアログで保存したときに読む。</summary>
    public ImageSource Icon { get; private set; } = ProjectIcon.Load(directory);

    public string Name => _displayName?.Trim() is { Length: > 0 } given
        ? given
        : Path.GetFileName(Directory.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } folder
            ? folder
            : Directory;

    /// <summary>
    /// wt.exe の -w に渡すウィンドウ名。数字だけの名前はウィンドウ番号と見なされるので、接頭辞を付ける。
    /// </summary>
    public string WindowName { get; } = windowName ?? $"{AppProfile.ShortTitle}-{Guid.NewGuid():N}"[..(AppProfile.ShortTitle.Length + 13)];

    /// <summary>このプロジェクトの Windows Terminal のウィンドウ。開く前は IntPtr.Zero。</summary>
    public IntPtr WindowHandle { get; set; }

    /// <summary>ウィンドウを閉じて開き直している途中。ウィンドウが閉じても、タブは外さない。</summary>
    public bool IsReopening { get; set; }

    /// <summary>タブに出す名前を変える。</summary>
    public void Rename(string? displayName)
    {
        _displayName = displayName;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
    }

    /// <summary>アイコンのファイルを読み直す。</summary>
    public void ReloadIcon()
    {
        Icon = ProjectIcon.Load(Directory);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
    }

    public bool IsSameDirectory(string directory) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Directory),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)),
            StringComparison.OrdinalIgnoreCase);
}
