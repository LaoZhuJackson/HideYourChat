using System.ComponentModel;
using System.Windows;
using HideYourChat.App.Core;
using HideYourChat.App.Pages;
using HideYourChat.App.Adapters.QQ;
using Serilog;
using WinForms = System.Windows.Forms;
using Application = System.Windows.Application;
using Wpf.Ui.Controls;

namespace HideYourChat.App;

public partial class MainWindow : FluentWindow
{
    private readonly ChatRuntimeService _runtime = new();
    private readonly SettingsService _settingsService = new();
    private AppSettings _settings = new();

    // 页面
    private HomePage _homePage = null!;
    private SettingsPage _settingsPage = null!;

    // 托盘
    private WinForms.NotifyIcon? _trayIcon;
    private WinForms.ToolStripMenuItem? _monitorMenuItem;
    private WinForms.ToolStripMenuItem? _overlayMenuItem;
    private WinForms.ToolStripMenuItem? _mainWindowMenuItem;
    private bool _isReallyClosing;

    public MainWindow()
    {
        InitializeComponent();

        _settings = _settingsService.Load();
        _runtime.SwitchAdapter(_settings.SelectedApp);

        // 创建页面，注入依赖
        _homePage = new HomePage(_runtime, _settings);
        _settingsPage = new SettingsPage(_settings, _settingsService);

        // 订阅页面事件
        _homePage.StartRequested += OnStartRequested;
        _homePage.StopRequested += OnStopRequested;
        _homePage.OverlayShown += () => { if (_overlayMenuItem is not null) _overlayMenuItem.Checked = true; };
        _homePage.OverlayHidden += () => { if (_overlayMenuItem is not null) _overlayMenuItem.Checked = false; };

        _settingsPage.StatusChanged += text =>
            Dispatcher.Invoke(() => StatusText.Text = $"状态：{text}");
        _runtime.StatusChanged += (_, text) =>
            Dispatcher.Invoke(() => StatusText.Text = $"状态：{text}");

        // 导航事件
        foreach (Wpf.Ui.Controls.NavigationViewItem item in NavView.MenuItems)
            item.Click += NavItem_Click;
        foreach (Wpf.Ui.Controls.NavigationViewItem item in NavView.FooterMenuItems)
            item.Click += NavItem_Click;

        // 默认显示主页
        PageContent.Content = _homePage;
        if (NavView.MenuItems[0] is Wpf.Ui.Controls.NavigationViewItem firstItem)
            firstItem.IsActive = true;

        // 回填配置
        _homePage.ApplySettingsToUi();
        _settingsPage.ApplySettingsToUi();
        UpdateThemeButtonIcon(_settings.IsDarkTheme);

        InitTrayIcon();
    }

    // ═══════════════ 导航 ═══════════════

    private void NavItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.NavigationViewItem item)
        {
            if (item.Tag is "theme")
            {
                ToggleTheme();
                return;
            }
            PageContent.Content = item.Tag switch
            {
                "settings" => _settingsPage,
                _ => _homePage,
            };

            foreach (Wpf.Ui.Controls.NavigationViewItem i in NavView.MenuItems)
                i.IsActive = i == item;
        }
    }

    private void ToggleTheme()
    {
        var current = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
        var target = current == Wpf.Ui.Appearance.ApplicationTheme.Dark
            ? Wpf.Ui.Appearance.ApplicationTheme.Light
            : Wpf.Ui.Appearance.ApplicationTheme.Dark;
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(target);

        bool dark = target == Wpf.Ui.Appearance.ApplicationTheme.Dark;
        _runtime.ApplyTheme(dark);
        _settings.IsDarkTheme = dark;

        ThemeNavItem.Icon = new SymbolIcon(
            dark ? SymbolRegular.WeatherMoon24
                 : SymbolRegular.WeatherSunny24);
    }

    // ═══════════════ 启动 / 停止 ═══════════════
    private void OnStartRequested()
    {
        SaveCurrentSettings();
        var opacity = _homePage.BackgroundOpacity;
        var textOpacity = _homePage.TextOpacity;
        if (!_runtime.Start(_settings, opacity, textOpacity))
            return;
        _homePage.SetRunningMode(true);
        SyncTrayMonitorState();
        _homePage.RefreshQQButton();
    }

    private async void OnStopRequested()
    {
        await _runtime.StopAsync();
        _homePage.SetRunningMode(false);
        SyncTrayMonitorState();
        _homePage.RefreshQQButton();
    }

    private void SyncTrayMonitorState()
    {
        if (_monitorMenuItem is not null)
            _monitorMenuItem.Text = _runtime.IsRunning ? "停止监听" : "开始监听";
    }

    // ═══════════════ 保存配置 ═══════════════

    public void SaveCurrentSettings()
    {
        _homePage.SaveToSettings();
        _settingsPage.SaveToSettings();
        _settings.BackgroundOpacity = _homePage.BackgroundOpacity;
        _settings.TextOpacity = _homePage.TextOpacity;

        var (left, top, width, height) = _runtime.GetOverlayBounds();
        if (!double.IsNaN(left))
        {
            _settings.OverlayLeft = left;
            _settings.OverlayTop = top;
            _settings.OverlayWidth = width;
            _settings.OverlayHeight = height;
        }

        _settings.IsDarkTheme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme()
            == Wpf.Ui.Appearance.ApplicationTheme.Dark;

        _settingsService.Save(_settings);
    }

    // ═══════════════ 托盘 ═══════════════

    private void InitTrayIcon()
    {
        var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);

        var menu = new WinForms.ContextMenuStrip();

        // 监听开关
        _monitorMenuItem = new WinForms.ToolStripMenuItem("开始监听");
        _monitorMenuItem.Click += (_, _) =>
        {
            if (_runtime.IsRunning) OnStopRequested();
            else OnStartRequested();
        };
        menu.Items.Add(_monitorMenuItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        // 悬浮窗 —— 勾选 = 可见
        _overlayMenuItem = new WinForms.ToolStripMenuItem("悬浮窗可见")
        {
            CheckOnClick = true,
            Checked = false,
        };
        _overlayMenuItem.CheckedChanged += (_, _) =>
        {
            if (_overlayMenuItem.Checked) _runtime.ShowOverlay();
            else _runtime.HideOverlay();
        };
        menu.Items.Add(_overlayMenuItem);

        // 主窗口 —— 勾选 = 可见
        _mainWindowMenuItem = new WinForms.ToolStripMenuItem("主窗口可见")
        {
            CheckOnClick = true,
            Checked = true,
        };
        _mainWindowMenuItem.CheckedChanged += (_, _) =>
        {
            if (_mainWindowMenuItem.Checked)
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            }
            else Hide();
        };
        menu.Items.Add(_mainWindowMenuItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("退出 HideYourChat", null, (_, _) =>
        {
            _isReallyClosing = true;
            _trayIcon?.Dispose();
            _trayIcon = null;
            Application.Current.Shutdown();
        });

        _trayIcon = new WinForms.NotifyIcon
        {
            Text = "HideYourChat",
            Icon = icon,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) =>
        {
            if (IsVisible) Hide();
            else { Show(); WindowState = WindowState.Normal; Activate(); }
            if (_mainWindowMenuItem is not null) _mainWindowMenuItem.Checked = IsVisible;
        };
    }

    // ═══════════════ 窗口生命周期 ═══════════════

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_settings.CloseToExit) _isReallyClosing = true;
        if (!_isReallyClosing)
        {
            e.Cancel = true;
            Hide();
            if (_mainWindowMenuItem is not null) _mainWindowMenuItem.Checked = false;
        }
        base.OnClosing(e);
    }

    protected override async void OnClosed(EventArgs e)
    {
        SaveCurrentSettings();
        await _runtime.ShutdownAsync();
        _trayIcon?.Dispose();
        if (_settings.CloseToExit)
            Application.Current.Shutdown();
    }

    public void SetStatus(string text)
    {
        Dispatcher.Invoke(() => StatusText.Text = $"状态：{text}");
    }

    private void UpdateThemeButtonIcon(bool dark)
    {
        ThemeNavItem.Icon = new SymbolIcon(
            dark ? SymbolRegular.WeatherMoon24
                 : SymbolRegular.WeatherSunny24);
    }
}
