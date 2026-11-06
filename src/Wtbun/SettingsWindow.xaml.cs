using System.Windows;
using Wtbun.Localization;

namespace Wtbun;

/// <summary>
/// 「≡」の「環境設定」。今は表示する言語だけ。保存と切り替えは呼び出し元（MainWindow）で行う。
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>選んだ言語（Loc.Languages の名前）。</summary>
    public string SelectedLanguage { get; private set; }

    /// <param name="language">今の言語。最初に選んでおく。</param>
    public SettingsWindow(string language)
    {
        InitializeComponent();
        Title = Loc.Format("Settings_Title", AppProfile.ShortTitle);

        SelectedLanguage = language;
        LanguageBox.ItemsSource = Loc.Languages.Select(l => new LanguageItem(l.Name, l.DisplayName)).ToList();
        LanguageBox.SelectedValue = language;

        Loaded += (_, _) => DialogPlacement.PlaceBelowOwner(this);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (LanguageBox.SelectedValue is string language)
        {
            SelectedLanguage = language;
        }
        DialogResult = true;
    }

    /// <summary>一覧の 1 行。画面の読み上げでも言語の名前が読まれるよう、ToString で名前を返す。</summary>
    private sealed record LanguageItem(string Name, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
