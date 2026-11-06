using System.Windows;
using Wtbun.Localization;

namespace Wtbun;

/// <summary>
/// すでに開いているプロジェクトをもう一度開こうとしたときの確認。開き直すか、前面に表示するだけかを選ぶ。
/// </summary>
public partial class AlreadyOpenWindow : Window
{
    public enum Choice
    {
        Cancel,
        Activate,
        Reopen,
    }

    /// <summary>選んだもの。閉じるボタンや Esc で閉じたときは Cancel。</summary>
    public Choice Chosen { get; private set; } = Choice.Cancel;

    /// <param name="terminalHint">Windows Terminal の閉じる確認も出る設定のときの案内。出さないときは null。</param>
    public AlreadyOpenWindow(string projectName, string? terminalHint)
    {
        InitializeComponent();
        Title = AppProfile.ShortTitle;
        MessageText.Text = Loc.Format("AlreadyOpen_Message", projectName);
        if (terminalHint is not null)
        {
            TerminalHint.Text = terminalHint;
            TerminalHint.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) => DialogPlacement.PlaceBelowOwner(this);
    }

    private void Reopen_Click(object sender, RoutedEventArgs e)
    {
        Chosen = Choice.Reopen;
        DialogResult = true;
    }

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        Chosen = Choice.Activate;
        DialogResult = true;
    }
}
