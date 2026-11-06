using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Tomlyn;
using Wtbun.Config;
using Wtbun.Layout;
using Wtbun.Native;
using Wtbun.Projects;
using Wtbun.Terminal;

namespace Wtbun;

public partial class MainWindow : Window
{
    // 利用者が Windows Terminal 側でウィンドウを閉じたことに気づくための間隔
    private static readonly TimeSpan WindowWatchInterval = TimeSpan.FromMilliseconds(500);

    // 移動やリサイズが落ち着いてから、ほかのウィンドウにそろえるまでの待ち時間
    private static readonly TimeSpan SyncDelay = TimeSpan.FromMilliseconds(200);

    // 変更が続いたときにまとめて 1 回保存するための待ち時間
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    // 前面になったウィンドウがいちばん上に上がりきるのを待ってから、タブ列とターミナルを並べ直すまでの時間
    private static readonly TimeSpan RaiseRetryDelay = TimeSpan.FromMilliseconds(150);

    // 選ばれているウィンドウが最小化されてから、Windows が次のウィンドウを前面にし終えるのを待つ時間
    private static readonly TimeSpan MinimizeAllDelay = TimeSpan.FromMilliseconds(120);

    // まとめて最小化するとき、全部が最小化されるのを待つ回数の上限
    private const int MaxMinimizeTicks = 10;

    // 終了するとき、ウィンドウが閉じたか・閉じる確認が出たかを見る間隔
    private static readonly TimeSpan ExitPollInterval = TimeSpan.FromMilliseconds(200);

    // 終了するとき、閉じるよう求めてから、閉じない（確認も出ていない）ウィンドウを取り消されたとみなすまでの時間
    private static readonly TimeSpan ExitCancelGrace = TimeSpan.FromSeconds(1.5);

    private readonly DispatcherTimer _windowWatcher;
    private readonly DispatcherTimer _syncTimer;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _raiseTimer;
    private IntPtr _raiseFront;
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ProjectHistory _history = ProjectHistory.Load();
    private bool _restoring;

    // 終了の途中（ウィンドウを 1 つずつ閉じている間）。状態を保存し直さず、閉じたウィンドウのタブも外さない
    private bool _exiting;

    // ウィンドウを閉じ終えて、wtbun のウィンドウを本当に閉じるとき
    private bool _exitReady;
    private readonly WindowEventWatcher _windowEvents = new();
    private readonly CloseRequestWatcher _closeRequests = new();
    private TerminalCloseGuard? _closeGuard;
    private bool _confirmingClose;
    private bool _takingForeground;
    private readonly TerminalLayout _layout;
    private IntPtr _ownHandle;
    private bool _suppressActivation;
    private bool _inMoveSize;

    // タブ列と全ウィンドウをまとめて最小化している間
    private bool _allMinimized;
    private readonly DispatcherTimer _minimizeTimer;
    private int _minimizeTicks;
    private Point _dragStart;
    private Project? _dragCandidate;

    // タブ列のドラッグ中の、ドラッグを始めたときのタブ列と共有の位置
    private RECT? _stripAtDragStart;
    private RECT? _sharedAtDragStart;

    public ObservableCollection<Project> Projects { get; } = [];

    private Project? SelectedProject => ProjectTabs.SelectedItem as Project;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Title = AppProfile.ShortTitle;
        ExitMenu.Header = $"{AppProfile.ShortTitle} を終了";
        // ダイアログを出すので、末尾に「…」を付ける
        AboutMenu.Header = $"{AppProfile.ShortTitle} について…";
        _layout = new TerminalLayout(Height);

        _windowWatcher = new DispatcherTimer { Interval = WindowWatchInterval };
        _windowWatcher.Tick += (_, _) => RemoveClosedProjects();
        _windowWatcher.Start();

        _syncTimer = new DispatcherTimer { Interval = SyncDelay };
        _syncTimer.Tick += (_, _) =>
        {
            _syncTimer.Stop();
            if (SelectedProject is { WindowHandle: var hWnd } && hWnd != IntPtr.Zero)
            {
                SyncFrom(hWnd);
            }
        };

        _raiseTimer = new DispatcherTimer { Interval = RaiseRetryDelay };
        _raiseTimer.Tick += (_, _) =>
        {
            _raiseTimer.Stop();
            // 待っている間に別のウィンドウが前面になっていたら、並べ直さない
            if (WindowOps.IsForeground(_raiseFront))
            {
                PlaceTogether();
            }
        };

        _minimizeTimer = new DispatcherTimer { Interval = MinimizeAllDelay };
        _minimizeTimer.Tick += (_, _) =>
        {
            MinimizeAll();
            // 全部が最小化されるまでは、途中の扱いにしておく（応答しないウィンドウがあれば、回数で諦める）
            if (IsAllMinimized() || ++_minimizeTicks >= MaxMinimizeTicks)
            {
                _minimizeTimer.Stop();
            }
        };

        _windowEvents.Foreground += OnTerminalForeground;
        _windowEvents.MoveSizeStarted += OnTerminalMoveSizeStarted;
        _windowEvents.MoveSizeEnded += OnTerminalMoveSizeEnded;
        _windowEvents.LocationChanged += OnTerminalLocationChanged;
        _windowEvents.MinimizeStarted += OnTerminalMinimizeStarted;
        _windowEvents.MinimizeEnded += OnTerminalMinimizeEnded;
        _closeRequests.CloseRequested += hWnd => Dispatcher.BeginInvoke(() => OnTerminalCloseRequested(hWnd));

        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveState();
        };
        Projects.CollectionChanged += (_, _) => MarkStateChanged();

        ShowInTaskbarMenu.IsChecked = _settings.ShowTerminalsInTaskbar;

        SourceInitialized += (_, _) =>
        {
            _ownHandle = new WindowInteropHelper(this).Handle;
            _closeGuard = new TerminalCloseGuard(_ownHandle);
            HwndSource.FromHwnd(_ownHandle).AddHook(WndProc);
        };
        Loaded += async (_, _) => await RestoreAsync();
        Activated += (_, _) =>
        {
            if (_takingForeground)
            {
                return;
            }
            if (_minimizeTimer.IsEnabled)
            {
                MinimizeNext(_ownHandle);
            }
            else
            {
                RaiseSelectedTerminal();
            }
        };
        LocationChanged += (_, _) => OnStripMovedByDrag();
        StateChanged += (_, _) => OnStripStateChanged();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _windowEvents.Dispose();
            _closeRequests.Dispose();
            _closeGuard?.Dispose();
        };
        // サインアウト・シャットダウンのときは、ウィンドウを閉じるのを待たずに終える（Windows がすべて閉じる）
        Application.Current.SessionEnding += (_, _) =>
        {
            _closeGuard?.Dispose();
            SaveState();
            _exitReady = true;
        };
    }

    /// <summary>
    /// 前回の終了時に開いていたプロジェクトを、同じ順番・同じ位置で開き直す。
    /// </summary>
    private async Task RestoreAsync()
    {
        var state = AppStateStore.Load();
        if (state is null)
        {
            return;
        }

        _restoring = true;
        try
        {
            // つながっていないモニターの位置は使わない
            if (state.StripRect is { } strip && WindowOps.IsOnScreen(strip))
            {
                WindowOps.Move(_ownHandle, strip);
            }
            if (state.SharedRect is { } shared && WindowOps.IsOnScreen(shared))
            {
                _layout.SharedRect = shared;
            }

            var missing = new List<string>();
            foreach (var previous in state.Projects)
            {
                if (!System.IO.Directory.Exists(previous.Directory))
                {
                    missing.Add(previous.Directory);
                    continue;
                }
                await OpenProjectAsync(previous.Directory, previous);
            }

            if (Projects.Count > 0)
            {
                ProjectTabs.SelectedIndex = Math.Clamp(state.SelectedIndex, 0, Projects.Count - 1);
                if (SelectedProject is { } selected)
                {
                    ActivateProject(selected);
                }
            }
            if (missing.Count > 0)
            {
                ShowError("次のプロジェクトのフォルダーが見つかりません。\n\n"
                    + string.Join("\n", missing));
            }
        }
        finally
        {
            _restoring = false;
        }
        SaveState();
    }

    private void MarkStateChanged()
    {
        if (!_restoring && !_exiting)
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }

    private void SaveState()
    {
        if (_ownHandle == IntPtr.Zero)
        {
            return;
        }
        AppStateStore.Save(new AppState
        {
            Projects = Projects
                .Select(p => new ProjectState { Directory = p.Directory, WindowName = p.WindowName, WindowHandle = p.WindowHandle.ToInt64() })
                .ToList(),
            SelectedIndex = ProjectTabs.SelectedIndex,
            SharedRect = _layout.SharedRect,
            // 最小化している間の GetWindowRect は画面の外を指すので、保存しない
            StripRect = WindowOps.IsMinimized(_ownHandle) ? null : WindowOps.GetRect(_ownHandle),
        });
    }

    /// <summary>
    /// wtbun のウィンドウを閉じようとしたとき（「wtbun を終了」・Alt＋F4 など）。先にターミナルのウィンドウを閉じる
    /// （<see cref="ExitAsync"/>）。閉じ終えたら、もう一度閉じる。サインアウト・シャットダウンのときは待たない。
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_exitReady)
        {
            return;
        }
        e.Cancel = true;
        if (!_exiting)
        {
            _ = ExitAsync();
        }
    }

    /// <summary>
    /// wtbun を終えるとき、開いていたプロジェクトを保存してから、ウィンドウを 1 つずつ閉じる。
    /// Windows Terminal は、全ウィンドウが 1 つのプロセスで動いていて、「すべてのタブを閉じますか？」の確認を同時に
    /// 1 つしか出さない。確認が出ている間に、ほかのウィンドウに閉じるよう求めても無視される（まとめて求めると、1 つしか
    /// 閉じなかった）。そこで、1 つ閉じるよう求めたら、そのウィンドウが閉じるか、確認が取り消されるまで待ってから次へ進む。
    /// 取り消されたウィンドウは残る（次の起動で、残っていたウィンドウはそのまま使う）。
    /// </summary>
    private async Task ExitAsync()
    {
        _exiting = true;
        _saveTimer.Stop();
        _syncTimer.Stop();
        _windowWatcher.Stop();
        // 閉じていく途中のウィンドウの前面化・移動に、タブ列が付いて行かないように
        _windowEvents.Dispose();
        // 終了中は確認を出さない（閉じるボタンと Alt+F4 は Windows Terminal の通常の動きに戻る）
        _closeRequests.Dispose();
        _closeGuard?.Dispose();
        SaveState();
        IsEnabled = false;

        foreach (var project in Projects.ToList())
        {
            var hWnd = project.WindowHandle;
            if (hWnd == IntPtr.Zero || !TerminalWindows.Exists(hWnd))
            {
                continue;
            }
            // 閉じる確認で取り消されて残ったウィンドウを、タスクバーから選べるようにしておく
            if (!_settings.ShowTerminalsInTaskbar)
            {
                Taskbar.SetButtonVisible(hWnd, true);
            }
            // 確認が出たときに見えるよう、前面に出してから閉じる（前面に出せなくても、いちばん上には置く）
            WindowOps.BringToTopWithoutActivating(hWnd);
            TerminalWindows.Activate(hWnd);
            TerminalWindows.RequestClose(hWnd);
            await WaitClosedOrCanceledAsync(hWnd);
        }

        _exitReady = true;
        // 閉じるウィンドウがなかったときは、ここまで待たずに来るので、まだ OnClosing の中にいる。
        // その中で Close を呼んでも無視されるので、OnClosing を抜けてから閉じ直す
        // 閉じ終わるのは待たない（待つと OnClosing を抜けられない）
        _ = Dispatcher.BeginInvoke(Close);
    }

    /// <summary>
    /// 閉じるよう求めたウィンドウが閉じるか、閉じる確認が取り消されるまで待つ。確認が出ている間は待ち続ける。
    /// 確認が出ないまま <see cref="ExitCancelGrace"/> たっても閉じなければ、取り消された（または閉じない）とみなす。
    /// </summary>
    private static async Task WaitClosedOrCanceledAsync(IntPtr hWnd)
    {
        var lastSeen = DateTime.UtcNow;
        while (TerminalWindows.Exists(hWnd))
        {
            await Task.Delay(ExitPollInterval);
            if (!TerminalWindows.Exists(hWnd))
            {
                return;
            }
            if (await Task.Run(() => TerminalWindows.IsConfirmingClose(hWnd)))
            {
                lastSeen = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - lastSeen > ExitCancelGrace)
            {
                return;
            }
        }
    }

    /// <summary>設定に従って、ウィンドウをタスクバーから隠す。出す設定のときは何もしない（Windows Terminal の通常の動き）。</summary>
    private void ApplyTaskbarSetting(IntPtr hWnd)
    {
        if (!_settings.ShowTerminalsInTaskbar)
        {
            Taskbar.SetButtonVisible(hWnd, false);
        }
    }

    private void ApplyTaskbarSettingToAll()
    {
        foreach (var project in Projects)
        {
            if (project.WindowHandle != IntPtr.Zero)
            {
                Taskbar.SetButtonVisible(project.WindowHandle, _settings.ShowTerminalsInTaskbar);
            }
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // タスクバーなどからの閉じる要求。確認は WndProc の外で出す
        if (msg == TerminalCloseGuard.CloseRequestedMessage)
        {
            var terminal = wParam;
            Dispatcher.BeginInvoke(() => OnTerminalCloseRequested(terminal));
            handled = true;
        }
        // エクスプローラーが再起動すると、隠したボタンも元に戻るので隠し直す
        else if (msg == Taskbar.TaskbarCreatedMessage && !_settings.ShowTerminalsInTaskbar)
        {
            ApplyTaskbarSettingToAll();
        }
        // マウスのドラッグも、システムメニューの「移動」（キーボード）も、ここを通る
        else if (msg == NativeMethods.WM_ENTERSIZEMOVE)
        {
            OnStripMoveStarted();
        }
        else if (msg == NativeMethods.WM_EXITSIZEMOVE)
        {
            OnStripMoveEnded();
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// wtbun が前面に来たとき（タスクバーのボタンを押したときなど）に、選ばれているウィンドウも一緒に前に出す。
    /// 入力先は wtbun のままにする。
    /// </summary>
    private void RaiseSelectedTerminal() => KeepTogether(_ownHandle);

    /// <summary>
    /// タブ列と選ばれているターミナルを、一緒にほかのウィンドウより前に出す。上からタブ列、ターミナルの順にする。
    /// <paramref name="front"/> は前面になった方。前面になったという知らせの時点では、前面になった方がまだいちばん上に
    /// 上がりきっていないことがあるので、少し待ってからもう一度置き直す。
    /// </summary>
    private void KeepTogether(IntPtr front)
    {
        _raiseFront = front;
        PlaceTogether();
        _raiseTimer.Stop();
        _raiseTimer.Start();
    }

    private void PlaceTogether()
    {
        if (_raiseFront == IntPtr.Zero || _ownHandle == IntPtr.Zero
            || SelectedProject is not { WindowHandle: var terminal } || terminal == IntPtr.Zero
            || !TerminalWindows.Exists(terminal) || WindowOps.IsMinimized(terminal))
        {
            return;
        }
        if (_raiseFront != _ownHandle && _raiseFront != terminal)
        {
            return;
        }
        if (_raiseFront == _ownHandle)
        {
            // Windows Terminal のウィンドウは「wtbun のすぐ後ろ」を指示すると wtbun の上に来るなど、思った位置に来ないので、
            // いちばん上に持ち上げる
            WindowOps.LiftWithoutActivating(terminal);
        }
        // タブ列がターミナルより後ろにあると、「ウィンドウの下に影を表示する」が有効なときにターミナルの影で暗くなるので、
        // タブ列をいちばん上にする。前面にいない wtbun からは HWND_TOP が無視されるので、この方法で持ち上げる
        WindowOps.LiftWithoutActivating(_ownHandle);
    }

    private void ShowInTaskbar_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowTerminalsInTaskbar = ShowInTaskbarMenu.IsChecked;
        _settings.Save();
        ApplyTaskbarSettingToAll();
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "プロジェクトのフォルダーを選択" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        _history.Add(dialog.FolderName);
        await OpenProjectAsync(dialog.FolderName);
    }

    /// <summary>「＋」のメニューを開くたびに、履歴の一覧を作り直す。</summary>
    private void OpenMenu_Opened(object sender, RoutedEventArgs e)
    {
        HistoryMenu.Items.Clear();
        foreach (var directory in _history.Directories)
        {
            var item = new MenuItem
            {
                // 「_」はアクセスキーの印として扱われるので、2 つ重ねてそのまま表示させる
                Header = directory.Replace("_", "__"),
                Tag = directory,
            };
            item.Click += HistoryItem_Click;
            HistoryMenu.Items.Add(item);
        }
        HistoryMenu.IsEnabled = HistoryMenu.Items.Count > 0;
        ClearHistoryMenu.IsEnabled = HistoryMenu.Items.Count > 0;
    }

    private async void HistoryItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string directory })
        {
            return;
        }
        if (!System.IO.Directory.Exists(directory))
        {
            ShowError($"フォルダーが見つかりません。\n\n{directory}");
            return;
        }
        _history.Add(directory);
        await OpenProjectAsync(directory);
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ClearHistoryWindow(_history) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        if (dialog.RemovesAll)
        {
            _history.Clear();
        }
        else
        {
            _history.Remove(dialog.Removing);
        }
    }

    /// <param name="previous">前回の起動で開いていたプロジェクトを開き直すときの、そのときの状態。</param>
    private async Task OpenProjectAsync(string directory, ProjectState? previous = null)
    {
        // すでに開いているプロジェクトなら、開き直すか、前面に表示するだけかを選んでもらう
        var existing = Projects.FirstOrDefault(p => p.IsSameDirectory(directory));
        if (existing is not null)
        {
            await OpenExistingAsync(existing, asks: previous is null);
            return;
        }

        // 前回のウィンドウが残っていれば、新しく開かずにそのまま使う
        var previousHandle = new IntPtr(previous?.WindowHandle ?? 0);
        if (TerminalWindows.IsTerminalWindow(previousHandle) && Projects.All(p => p.WindowHandle != previousHandle))
        {
            var adopted = new Project(directory, ProjectConfigStore.TryLoadName(directory), previous!.WindowName)
            {
                WindowHandle = previousHandle,
            };
            Projects.Add(adopted);
            ProjectTabs.SelectedItem = adopted;
            OnTerminalOpened(previousHandle);
            return;
        }

        ProjectConfig config;
        try
        {
            config = ProjectConfigStore.LoadOrCreate(directory);
        }
        catch (Exception ex) when (ConfigErrorMessage(ex) is { } message)
        {
            Debug.WriteLine(ex);
            ShowError(message);
            return;
        }

        var project = new Project(directory, config.Name);
        Projects.Add(project);
        ProjectTabs.SelectedItem = project;

        if (await OpenWindowAsync(project, config))
        {
            OnTerminalOpened(project.WindowHandle);
        }
    }

    /// <summary>
    /// プロジェクトのウィンドウを開く。開けなければ、メッセージを出してタブを外し、false を返す。
    /// </summary>
    private async Task<bool> OpenWindowAsync(Project project, ProjectConfig config)
    {
        try
        {
            project.WindowHandle = await TerminalLauncher.OpenAsync(project.WindowName, project.Directory, config);
            return true;
        }
        catch (TerminalLaunchException ex)
        {
            Debug.WriteLine(ex);
            RemoveProject(project);
            ShowError(ex.InnerException is null
                ? "ターミナルのウィンドウを開けません。もう一度お試しください。"
                : "Windows Terminal を起動できません。インストールされているか確認してください。");
            return false;
        }
    }

    /// <summary>
    /// すでに開いているプロジェクトをもう一度開こうとしたとき。開き直す・前面に表示する・キャンセルから選んでもらう。
    /// </summary>
    /// <param name="asks">false なら選んでもらわずに、前面に表示するだけにする（前回の状態を復元するとき）。</param>
    private async Task OpenExistingAsync(Project project, bool asks)
    {
        // まだ開いている途中か、開き直している途中なら、タブを選ぶだけにする
        if (!asks || project.WindowHandle == IntPtr.Zero || project.IsReopening)
        {
            ProjectTabs.SelectedItem = project;
            ActivateProject(project);
            return;
        }

        var hint = TerminalSettings.WarnsWhenClosingTabs() ? TerminalCloseWarningHint : null;
        var dialog = new AlreadyOpenWindow(project.Name, hint) { Owner = this };
        dialog.ShowDialog();
        // 選んでいる間に、Windows Terminal でウィンドウが閉じられたとき
        if (!Projects.Contains(project))
        {
            return;
        }
        switch (dialog.Chosen)
        {
            case AlreadyOpenWindow.Choice.Activate:
                ProjectTabs.SelectedItem = project;
                ActivateProject(project);
                break;
            case AlreadyOpenWindow.Choice.Reopen:
                ProjectTabs.SelectedItem = project;
                await ReopenProjectAsync(project, confirms: false);
                break;
        }
    }

    /// <summary>
    /// 確認してから、プロジェクトのウィンドウを閉じて、今の構成ファイルで開き直す。タブの位置は変えない。
    /// 構成ファイルが読めなければ閉じない。Windows Terminal の閉じる確認で取り消されたときは、そのまま残す。
    /// </summary>
    /// <param name="confirms">false なら確認しない（呼び出し元ですでに確認したとき）。</param>
    private async Task ReopenProjectAsync(Project project, bool confirms = true)
    {
        var oldWindow = project.WindowHandle;
        // まだ開いている途中か、開き直している途中
        if (oldWindow == IntPtr.Zero || project.IsReopening)
        {
            return;
        }
        if (confirms && !ConfirmClosing($"「{project.Name}」を開き直しますか？実行中のコマンドは終了します。") || !Projects.Contains(project))
        {
            return;
        }

        ProjectConfig config;
        try
        {
            config = ProjectConfigStore.LoadOrCreate(project.Directory);
        }
        catch (Exception ex) when (ConfigErrorMessage(ex) is { } message)
        {
            Debug.WriteLine(ex);
            ShowError(message);
            return;
        }

        project.IsReopening = true;
        try
        {
            if (TerminalWindows.Exists(oldWindow))
            {
                // 確認が出たときに見えるよう、前面に出してから閉じる
                TerminalWindows.Activate(oldWindow);
                TerminalWindows.RequestClose(oldWindow);
                await WaitClosedOrCanceledAsync(oldWindow);
                if (TerminalWindows.Exists(oldWindow))
                {
                    return;
                }
            }
            if (_exiting)
            {
                return;
            }

            project.WindowHandle = IntPtr.Zero;
            project.Rename(config.Name);
            project.ReloadIcon();
            if (await OpenWindowAsync(project, config))
            {
                // 閉じたときに別のタブへ移っているので、開き直したタブに戻す
                SelectWithoutActivating(project);
                OnTerminalOpened(project.WindowHandle);
                // タブの並びも選択も変わらないことがあるので、新しいウィンドウを必ず保存する
                MarkStateChanged();
            }
        }
        finally
        {
            project.IsReopening = false;
        }
    }

    /// <summary>構成ファイルを扱えなかったときに出す文言。構成ファイルの問題でなければ null。</summary>
    private static string? ConfigErrorMessage(Exception ex) => ex switch
    {
        TomlException { Line: { } line } => $"{ProjectConfigStore.FileName} の {line} 行目が正しくありません。",
        TomlException => $"{ProjectConfigStore.FileName} の書き方が正しくありません。",
        UnknownConfigKeyException { MisplacedInTab: true } unknown => $"{ProjectConfigStore.FileName} の「{unknown.Key}」は最初の [[tabs]] より前に書いてください。",
        UnknownConfigKeyException unknown => $"{ProjectConfigStore.FileName} の「{unknown.Key}」は使用できない項目です。",
        InvalidConfigValueException { Key: "tab_color" } => $"{ProjectConfigStore.FileName} の「tab_color」が正しくありません。'#RRGGBB' の形式で書いてください。",
        InvalidConfigValueException { Key: "split" } => $"{ProjectConfigStore.FileName} の「split」が正しくありません。'right' か 'down' を書いてください。",
        InvalidConfigValueException { Key: "size" } => $"{ProjectConfigStore.FileName} の「size」が正しくありません。0 より大きく 1 より小さい数を書いてください。",
        InvalidConfigValueException { Key: "selected" } => $"{ProjectConfigStore.FileName} の「selected = true」が多すぎます。選ぶタブは 1 つ、タブの中で選ぶペインは 1 つにしてください。",
        InvalidConfigValueException { Key: "panes" } => $"{ProjectConfigStore.FileName} の「panes」が正しくありません。[[tabs.panes]] の形で書いてください。",
        InvalidConfigValueException invalid => $"{ProjectConfigStore.FileName} の「{invalid.Key}」が正しくありません。",
        IOException or UnauthorizedAccessException => $"{ProjectConfigStore.FileName} を読み書きできません。アクセス権を確認してください。",
        _ => null,
    };

    private void ActivateProject(Project project)
    {
        if (project.WindowHandle != IntPtr.Zero)
        {
            _layout.Apply(project.WindowHandle);
            TerminalWindows.Activate(project.WindowHandle);
            FollowStrip(project.WindowHandle);
        }
    }

    /// <summary>
    /// 開いたばかりのウィンドウを、ほかのウィンドウと同じ位置とサイズにする。最初の 1 つなら、その位置を共有の位置にする。
    /// </summary>
    private void OnTerminalOpened(IntPtr hWnd)
    {
        _windowEvents.Watch(hWnd);
        _closeRequests.Watch(hWnd);
        _closeGuard?.Attach(hWnd);
        ApplyTaskbarSetting(hWnd);
        if (_layout.SharedRect is null)
        {
            SyncFrom(hWnd);
        }
        else
        {
            _layout.Apply(hWnd);
            FollowStrip(hWnd);
        }
        // 開いたウィンドウは、このウィンドウだと分かる前に前面になっていて、前面になった知らせを見逃している
        if (WindowOps.IsForeground(hWnd) && SelectedProject?.WindowHandle == hWnd)
        {
            KeepTogether(hWnd);
        }
        MarkStateChanged();
    }

    /// <summary>
    /// 選ばれているウィンドウの位置とサイズを（タブ列の余白を空けるよう直してから）共有の位置にし、ほかのウィンドウとタブ列をそろえる。
    /// </summary>
    private void SyncFrom(IntPtr source)
    {
        if (!TerminalWindows.Exists(source) || WindowOps.IsMinimized(source))
        {
            return;
        }
        _layout.SharedRect = _layout.Normalize(source);
        foreach (var project in Projects)
        {
            if (project.WindowHandle != IntPtr.Zero && project.WindowHandle != source)
            {
                _layout.Apply(project.WindowHandle);
            }
        }
        FollowStrip(source);
        MarkStateChanged();
    }

    /// <summary>タブ列をウィンドウの真上に置く。</summary>
    private void FollowStrip(IntPtr hWnd)
    {
        if (_ownHandle == IntPtr.Zero || WindowOps.IsMinimized(hWnd) || WindowOps.IsMinimized(_ownHandle))
        {
            return;
        }
        var strip = _layout.StripRect(hWnd);
        if (WindowOps.GetRect(_ownHandle) != strip)
        {
            WindowOps.Move(_ownHandle, strip);
        }
    }

    // 利用者がタスクバーなどから直接ウィンドウを前面に出したときも、そのタブを選んでタブ列を付ける
    private void OnTerminalForeground(IntPtr hWnd)
    {
        var project = Projects.FirstOrDefault(p => p.WindowHandle == hWnd);
        if (project is null || WindowOps.IsMinimized(hWnd))
        {
            return;
        }
        // まとめて最小化している途中で、次のウィンドウが前面になった。タブは変えず、これも最小化する
        if (_minimizeTimer.IsEnabled)
        {
            MinimizeNext(hWnd);
            return;
        }
        if (_allMinimized)
        {
            RestoreAll(hWnd);
            return;
        }
        SelectWithoutActivating(project);
        FollowStrip(hWnd);
        // ほかのアプリのウィンドウに隠れていたタブ列も、ターミナルと一緒に前に出す（タブ列はウィンドウと重ならない）
        KeepTogether(hWnd);
    }

    private void SelectWithoutActivating(Project project)
    {
        if (SelectedProject == project)
        {
            return;
        }
        _suppressActivation = true;
        try
        {
            ProjectTabs.SelectedItem = project;
        }
        finally
        {
            _suppressActivation = false;
        }
    }

    // 選ばれているウィンドウが最小化されたら、タブ列とほかのウィンドウも最小化する
    private void OnTerminalMinimizeStarted(IntPtr hWnd)
    {
        if (!_allMinimized && !_restoring && hWnd == SelectedProject?.WindowHandle)
        {
            BeginMinimizeAll();
        }
    }

    /// <summary>
    /// タブ列と全ウィンドウのまとめての最小化を始める。Windows が次のウィンドウを前面にしている途中で最小化すると、
    /// 最小化されたウィンドウが前面に残り、入力が見えないウィンドウに入ってしまう。そこで、前面になったウィンドウを順に
    /// 最小化して、前面の受け渡しは Windows に任せる（OnTerminalForeground・MinimizeNext）。前面にならなかったウィンドウは、
    /// タイマーで最小化する。
    /// </summary>
    private void BeginMinimizeAll()
    {
        _allMinimized = true;
        _minimizeTicks = 0;
        _minimizeTimer.Start();
    }

    private bool IsAllMinimized() =>
        WindowOps.IsMinimized(_ownHandle)
        && Projects.All(p => p.WindowHandle == IntPtr.Zero || WindowOps.IsMinimized(p.WindowHandle));

    private void MinimizeNext(IntPtr hWnd)
    {
        _minimizeTimer.Stop();
        _minimizeTimer.Start();
        WindowOps.Minimize(hWnd);
    }

    // まとめて最小化しているウィンドウのどれかが元に戻ったら、全部を元に戻す
    private void OnTerminalMinimizeEnded(IntPtr hWnd)
    {
        if (_allMinimized && !_minimizeTimer.IsEnabled && Projects.Any(p => p.WindowHandle == hWnd))
        {
            RestoreAll(hWnd);
        }
    }

    // タスクバーのボタンなどで、タブ列が最小化されたり元に戻ったりしたとき
    private void OnStripStateChanged()
    {
        if (_exiting)
        {
            return;
        }
        if (WindowState == WindowState.Minimized && !_allMinimized)
        {
            BeginMinimizeAll();
        }
        else if (WindowState != WindowState.Minimized && _allMinimized && !_minimizeTimer.IsEnabled)
        {
            RestoreAll(_ownHandle);
        }
    }

    /// <summary>
    /// タブ列と全プロジェクトのウィンドウのうち、まだ最小化されていないものを最小化する。
    /// </summary>
    private void MinimizeAll()
    {
        foreach (var project in Projects)
        {
            if (project.WindowHandle != IntPtr.Zero && !WindowOps.IsMinimized(project.WindowHandle))
            {
                WindowOps.Minimize(project.WindowHandle);
            }
        }
        if (!WindowOps.IsMinimized(_ownHandle))
        {
            WindowOps.Minimize(_ownHandle);
        }
    }

    /// <summary>
    /// まとめて最小化したタブ列と全ウィンドウを元に戻す。
    /// </summary>
    /// <param name="front">利用者が元に戻した方（ターミナルならそのタブを選ぶ）。入力先はこのウィンドウのままにする。</param>
    private void RestoreAll(IntPtr front)
    {
        _allMinimized = false;
        if (WindowOps.IsMinimized(_ownHandle))
        {
            WindowOps.Restore(_ownHandle, activate: false);
        }
        if (Projects.FirstOrDefault(p => p.WindowHandle == front) is { } frontProject)
        {
            SelectWithoutActivating(frontProject);
        }
        foreach (var project in Projects)
        {
            // front は Windows が元に戻している途中
            if (project.WindowHandle == IntPtr.Zero || project.WindowHandle == front)
            {
                continue;
            }
            if (WindowOps.IsMinimized(project.WindowHandle))
            {
                WindowOps.Restore(project.WindowHandle, activate: false);
            }
            _layout.Apply(project.WindowHandle);
        }
        if (SelectedProject is not { WindowHandle: var terminal } || terminal == IntPtr.Zero)
        {
            return;
        }
        // 元に戻したほかのウィンドウより前に出す
        WindowOps.LiftWithoutActivating(terminal);
        FollowStrip(terminal);
        KeepTogether(front);
    }

    private void OnTerminalMoveSizeStarted(IntPtr hWnd)
    {
        if (hWnd == SelectedProject?.WindowHandle)
        {
            _inMoveSize = true;
        }
    }

    private void OnTerminalMoveSizeEnded(IntPtr hWnd)
    {
        if (hWnd == SelectedProject?.WindowHandle)
        {
            _inMoveSize = false;
            _syncTimer.Stop();
            SyncFrom(hWnd);
        }
    }

    private void OnTerminalLocationChanged(IntPtr hWnd)
    {
        // ほかのウィンドウの変化は、wtbun がそろえたときのものなので見ない
        if (_stripAtDragStart is not null || hWnd != SelectedProject?.WindowHandle)
        {
            return;
        }
        // ドラッグ中もタブ列はついて行く
        FollowStrip(hWnd);
        if (_inMoveSize)
        {
            FollowOtherTerminals(hWnd);
        }
        else
        {
            // ドラッグでなければ、落ち着いてからそろえる
            _syncTimer.Stop();
            _syncTimer.Start();
        }
    }

    /// <summary>
    /// 選ばれているウィンドウを動かしている間、ほかのウィンドウも同じ位置へ動かす。大きさが変わっているとき（リサイズ中）は、
    /// ウィンドウごとに描き直しが起きて重いので動かさず、操作が終わってからそろえる。
    /// </summary>
    private void FollowOtherTerminals(IntPtr source)
    {
        var rect = WindowOps.GetRect(source);
        if (_layout.SharedRect is not { } shared || rect.Width != shared.Width || rect.Height != shared.Height)
        {
            return;
        }
        foreach (var project in Projects)
        {
            var hWnd = project.WindowHandle;
            if (hWnd != IntPtr.Zero && hWnd != source && !WindowOps.IsMinimized(hWnd))
            {
                WindowOps.Move(hWnd, rect);
            }
        }
    }

    private void Strip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void OnStripMoveStarted()
    {
        _stripAtDragStart = WindowOps.GetRect(_ownHandle);
        _sharedAtDragStart = _layout.SharedRect;
    }

    private void OnStripMoveEnded()
    {
        _stripAtDragStart = null;
        _sharedAtDragStart = null;
        if (SelectedProject is { WindowHandle: var hWnd } && hWnd != IntPtr.Zero)
        {
            SyncFrom(hWnd);
        }
    }

    // タブ列をドラッグしている間は、全ウィンドウを同じだけ動かす
    private void OnStripMovedByDrag()
    {
        if (_stripAtDragStart is not { } start || _sharedAtDragStart is not { } shared)
        {
            return;
        }
        var now = WindowOps.GetRect(_ownHandle);
        _layout.SharedRect = shared.Offset(now.Left - start.Left, now.Top - start.Top);
        foreach (var project in Projects)
        {
            if (project.WindowHandle != IntPtr.Zero)
            {
                _layout.Apply(project.WindowHandle);
            }
        }
    }

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    /// <summary>
    /// 確認してから、ウィンドウに閉じるよう求める。タブは、ウィンドウが実際に閉じたときに RemoveClosedProjects で外す
    /// （Windows Terminal の確認で取り消されることがあるため）。
    /// </summary>
    private void CloseProject(Project project)
    {
        if (project.WindowHandle == IntPtr.Zero || project.IsReopening)
        {
            // まだ開いている途中か、開き直している途中
            return;
        }
        // ウィンドウがもうなければ、確認せずにタブを外す。確認している間に閉じられることもあるので、確認の後でも確かめる
        if (TerminalWindows.Exists(project.WindowHandle)
            && (!ConfirmClosing($"「{project.Name}」を閉じますか？実行中のコマンドは終了します。") || !Projects.Contains(project)))
        {
            return;
        }
        if (!TerminalWindows.Exists(project.WindowHandle))
        {
            RemoveProject(project);
            return;
        }
        // 確認が出たときに見えるよう、前面に出してから閉じる
        TerminalWindows.Activate(project.WindowHandle);
        TerminalWindows.RequestClose(project.WindowHandle);
    }

    /// <summary>
    /// Windows Terminal の閉じる操作を受け取った。操作はウィンドウに届いていないので、代わりに閉じる。
    /// </summary>
    private void OnTerminalCloseRequested(IntPtr hWnd)
    {
        var project = Projects.FirstOrDefault(p => p.WindowHandle == hWnd);
        if (project is null)
        {
            // もう wtbun のタブがないウィンドウは、ボタンどおりに閉じる
            TerminalWindows.RequestClose(hWnd);
            return;
        }
        CloseProject(project);
    }

    private void RemoveClosedProjects()
    {
        if (_exiting)
        {
            return;
        }
        var closed = Projects
            .Where(p => p.WindowHandle != IntPtr.Zero && !p.IsReopening && !TerminalWindows.Exists(p.WindowHandle))
            .ToList();
        foreach (var project in closed)
        {
            RemoveProject(project);
        }
    }

    /// <summary>
    /// タブを外す。選ばれていたタブなら隣のタブを選ぶが、そのウィンドウを前面には出さない。
    /// </summary>
    private void RemoveProject(Project project)
    {
        var index = Projects.IndexOf(project);
        if (index < 0)
        {
            return;
        }
        var wasSelected = ProjectTabs.SelectedItem == project;

        _suppressActivation = true;
        try
        {
            Projects.RemoveAt(index);
            if (wasSelected && Projects.Count > 0)
            {
                ProjectTabs.SelectedIndex = Math.Min(index, Projects.Count - 1);
                if (SelectedProject is { WindowHandle: var hWnd } && hWnd != IntPtr.Zero)
                {
                    FollowStrip(hWnd);
                }
            }
        }
        finally
        {
            _suppressActivation = false;
        }
    }

    private void MoveProject(Project project, int offset)
    {
        var from = Projects.IndexOf(project);
        var to = from + offset;
        if (from >= 0 && to >= 0 && to < Projects.Count)
        {
            Projects.Move(from, to);
        }
    }

    private void ProjectTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 開き直している間は、最後に選ぶタブのウィンドウだけを前面に出す
        if (!_suppressActivation && !_restoring && ProjectTabs.SelectedItem is Project project)
        {
            ActivateProject(project);
        }
        MarkStateChanged();
    }

    // 選ばれているタブをもう一度クリックしたときは SelectionChanged が来ないので、ここでも前面に出す
    private void Tab_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: Project project })
        {
            ActivateProject(project);
        }
    }

    private void CloseProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Project project })
        {
            CloseProject(project);
        }
    }

    private void EditConfig_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Project project })
        {
            return;
        }

        ProjectConfigDocument document;
        try
        {
            document = ProjectConfigStore.LoadForEdit(project.Directory);
        }
        catch (Exception ex) when (ConfigErrorMessage(ex) is { } message)
        {
            Debug.WriteLine(ex);
            ShowError(message);
            return;
        }

        var dialog = new ProjectConfigWindow(project, document) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            project.Rename(dialog.SavedName);
            project.ReloadIcon();
        }
    }

    private async void ReopenProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Project project })
        {
            await ReopenProjectAsync(project);
        }
    }

    private void MoveLeft_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Project project })
        {
            MoveProject(project, -1);
        }
    }

    private void MoveRight_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Project project })
        {
            MoveProject(project, 1);
        }
    }

    // タブのドラッグで並べ替える
    private void Tab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragCandidate = (sender as FrameworkElement)?.DataContext as Project;
    }

    private void Tab_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null)
        {
            return;
        }
        var delta = e.GetPosition(null) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }
        var dragged = _dragCandidate;
        _dragCandidate = null;
        DragDrop.DoDragDrop((DependencyObject)sender, dragged, DragDropEffects.Move);
    }

    private void Tab_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Project)) is Project dragged
            && sender is ListBoxItem { DataContext: Project target }
            && dragged != target)
        {
            Projects.Move(Projects.IndexOf(dragged), Projects.IndexOf(target));
        }
    }

    /// <summary>Windows Terminal の閉じる確認も重なる設定のときに、確認に添える案内。</summary>
    private const string TerminalCloseWarningHint =
        "Windows Terminal の確認が不要なら、設定の「操作」で「複数のタブを閉じるときに警告する」をオフにしてください。";

    private void ShowError(string message) =>
        MessageBox.Show(this, message, AppProfile.ShortTitle, MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>
    /// ウィンドウを閉じる確認。Windows Terminal の確認も重なる設定のときは、それをオフにする案内を添える。
    /// </summary>
    private bool ConfirmClosing(string message)
    {
        // Windows Terminal の閉じるボタンや Alt+F4 は、確認やダイアログを出している間も押せる
        if (_confirmingClose)
        {
            return false;
        }
        if (TerminalSettings.WarnsWhenClosingTabs())
        {
            message += "\n\n" + TerminalCloseWarningHint;
        }
        // ダイアログを出しているときは、そのダイアログを持ち主にする（タブ列を持ち主にすると、確認を閉じたときにタブ列が操作できるようになる）
        var owner = OwnedWindows.Cast<Window>().FirstOrDefault(w => w.IsVisible) ?? this;
        // Windows Terminal の閉じるボタンや Alt+F4 から来たときは、前面にいるのは Windows Terminal なので、確認を前面に出せるようにしておく。
        // ここで前面になっても、タブ列の Activated でターミナルを前面に戻さない
        _takingForeground = true;
        var previous = WindowOps.TakeForeground(new WindowInteropHelper(owner).Handle);
        _takingForeground = false;
        _confirmingClose = true;
        try
        {
            var confirmed = MessageBox.Show(owner, message, AppProfile.ShortTitle, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
                == MessageBoxResult.Yes;
            // 閉じないなら、入力先を元のウィンドウに戻す
            if (!confirmed && TerminalWindows.IsTerminalWindow(previous))
            {
                TerminalWindows.Activate(previous);
            }
            return confirmed;
        }
        finally
        {
            _confirmingClose = false;
        }
    }
}
