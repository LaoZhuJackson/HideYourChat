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
        _settingsPage.StatusChanged += text =>
            Dispatcher.Invoke(() => StatusText.Text = $"状态：{text}");
        _runtime.StatusChanged += (_, text) =>
            Dispatcher.Invoke(() => StatusText.Text = $"状态：{text}");

        // 导航事件
        foreach (Wpf.Ui.Controls.NavigationViewItem item in NavView.MenuItems)
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
            if(item.Tag is "theme")
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
        UpdateTrayMenuState();
    }

    private async void OnStopRequested()
    {
        await _runtime.StopAsync();
        _homePage.SetRunningMode(false);
        UpdateTrayMenuState();
    }

    private void UpdateTrayMenuState()
    {
        if (_monitorMenuItem is not null)
            _monitorMenuItem.Text = _runtime.IsRunning ? "停止监听" : "开始监听";
        if (_overlayMenuItem is not null)
            _overlayMenuItem.Text = "显示悬浮窗";
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

        _monitorMenuItem = new WinForms.ToolStripMenuItem("开始监听");
        _monitorMenuItem.Click += (_, _) =>
        {
            if (_runtime.IsRunning) OnStopRequested();
            else OnStartRequested();
        };
        menu.Items.Add(_monitorMenuItem);

        _overlayMenuItem = new WinForms.ToolStripMenuItem("显示悬浮窗");
        _overlayMenuItem.Click += (_, _) =>
        {
            _runtime.ShowOverlay();
            _overlayMenuItem!.Text = "隐藏悬浮窗";
        };
        menu.Items.Add(_overlayMenuItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("显示主窗口", null, (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });
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
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };
    }

    // ═══════════════ 窗口生命周期 ═══════════════
    protected override void OnClosing(CancelEventArgs e)
    {
        if(_settings.CloseToExit) _isReallyClosing = true;
        if (!_isReallyClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    protected override async void OnClosed(EventArgs e)
    {
        SaveCurrentSettings();
        await _runtime.ShutdownAsync();
        _trayIcon?.Dispose();
        if(_settings.CloseToExit)
            Application.Current.Shutdown();
    }

    private void UpdateThemeButtonIcon(bool dark)
    {
        // 主页里的主题按钮图标会自己更新，这里只记录初始状态
    }

    public void SetStatus(string text)
    {
        Dispatcher.Invoke(() => StatusText.Text = $"状态：{text}");
    }
}