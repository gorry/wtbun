using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Tomlyn;
using Wtbun.Config;
using Wtbun.Projects;
using Wtbun.Terminal;
using Wtbun.Localization;

namespace Wtbun;

/// <summary>
/// プロジェクトの構成ファイルを、手で書かずに編集するダイアログ。
/// </summary>
public partial class ProjectConfigWindow : Window
{
    private readonly string _projectDir;
    private readonly ProjectConfigDocument _document;

    /// <summary>一覧に出すタブとペイン。タブのすぐ後ろに、そのタブのペインを並べる（先頭はいつもタブ）。</summary>
    private readonly ObservableCollection<EditItem> _items;

    /// <summary>インポートした画像（正方形に切り抜いたもの）。保存するまでファイルには書かない。</summary>
    private BitmapSource? _importedIcon;

    /// <summary>保存したときの、タブに出す名前。空なら null。</summary>
    public string? SavedName { get; private set; }

    public ProjectConfigWindow(Project project, ProjectConfigDocument document)
    {
        InitializeComponent();
        _projectDir = project.Directory;
        _document = document;
        Title = Loc.Format("Config_Title", project.Name);

        NameBox.Text = document.Config.Name ?? "";
        _items = new ObservableCollection<EditItem>(document.Config.Tabs.SelectMany(tab =>
            new[] { EditItem.From(tab) }.Concat(tab.Panes.Select(EditItem.From))));
        TabList.ItemsSource = _items;
        foreach (var item in _items)
        {
            item.PropertyChanged += Item_PropertyChanged;
        }
        _items.CollectionChanged += (_, e) =>
        {
            // 並べ替え（Move）でも NewItems が来るので、足したときだけ登録する（2 回登録しないように）
            if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                return;
            }
            foreach (var item in e.NewItems?.OfType<EditItem>() ?? [])
            {
                item.PropertyChanged += Item_PropertyChanged;
            }
        };
        TabList.SelectedIndex = 0;
        ProfileBox.ItemsSource = TerminalSettings.LoadProfileNames();
        ColorSchemeBox.ItemsSource = TerminalSettings.LoadColorSchemeNames();
        IconPreview.Source = ProjectIcon.Load(_projectDir);
        UpdateIconHint();

        // 高さは中身に合わせるが、画面の作業領域を超えないようにする
        MaxHeight = SystemParameters.WorkArea.Height;
        Loaded += (_, _) => DialogPlacement.PlaceBelowOwner(this);
    }

    private EditItem? SelectedItem => TabList.SelectedItem as EditItem;

    private int TabCount => _items.Count(i => !i.IsPane);

    private void TabList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateButtons();

    private void UpdateButtons()
    {
        var index = TabList.SelectedIndex;
        var item = SelectedItem;
        AddPaneButton.IsEnabled = item is not null;
        DuplicateButton.IsEnabled = item is not null;
        // タブが 1 つもないとウィンドウを開けないので、最後の 1 つのタブは消せない
        RemoveTabButton.IsEnabled = item is { IsPane: true } || (item is not null && TabCount > 1);
        if (item is { IsPane: true })
        {
            // ペインは、同じタブの中でだけ動かす
            MoveUpButton.IsEnabled = _items[index - 1].IsPane;
            MoveDownButton.IsEnabled = index + 1 < _items.Count && _items[index + 1].IsPane;
        }
        else
        {
            MoveUpButton.IsEnabled = index > 0;
            MoveDownButton.IsEnabled = index >= 0 && BlockEnd(index) < _items.Count;
        }
    }

    /// <summary>index のタブと、そのペインの範囲の終わり（次のタブの位置か、一覧の終わり）。</summary>
    private int BlockEnd(int index)
    {
        var end = index + 1;
        while (end < _items.Count && _items[end].IsPane)
        {
            end++;
        }
        return end;
    }

    /// <summary>index の行が属するタブの位置。</summary>
    private int OwnerTab(int index)
    {
        while (index > 0 && _items[index].IsPane)
        {
            index--;
        }
        return index;
    }

    /// <summary>
    /// 「開いたときに選択」は、タブなら全部のタブで 1 つ、ペインなら同じタブのペインで 1 つだけにする（ラジオボタンと同じ）。
    /// </summary>
    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EditItem.IsSelectedOnOpen) || sender is not EditItem { IsSelectedOnOpen: true } item)
        {
            return;
        }
        var index = _items.IndexOf(item);
        IEnumerable<EditItem> others = item.IsPane
            ? _items.Skip(OwnerTab(index) + 1).Take(BlockEnd(OwnerTab(index)) - OwnerTab(index) - 1)
            : _items.Where(i => !i.IsPane);
        foreach (var other in others.Where(o => o != item))
        {
            other.IsSelectedOnOpen = false;
        }
    }

    private void AddTab_Click(object sender, RoutedEventArgs e)
    {
        var tab = new EditItem(isPane: false);
        _items.Add(tab);
        TabList.SelectedItem = tab;
        UpdateButtons();
    }

    /// <summary>選んでいるタブ（ペインならそのタブ）の、最後のペインの後ろに足す。</summary>
    private void AddPane_Click(object sender, RoutedEventArgs e)
    {
        var index = TabList.SelectedIndex;
        if (index < 0)
        {
            return;
        }
        var pane = new EditItem(isPane: true);
        _items.Insert(BlockEnd(OwnerTab(index)), pane);
        TabList.SelectedItem = pane;
        UpdateButtons();
    }

    /// <summary>
    /// 選んでいる行を複製する。ペインはすぐ後ろに、タブはそのペインも一緒に、そのタブ（とペイン）の後ろに入れる。
    /// 「開いたときに選択」は、タブ同士・同じタブのペイン同士で 1 つだけなので、複製した行（タブを複製したときはタブの行、
    /// ペインを複製したときはそのペイン）には付けない。タブを複製したときのペインは、新しいタブの中で元と同じにする。
    /// </summary>
    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        var index = TabList.SelectedIndex;
        if (SelectedItem is not { } item)
        {
            return;
        }
        var end = item.IsPane ? index + 1 : BlockEnd(index);
        var copies = _items.Skip(index).Take(end - index).Select(i => i.Clone()).ToList();
        copies[0].IsSelectedOnOpen = false;
        for (var i = 0; i < copies.Count; i++)
        {
            _items.Insert(end + i, copies[i]);
        }
        TabList.SelectedItem = copies[0];
        UpdateButtons();
    }

    /// <summary>ペインならそのペインだけ、タブならそのペインも一緒に削除する。</summary>
    private void RemoveTab_Click(object sender, RoutedEventArgs e)
    {
        var index = TabList.SelectedIndex;
        if (SelectedItem is not { } item || (!item.IsPane && TabCount <= 1))
        {
            return;
        }
        var end = item.IsPane ? index + 1 : BlockEnd(index);
        for (var i = end - 1; i >= index; i--)
        {
            _items.RemoveAt(i);
        }
        TabList.SelectedIndex = Math.Min(index, _items.Count - 1);
        UpdateButtons();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int offset)
    {
        var index = TabList.SelectedIndex;
        if (SelectedItem is not { } item)
        {
            return;
        }
        if (item.IsPane)
        {
            var to = index + offset;
            if (to >= 0 && to < _items.Count && _items[to].IsPane)
            {
                _items.Move(index, to);
            }
        }
        else
        {
            // タブはペインと一緒に、隣のタブ（とそのペイン）と入れ替える
            var end = BlockEnd(index);
            if (offset < 0 && index > 0)
            {
                MoveBlock(index, end, OwnerTab(index - 1));
            }
            else if (offset > 0 && end < _items.Count)
            {
                MoveBlock(end, BlockEnd(end), index);
            }
        }
        TabList.SelectedItem = item;
        UpdateButtons();
    }

    /// <summary>[start, end) の行を、to の位置へ移す（to は start より前）。</summary>
    private void MoveBlock(int start, int end, int to)
    {
        for (var i = 0; i < end - start; i++)
        {
            _items.Move(start + i, to + i);
        }
    }

    private void BrowseDir_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is not { } tab)
        {
            return;
        }
        var current = Path.GetFullPath(Path.Combine(_projectDir, tab.Dir?.Trim() ?? "."));
        var dialog = new OpenFolderDialog
        {
            Title = Loc.Get("Config_SelectStartFolder"),
            InitialDirectory = Directory.Exists(current) ? current : _projectDir,
        };
        if (dialog.ShowDialog(this) == true)
        {
            tab.Dir = ToConfigPath(dialog.FolderName);
        }
    }

    /// <summary>プロジェクトの中のフォルダなら相対パスにする（構成ファイルをほかの場所でも使えるように）。</summary>
    private string ToConfigPath(string folder)
    {
        var relative = Path.GetRelativePath(_projectDir, folder);
        var isOutside = relative == ".." || relative.StartsWith(@"..\", StringComparison.Ordinal) || Path.IsPathRooted(relative);
        return isOutside ? folder : relative;
    }

    private void ImportIcon_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.Get("Config_SelectIconImage"),
            Filter = $"{Loc.Get("Config_ImageFiles")}|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.tif;*.tiff|{Loc.Get("Config_AllFiles")}|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        if (ProjectIcon.TryLoadImage(dialog.FileName) is not { } image)
        {
            ShowMessage(Loc.Get("Config_CannotLoadImage"));
            return;
        }
        _importedIcon = ProjectIcon.CropToSquare(image);
        IconPreview.Source = _importedIcon;
        UpdateIconHint();
    }

    private void UpdateIconHint()
    {
        var hasIconFile = File.Exists(Path.Combine(_projectDir, ProjectIcon.IconFileName));
        IconHint.Text = _importedIcon is null
            ? Loc.Get("Config_IconHintCrop")
            : hasIconFile
                ? Loc.Format("Config_IconHintIco", ProjectIcon.IconFileName)
                : Loc.Get("Config_IconHintSave");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_items.FirstOrDefault(t => !TabColor.IsValid(t.TabColor)) is { } invalidColor)
        {
            TabList.SelectedItem = invalidColor;
            ShowMessage(Loc.Format("Config_InvalidColor", invalidColor.Name));
            return;
        }
        if (_items.FirstOrDefault(t => t.IsPane && !t.TryGetSize(out _)) is { } invalidSize)
        {
            TabList.SelectedItem = invalidSize;
            ShowMessage(Loc.Format("Config_InvalidSize", invalidSize.Name));
            return;
        }
        // 構成ファイルの保存に失敗してもう一度保存するときに、同じ画像を書き直すだけで済むよう、アイコンを先に保存する
        if (_importedIcon is not null && !TrySaveIcon(_importedIcon))
        {
            return;
        }

        var edited = new ProjectConfig
        {
            Name = NameBox.Text,
            Tabs = ToTabs(),
        };

        // 変えていなければ、ファイルには触らない
        if (!ProjectConfigWriter.IsSame(edited, _document.Config) && !TrySave(edited))
        {
            return;
        }
        SavedName = NameBox.Text.Trim() is { Length: > 0 } name ? name : null;
        DialogResult = true;
    }

    /// <summary>一覧の並びから、タブとそのペインの構成を作る。</summary>
    private List<TabConfig> ToTabs()
    {
        var tabs = new List<TabConfig>();
        foreach (var item in _items)
        {
            if (item.IsPane)
            {
                tabs[^1].Panes.Add(item.ToPane());
            }
            else
            {
                tabs.Add(item.ToTab());
            }
        }
        return tabs;
    }

    private bool TrySave(ProjectConfig edited)
    {
        var fileName = ProjectConfigStore.FileName;
        try
        {
            // このダイアログを開いている間に、ファイルが手で書き換えられていたら上書きしない
            if (ProjectConfigStore.ReadTextOrNull(_document.Path) != _document.Text)
            {
                ShowMessage(Loc.Format("Config_ChangedElsewhere", fileName));
                return false;
            }

            var edit = ProjectConfigWriter.Apply(_document.Text ?? ProjectConfigStore.DefaultText, edited);
            if (edit.LosesComments && !ConfirmLosingComments())
            {
                return false;
            }
            ProjectConfigStore.WriteText(_document.Path, edit.Text);
            return true;
        }
        catch (ProjectConfigNotEditableException ex)
        {
            Debug.WriteLine(ex);
            ShowMessage(Loc.Format("Config_NotEditable", fileName));
        }
        catch (Exception ex) when (ex is TomlException or UnknownConfigKeyException or InvalidConfigValueException)
        {
            Debug.WriteLine(ex);
            ShowMessage(Loc.Format("Config_ChangedElsewhere", fileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(ex);
            ShowMessage(Loc.Format("Config_CannotSave", fileName));
        }
        return false;
    }

    private bool TrySaveIcon(BitmapSource icon)
    {
        try
        {
            ProjectIcon.SaveImage(_projectDir, icon);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(ex);
            ShowMessage(Loc.Format("Config_CannotSave", ProjectIcon.ImageFileName));
            return false;
        }
    }

    private bool ConfirmLosingComments() =>
        MessageBox.Show(
            this,
            Loc.Format("Config_ConfirmLoseComments", ProjectConfigStore.FileName),
            AppProfile.ShortTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    private void ShowMessage(string message) =>
        MessageBox.Show(this, message, AppProfile.ShortTitle, MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>ダイアログの一覧の 1 行ぶん（タブかペイン）。</summary>
    private sealed class EditItem(bool isPane) : INotifyPropertyChanged
    {
        private string? _title;
        private string? _dir;
        private string? _tabColor;

        // ファイルに append_command_line・duplicate・selected = false と書いてあったときは、チェックを変えなければそのまま残す
        private bool _appendCommandLineWasFalse;
        private bool _duplicateWasFalse;
        private bool _selectedWasFalse;
        private bool _isSelectedOnOpen;

        // サイズの欄を変えなければ、ファイルの値をそのまま残す（百分率との行き来で値が変わらないように）
        private double? _initialSize;
        private string _initialSizeText = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsPane { get; } = isPane;

        public string? Title
        {
            get => _title;
            set
            {
                _title = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Header)));
            }
        }

        public string? Dir
        {
            get => _dir;
            set
            {
                _dir = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Dir)));
            }
        }

        public string? Command { get; set; }

        public string? Profile { get; set; }

        public bool AppendCommandLine { get; set; }

        public string? TabColor
        {
            get => _tabColor;
            set
            {
                _tabColor = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TabColor)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TabColorBrush)));
            }
        }

        /// <summary>タブの色の見本。空か正しくなければ透明。</summary>
        public Brush TabColorBrush =>
            Config.TabColor.Normalize(TabColor) is { } color && ColorConverter.ConvertFromString(color) is Color c
                ? new SolidColorBrush(c)
                : Brushes.Transparent;

        public string? ColorScheme { get; set; }

        /// <summary>0：プロファイルの設定に従う、1：許可する、2：許可しない。</summary>
        public int SuppressApplicationTitleIndex { get; set; }

        /// <summary>0：既定、1：引き継ぐ、2：新しく読み込む。</summary>
        public int InheritEnvironmentIndex { get; set; }

        /// <summary>ペインの分割の向き。0：自動、1：右、2：下。</summary>
        public int SplitIndex { get; set; }

        /// <summary>ペインのサイズ（百分率）。空なら既定（半分）。</summary>
        public string SizeText { get; set; } = "";

        public bool Duplicate { get; set; }

        /// <summary>開いたときに、このタブ（ペインなら、タブの中でこのペイン）を選ぶ。</summary>
        public bool IsSelectedOnOpen
        {
            get => _isSelectedOnOpen;
            set
            {
                if (_isSelectedOnOpen == value)
                {
                    return;
                }
                _isSelectedOnOpen = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedOnOpen)));
            }
        }

        public string SelectedLabel => Loc.Get(IsPane ? "Config_SelectPaneOnOpen" : "Config_SelectTabOnOpen");

        public string SelectedHint => IsPane
            ? Loc.Get("Config_SelectPaneHint")
            : Loc.Get("Config_SelectTabHint");

        /// <summary>メッセージに出す名前。</summary>
        public string Name => string.IsNullOrWhiteSpace(Title) ? Loc.Get(IsPane ? "Config_UnnamedPane" : "Config_Untitled") : Title;

        /// <summary>一覧に出す文字。ペインは字下げして、どのタブのものか分かるようにする。</summary>
        public string Header => IsPane ? Loc.Format("Config_PaneHeader", Name) : Name;

        // 画面の読み上げなどに、一覧に出ているものと同じ文字を渡す
        public override string ToString() => Header;

        /// <summary>サイズの欄を割合（0 より大きく 1 より小さい）にする。空なら null。正しくなければ false。</summary>
        public bool TryGetSize(out double? size)
        {
            size = null;
            var text = SizeText.Trim().TrimEnd('%', '％').Trim();
            if (text.Length == 0)
            {
                return true;
            }
            if (SizeText == _initialSizeText)
            {
                size = _initialSize;
                return true;
            }
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var percent) || percent is not (>= 1 and <= 99))
            {
                return false;
            }
            size = Math.Round(percent / 100, 4);
            return true;
        }

        /// <summary>同じ内容の行を作る（ファイルにどう書いてあったかの記録も含めて）。</summary>
        public EditItem Clone() => new(IsPane)
        {
            Title = Title,
            Dir = Dir,
            Command = Command,
            Profile = Profile,
            AppendCommandLine = AppendCommandLine,
            TabColor = TabColor,
            ColorScheme = ColorScheme,
            SuppressApplicationTitleIndex = SuppressApplicationTitleIndex,
            InheritEnvironmentIndex = InheritEnvironmentIndex,
            SplitIndex = SplitIndex,
            SizeText = SizeText,
            Duplicate = Duplicate,
            IsSelectedOnOpen = IsSelectedOnOpen,
            _appendCommandLineWasFalse = _appendCommandLineWasFalse,
            _duplicateWasFalse = _duplicateWasFalse,
            _selectedWasFalse = _selectedWasFalse,
            _initialSize = _initialSize,
            _initialSizeText = _initialSizeText,
        };

        public static EditItem From(TerminalConfig terminal)
        {
            var pane = terminal as PaneConfig;
            var item = new EditItem(pane is not null)
            {
                Title = terminal.Title,
                Dir = terminal.Dir,
                Command = terminal.Command,
                Profile = terminal.Profile,
                AppendCommandLine = terminal.AppendCommandLine == true,
                _appendCommandLineWasFalse = terminal.AppendCommandLine == false,
                TabColor = terminal.TabColor,
                ColorScheme = terminal.ColorScheme,
                SuppressApplicationTitleIndex = terminal.SuppressApplicationTitle switch { null => 0, false => 1, true => 2 },
                InheritEnvironmentIndex = terminal.InheritEnvironment switch { null => 0, true => 1, false => 2 },
                IsSelectedOnOpen = terminal.Selected == true,
                _selectedWasFalse = terminal.Selected == false,
            };
            if (pane is not null)
            {
                item.SplitIndex = pane.Split?.Trim() switch { PaneConfig.SplitRight => 1, PaneConfig.SplitDown => 2, _ => 0 };
                item.Duplicate = pane.Duplicate == true;
                item._duplicateWasFalse = pane.Duplicate == false;
                item._initialSize = pane.Size;
                item._initialSizeText = pane.Size is { } size
                    ? Math.Round(size * 100, 2).ToString(CultureInfo.CurrentCulture)
                    : "";
                item.SizeText = item._initialSizeText;
            }
            return item;
        }

        public TabConfig ToTab() => Fill(new TabConfig());

        public PaneConfig ToPane()
        {
            var pane = Fill(new PaneConfig());
            pane.Split = SplitIndex switch { 1 => PaneConfig.SplitRight, 2 => PaneConfig.SplitDown, _ => null };
            pane.Size = TryGetSize(out var size) ? size : null;
            pane.Duplicate = Duplicate ? true : _duplicateWasFalse ? false : null;
            return pane;
        }

        private T Fill<T>(T terminal) where T : TerminalConfig
        {
            terminal.Title = Title;
            terminal.Dir = Dir;
            terminal.Command = Command;
            terminal.Profile = Profile;
            terminal.AppendCommandLine = AppendCommandLine ? true : _appendCommandLineWasFalse ? false : null;
            terminal.TabColor = TabColor;
            terminal.ColorScheme = ColorScheme;
            terminal.SuppressApplicationTitle = SuppressApplicationTitleIndex switch { 1 => false, 2 => true, _ => null };
            terminal.InheritEnvironment = InheritEnvironmentIndex switch { 1 => true, 2 => false, _ => null };
            terminal.Selected = IsSelectedOnOpen ? true : _selectedWasFalse ? false : null;
            return terminal;
        }
    }
}
