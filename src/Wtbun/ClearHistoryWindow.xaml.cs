using System.Windows;
using Wtbun.Config;
using Wtbun.Localization;

namespace Wtbun;

/// <summary>
/// フォルダーの履歴の消去の確認。無効になったフォルダーだけか、すべてかを選ぶ。
/// </summary>
public partial class ClearHistoryWindow : Window
{
    private readonly List<string> _unusable;
    private readonly int _allCount;

    /// <summary>消去するフォルダー。キャンセルしたときは空。</summary>
    public IReadOnlyList<string> Removing { get; private set; } = [];

    /// <summary>すべてを消去する。</summary>
    public bool RemovesAll { get; private set; }

    public ClearHistoryWindow(ProjectHistory history)
    {
        InitializeComponent();
        Title = Loc.Format("ClearHistory_Title", AppProfile.ShortTitle);

        // 開いた時点で調べる（ネットワーク上のフォルダーなどで遅いときも、ここで 1 回だけにする）
        _unusable = history.UnusableDirectories();
        _allCount = history.Directories.Count;
        UnusableOption.Content = Loc.Format("ClearHistory_Unusable", _unusable.Count);
        AllOption.Content = Loc.Format("ClearHistory_All", _allCount);
        // 無効なものがなければ、すべてを選んでおく（無効なほうを選んだまま消去しても、何も起きないので）
        if (_unusable.Count == 0)
        {
            UnusableOption.IsEnabled = false;
            AllOption.IsChecked = true;
        }

        Loaded += (_, _) => DialogPlacement.PlaceBelowOwner(this);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        RemovesAll = AllOption.IsChecked == true;
        Removing = RemovesAll ? [] : _unusable;
        DialogResult = true;
    }
}
