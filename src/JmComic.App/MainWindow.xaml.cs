using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using JmComic.App.Common;
using JmComic.App.Controls;
using JmComic.App.Dialogs;
using JmComic.App.Services;
using JmComic.App.Themes;
using JmComic.App.ViewModels;
using JmComic.App.Views;
using JmComic.Core;
using JmComic.Core.Models;
using JmComic.Core.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace JmComic.App;

/// <summary>主窗口：顶部应用栏 + 左侧导航 + 中央内容 + 右侧下载面板。</summary>
public partial class MainWindow : Window
{
    private readonly SessionService _session;
    private readonly SourceManager _sourceManager;
    private readonly SearchView _searchView;
    private RankView? _rankView;
    private RankBrowseView? _rankBrowseView;
    private CategoryView? _categoryView;
    private CategoryBrowseView? _categoryBrowseView;
    private FavoriteView? _favoriteView;
    private LocalView? _localView;
    private UserControl? _localTabContent;
    private WeeklyView? _weeklyView;
    private readonly Dictionary<string, ChapterView> _chapterViews = new();
    private UserControl? _lastPage;
    private bool _panelVisible;

    public MainWindow()
    {
        InitializeComponent();

        _session = App.Services.GetRequiredService<SessionService>();
        _sourceManager = App.Services.GetRequiredService<SourceManager>();
        _searchView = new SearchView();
        _lastPage = _searchView;
        PageHost.Content = _searchView;

        Navigation.OpenComicHandler = OpenComic;
        Navigation.OpenRankHandler = OpenRank;
        Navigation.OpenReaderHandler = OpenReader;
        Navigation.OpenLocalDetailHandler = OpenLocalDetail;
        Navigation.CloseLocalDetailHandler = ShowLocalList;
        SearchPanelView.SearchChanged += (keyword, tags) => _localView?.ApplySearch(keyword, tags);
        Navigation.BackHandler = () =>
        {
            if (ReferenceEquals(PageHost.Content, _localTabContent) && _localTabContent is ReaderView)
            {
                // 阅读页返回：回到本地漫画列表（右侧恢复本地搜索工具）
                ShowLocalList();
                return;
            }
            HideRightPanel();
            PageHost.Content = _lastPage;
        };

        foreach (var source in _sourceManager.Sources)
        {
            SourceBox.Items.Add(new ComboBoxItem { Content = source.Info.DisplayName, Tag = source });
        }
        SourceBox.SelectedItem = SourceBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i => ReferenceEquals(i.Tag, _sourceManager.Current));
        _sourceManager.CurrentChanged += () => Dispatcher.Invoke(UpdateNavCapabilities);
        UpdateNavCapabilities();

        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionService.IsLoggedIn) or nameof(SessionService.Username))
            {
                UpdateLoginArea();
            }
            if (e.PropertyName == nameof(SessionService.IsLoggedIn) &&
                ReferenceEquals(PageHost.Content, _favoriteView))
            {
                // 启动时登录异步恢复完成：若当前停在收藏页（此前显示"请先登录"），立即刷新收藏列表
                _favoriteView?.Refresh();
            }
        };
        UpdateLoginArea();
        UpdateThemeIcon();
        UpdatePanelVisibility();

        Loaded += (_, _) => ToastService.ShowHandler = (message, kind) => Snackbars.Show(message, kind);
        Closed += (_, _) => ToastService.ShowHandler = null;
    }

    // ====================== 导航 ======================

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageHost is null)
        {
            return;
        }
        HideRightPanel();
        if (ReferenceEquals(sender, NavSearch))
        {
            _lastPage = _searchView;
            PageHost.Content = _searchView;
        }
        else if (ReferenceEquals(sender, NavRank))
        {
            if (_sourceManager.Current is IRankSource)
            {
                _rankBrowseView ??= new RankBrowseView();
                _lastPage = _rankBrowseView;
                PageHost.Content = _rankBrowseView;
                _rankBrowseView.OnShown();
            }
            else
            {
                _rankView ??= new RankView();
                _lastPage = _rankView;
                PageHost.Content = _rankView;
                _rankView.OnShown();
            }
        }
        else if (ReferenceEquals(sender, NavCategory))
        {
            _categoryView ??= new CategoryView();
            _lastPage = _categoryView;
            PageHost.Content = _categoryView;
            _categoryView.OnShown();
        }
        else if (ReferenceEquals(sender, NavFavorite))
        {
            _favoriteView ??= new FavoriteView();
            _lastPage = _favoriteView;
            PageHost.Content = _favoriteView;
            _favoriteView.OnShown();
        }
        else if (ReferenceEquals(sender, NavLocal))
        {
            _localView ??= new LocalView();
            _localTabContent ??= _localView;
            _lastPage = _localView;
            if (ReferenceEquals(_localTabContent, _localView))
            {
                ShowLocalList();
            }
            else
            {
                // 本地页签停留在阅读页：恢复右侧漫画详情面板
                DownloadPanelView.Visibility = Visibility.Collapsed;
                SearchPanelView.Visibility = Visibility.Collapsed;
                LocalDetailPanelView.Visibility = Visibility.Collapsed;
                DetailPanelView.Visibility = Visibility.Visible;
                _panelVisible = true;
                UpdatePanelVisibility();
                PageHost.Content = _localTabContent;
            }
        }
        else if (ReferenceEquals(sender, NavWeekly))
        {
            _weeklyView ??= new WeeklyView();
            _lastPage = _weeklyView;
            PageHost.Content = _weeklyView;
            _weeklyView.OnShown();
        }
    }

    private void OpenComic(string sourceId, string comicId)
    {
        HideRightPanel();
        var source = _sourceManager.Get(sourceId);
        var key = $"{source.Info.Id}:{comicId}";
        // 复用已打开的详情页，保持章节列表/滚动位置/选择状态
        if (!_chapterViews.TryGetValue(key, out var view))
        {
            if (_chapterViews.Count >= 30)
            {
                _chapterViews.Clear();
            }
            view = new ChapterView(source, comicId);
            _chapterViews[key] = view;
        }
        PageHost.Content = view;
    }

    private void OpenRank(RankPeriod period)
    {
        HideRightPanel();
        _rankView ??= new RankView();
        _lastPage = _rankView;
        PageHost.Content = _rankView;
        _rankView.OnShown(period);
    }

    private void OpenReader(LocalComic comic)
    {
        DownloadPanelView.Visibility = Visibility.Collapsed;
        SearchPanelView.Visibility = Visibility.Collapsed;
        LocalDetailPanelView.Visibility = Visibility.Collapsed;
        DetailPanelView.Visibility = Visibility.Visible;
        DetailPanelView.Show(comic);
        _panelVisible = true;
        UpdatePanelVisibility();
        _localTabContent = new ReaderView(comic);
        PageHost.Content = _localTabContent;
    }

    /// <summary>本地列表点击卡片：右侧切换到本地漫画详情面板（检查更新/更新下载）。</summary>
    private void OpenLocalDetail(LocalComic comic)
    {
        DownloadPanelView.Visibility = Visibility.Collapsed;
        SearchPanelView.Visibility = Visibility.Collapsed;
        DetailPanelView.Visibility = Visibility.Collapsed;
        LocalDetailPanelView.Visibility = Visibility.Visible;
        LocalDetailPanelView.Show(comic);
        _panelVisible = true;
        UpdatePanelVisibility();
    }
    // ====================== 内容源切换 ======================

    private void SourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceBox.SelectedItem is ComboBoxItem { Tag: IComicSource source })
        {
            _sourceManager.Current = source;
        }
    }

    /// <summary>按当前源能力显隐导航项；切源后把不能由新源服务的页面重置回搜索页。</summary>
    private void UpdateNavCapabilities()
    {
        var info = _sourceManager.Current.Info;
        NavRank.Visibility = info.SupportsRank ? Visibility.Visible : Visibility.Collapsed;
        NavWeekly.Visibility = info.SupportsWeekly ? Visibility.Visible : Visibility.Collapsed;
        NavCategory.Visibility = info.SupportsCategories ? Visibility.Visible : Visibility.Collapsed;
        NavFavorite.Visibility = info.SupportsFavorites ? Visibility.Visible : Visibility.Collapsed;

        var onUnsupportedPage = (NavWeekly.IsChecked == true && !info.SupportsWeekly)
                               || (NavFavorite.IsChecked == true && !info.SupportsFavorites)
                               || (NavRank.IsChecked == true && !info.SupportsRank);
        if (onUnsupportedPage)
        {
            NavSearch.IsChecked = true;
            _lastPage = _searchView;
            PageHost.Content = _searchView;
            return;
        }

        // 排行页按源切换：IRankSource 用通用排行浏览，禁漫保留原排行页
        if (NavRank.IsChecked == true)
        {
            if (_sourceManager.Current is IRankSource && !ReferenceEquals(PageHost.Content, _rankBrowseView))
            {
                _rankBrowseView ??= new RankBrowseView();
                _lastPage = _rankBrowseView;
                PageHost.Content = _rankBrowseView;
                _rankBrowseView.OnShown();
            }
            else if (_sourceManager.Current is not IRankSource && !ReferenceEquals(PageHost.Content, _rankView))
            {
                _rankView ??= new RankView();
                _lastPage = _rankView;
                PageHost.Content = _rankView;
                _rankView.OnShown();
            }
        }

        // 分类页按源切换：支持 ICategorySource 的源用通用分类浏览，禁漫保留主题页
        if (NavCategory.IsChecked == true)
        {
            if (_sourceManager.Current is ICategorySource && !ReferenceEquals(PageHost.Content, _categoryBrowseView))
            {
                _categoryBrowseView ??= new CategoryBrowseView();
                _lastPage = _categoryBrowseView;
                PageHost.Content = _categoryBrowseView;
                _categoryBrowseView.OnShown();
            }
            else if (_sourceManager.Current is not ICategorySource && !ReferenceEquals(PageHost.Content, _categoryView))
            {
                _categoryView ??= new CategoryView();
                _lastPage = _categoryView;
                PageHost.Content = _categoryView;
                _categoryView.OnShown();
            }
        }
    }

    // ====================== 顶栏操作 ======================

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.Toggle();
        UpdateThemeIcon();
    }

    private void UpdateThemeIcon()
    {
        ThemeIcon.Text = ThemeManager.IsDark ? Icons.Moon : Icons.Sun;
    }

    private void OpenConfigDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AppDataDir);
            Process.Start("explorer.exe", AppPaths.AppDataDir);
        }
        catch (Exception ex)
        {
            ToastService.ShowError(ex, "打开配置目录失败：");
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog { Owner = this };
        dialog.ShowDialog();
    }

    private void PanelToggle_Click(object sender, RoutedEventArgs e)
    {
        _panelVisible = !_panelVisible;
        UpdatePanelVisibility();
    }

    private void UpdatePanelVisibility()
    {
        if (_panelVisible)
        {
            // 显示侧栏：恢复可拖动范围与宽度（MinWidth 会顶住 Width=0，收起时必须先放开）
            PanelColumn.MinWidth = 300;
            PanelColumn.MaxWidth = 420;
            PanelColumn.Width = new GridLength(344);
        }
        else
        {
            PanelColumn.MinWidth = 0;
            PanelColumn.MaxWidth = 420;
            PanelColumn.Width = new GridLength(0);
        }
        PanelSplitter.Visibility = _panelVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>离开阅读页（进入列表/详情等页面）时：隐藏右侧面板，恢复下载队列内容。</summary>
    private void HideRightPanel()
    {
        DetailPanelView.Visibility = Visibility.Collapsed;
        SearchPanelView.Visibility = Visibility.Collapsed;
        LocalDetailPanelView.Visibility = Visibility.Collapsed;
        DownloadPanelView.Visibility = Visibility.Visible;
        _panelVisible = false;
        UpdatePanelVisibility();
    }

    /// <summary>显示本地漫画列表，并在右侧展示本地搜索工具。</summary>
    private void ShowLocalList()
    {
        _localView ??= new LocalView();
        _localTabContent = _localView;
        PageHost.Content = _localView;
        _localView.SetSearchPanel(SearchPanelView);
        SearchPanelView.Visibility = Visibility.Visible;
        DownloadPanelView.Visibility = Visibility.Collapsed;
        DetailPanelView.Visibility = Visibility.Collapsed;
        LocalDetailPanelView.Visibility = Visibility.Collapsed;
        _panelVisible = true;
        UpdatePanelVisibility();
        _localView.OnShown();
    }

    // ====================== 窗口控制 ======================

    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct WinPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int CbSize;
        public WinRect RcMonitor;
        public WinRect RcWork;
        public uint DwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public WinPoint PtReserved;
        public WinPoint PtMaxSize;
        public WinPoint PtMaxPosition;
        public WinPoint PtMinTrackSize;
        public WinPoint PtMaxTrackSize;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // 无边框窗口：拦截 WM_GETMINMAXINFO，让最大化恰好填满当前显示器工作区（不遮挡任务栏）
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WindowProc);
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfo)
        {
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            var info = new MonitorInfo { CbSize = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                mmi.PtMaxPosition.X = info.RcWork.Left;
                mmi.PtMaxPosition.Y = info.RcWork.Top;
                mmi.PtMaxSize.X = info.RcWork.Right - info.RcWork.Left;
                mmi.PtMaxSize.Y = info.RcWork.Bottom - info.RcWork.Top;
                Marshal.StructureToPtr(mmi, lParam, true);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => SystemCommands.MinimizeWindow(this);

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => SystemCommands.CloseWindow(this);

    private void Window_StateChanged(object? sender, EventArgs e) => UpdateMaximizeIcon();

    private void UpdateMaximizeIcon()
    {
        if (MaximizeIcon is null)
        {
            return;
        }
        var maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Text = maximized ? Icons.Restore : Icons.Maximize;
        MaximizeButton.ToolTip = maximized ? "还原" : "最大化";
    }
    // ====================== 登录区 ======================

    private void UpdateLoginArea()
    {
        LoginArea.Child = null;
        if (_session.IsLoggedIn)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var username = new TextBlock
            {
                Text = _session.Username ?? "已登录",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            username.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            var logout = new Button
            {
                Style = (Style)FindResource("IconButtonStyle"),
                ToolTip = "退出登录",
                Margin = new Thickness(8, 0, 0, 0),
                Content = new TextBlock { Text = Icons.SignOut, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14 },
            };
            logout.Click += Logout_Click;
            panel.Children.Add(username);
            panel.Children.Add(logout);
            LoginArea.Child = panel;
        }
        else
        {
            var login = new Button
            {
                Style = (Style)FindResource("PrimaryButtonStyle"),
                Content = "登录",
            };
            login.Click += Login_Click;
            LoginArea.Child = login;
        }
    }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Dialogs.LoginDialog { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            UpdateLoginArea();
            ToastService.Show($"欢迎回来，{_session.Username}", ToastKind.Success);
            _favoriteView?.Refresh();
        }
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        _session.Logout();
        UpdateLoginArea();
        ToastService.Show("已退出登录", ToastKind.Info);
        _favoriteView?.Refresh();
    }
}












