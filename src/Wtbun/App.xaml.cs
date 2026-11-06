using System.Windows;
using Wtbun.Config;

namespace Wtbun;

public partial class App : Application
{
    private const string DataDirOption = "--data-dir";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 状態や設定を読む前に、保存先を決める（MainWindow を作ると読み始める）
        if (!ApplyOptions(e.Args))
        {
            MessageBox.Show(
                $"保存先のフォルダーが指定されていません。{DataDirOption} の後ろにフォルダーを指定してください。",
                AppProfile.ShortTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    /// <summary>
    /// 起動時の引数を読む。今は保存先の指定（--data-dir フォルダー、または --data-dir=フォルダー）だけ。
    /// 知らない引数は無視する。指定の書き方が正しくなければ false。
    /// </summary>
    private static bool ApplyOptions(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            string? folder = null;
            if (args[i] == DataDirOption)
            {
                folder = i + 1 < args.Length ? args[++i] : "";
            }
            else if (args[i].StartsWith(DataDirOption + "=", StringComparison.Ordinal))
            {
                folder = args[i][(DataDirOption.Length + 1)..];
            }

            if (folder is null)
            {
                continue;
            }
            if (string.IsNullOrWhiteSpace(folder))
            {
                return false;
            }
            UserDataFile.UseFolder(folder);
        }
        return true;
    }
}
