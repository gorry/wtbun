using System.Globalization;
using System.Windows;
using Wtbun.Config;
using Wtbun.Localization;

namespace Wtbun;

public partial class App : Application
{
    private const string DataDirOption = "--data-dir";
    private const string LanguageOption = "--lang";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 状態や設定を読む前に、保存先を決める（MainWindow を作ると読み始める）
        var options = ReadOptions(e.Args);
        if (options.DataDir is { } folder)
        {
            UserDataFile.UseFolder(folder);
        }

        // メッセージを出す前に、言語を決める。引数が正しくなくて終わるときは、設定を書き込まない
        ApplyLanguage(options.Language, records: options.Error is null);

        if (options.Error is { } error)
        {
            MessageBox.Show(error(), AppProfile.ShortTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    /// <summary>
    /// 表示する言語を決める。初めての起動なら Windows の表示言語から決めて記録する。
    /// --lang の指定（正しいもの）があれば、記録は変えずにそれを使う。
    /// </summary>
    private static void ApplyLanguage(string? overridden, bool records)
    {
        var settings = AppSettings.Load();
        var recorded = Loc.Normalize(settings.Language);
        if (recorded is null)
        {
            recorded = Loc.Detect(CultureInfo.CurrentUICulture);
            if (records)
            {
                settings.Language = recorded;
                settings.Save();
            }
        }
        Loc.Apply(overridden ?? recorded);
    }

    private sealed record Options(string? DataDir, string? Language, Func<string>? Error);

    /// <summary>
    /// 起動時の引数を読む。保存先（--data-dir フォルダー、--data-dir=フォルダー）と言語（--lang 名前、--lang=名前）。
    /// 知らない引数は無視する。書き方が正しくなければ Error に文言を作る関数を入れる（文言は言語を決めてから作る）。
    /// </summary>
    private static Options ReadOptions(string[] args)
    {
        string? dataDir = null;
        string? language = null;
        Func<string>? error = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (ReadValue(args, ref i, DataDirOption) is { } folder)
            {
                if (string.IsNullOrWhiteSpace(folder))
                {
                    error ??= () => Loc.Format("App_NoDataDir", DataDirOption);
                    continue;
                }
                dataDir = folder;
            }
            else if (ReadValue(args, ref i, LanguageOption) is { } name)
            {
                language = Loc.Normalize(name);
                if (language is null)
                {
                    error ??= () => Loc.Format("App_BadLanguage", LanguageOption, string.Join(", ", Loc.Languages.Select(l => l.Name)));
                }
            }
        }
        return new Options(dataDir, language, error);
    }

    /// <summary>「オプション 値」か「オプション=値」なら値（なければ ""）。そのオプションでなければ null。</summary>
    private static string? ReadValue(string[] args, ref int i, string option)
    {
        if (args[i] == option)
        {
            return i + 1 < args.Length ? args[++i] : "";
        }
        if (args[i].StartsWith(option + "=", StringComparison.Ordinal))
        {
            return args[i][(option.Length + 1)..];
        }
        return null;
    }
}
