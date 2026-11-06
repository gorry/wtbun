using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Interop;
using Wtbun.Native;
using Wtbun.Projects;
using Wtbun.Localization;

namespace Wtbun;

/// <summary>
/// 「≡」の「wtbun について」。アプリ名・バージョン・著作権表示・URL と、NOTICE・LICENSE の本文を出す。
/// 値は Profile.ini（AppProfile）から、本文は exe に入れた NOTICE・LICENSE から読む。
/// </summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        Title = Loc.Format("About_Title", AppProfile.ShortTitle);

        AppIcon.Source = ProjectIcon.Default;
        TitleText.Text = Loc.Format("About_AppName", AppProfile.Title, AppProfile.ShortTitle);
        VersionText.Text = Loc.Format("About_Version", AppProfile.Version);
        CopyrightText.Text = string.IsNullOrEmpty(AppProfile.Author)
            ? AppProfile.Copyright
            : $"{AppProfile.Copyright}\n{AppProfile.Author}";

        // Profile.ini に URL がなければ、その行は出さない
        // 新しいバージョンを探せるよう、リリースのページを出す
        SetLink(ProjectLink, ProjectLinkLine, AppProfile.ReleaseUrl);
        ProjectLinkText.Text = AppProfile.ReleaseUrl;
        SetLink(NoticeLink, NoticeLinkLine, AppProfile.NoticeUrl);

        NoticeBox.Text = ReadResource("NOTICE");
        LicenseBox.Text = ReadResource("LICENSE");

        Loaded += (_, _) => DialogPlacement.PlaceBelowOwner(this);
    }

    private static void SetLink(Hyperlink link, UIElement line, string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            link.NavigateUri = uri;
            link.ToolTip = url;
        }
        else
        {
            line.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>exe に入れた文書を読む。改行は Windows の形にそろえる（TextBox で正しく折り返すように）。</summary>
    private static string ReadResource(string name)
    {
        using var stream = typeof(AboutWindow).Assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return "";
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\r\n");
    }

    /// <summary>既定のブラウザーで開く。</summary>
    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Hyperlink { NavigateUri: { } uri })
        {
            return;
        }
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine(ex);
            MessageBox.Show(this, Loc.Get("About_CannotOpenBrowser"), AppProfile.ShortTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 問い合わせのときに貼り付けられるよう、アプリ名とバージョンをコピーする。
    /// WPF の Clipboard.SetText は、クリップボードを見張るアプリがあると失敗することがあるので使わない（ClipboardText を参照）。
    /// </summary>
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var text = $"{AppProfile.Title} ({AppProfile.ShortTitle}) {AppProfile.Version}";
        if (!ClipboardText.TrySet(text, new WindowInteropHelper(this).Handle))
        {
            // ほかのアプリがクリップボードを開いたままのとき
            MessageBox.Show(this, Loc.Get("About_CannotCopy"), AppProfile.ShortTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
