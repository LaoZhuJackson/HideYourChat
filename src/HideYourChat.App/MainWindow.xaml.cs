using HideYourChat.App.Adapters.QQ;
using HideYourChat.App.Adapters.WeChat;
using HideYourChat.App.Core;
using HideYourChat.App.Overlay;
using System.Windows;
using System.Windows.Controls;
using Serilog;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using HideYourChat.App.Update;
using System.ComponentModel;
using WinForms = System.Windows.Forms;

namespace HideYourChat.App;

public partial class MainWindow : FluentWindow
{
    private readonly ChatRuntimeService _runtime = new();
    // 托盘相关
    private WinForms.NotifyIcon? _trayIcon;
    private bool _isReallyClosing;
    private WinForms.ToolStripMenuItem? _monitorMenuItem;
    private WinForms.ToolStripMenuItem? _overlayMenuItem;
    // 透明度相关
    private double _backgroundOpacity = 0.80;
    private double _textOpacity = 1.00;

    // 配置文件
    private readonly SettingsService _settingsService = new();
    private AppSettings _settings = new();

    public MainWindow()
    {
        InitializeComponent();

        _settings = _settingsService.Load();

        _backgroundOpacity = _settings.BackgroundOpacity;
        _textOpacity = _settings.TextOpacity;

        _runtime.SwitchAdapter(_settings.SelectedApp);
        _runtime.StatusChanged += (_, text) =>
            Dispatcher.Invoke(() => StatusText.Text = $"状态：{text}");
        _runtime.ConfigLockChanged += (_, locked) =>
            Dispatcher.Invoke(() => SetConfigPanelEnabled(locked));

        ApplySettingsToUi(); // 把配置回填到各控件
        UpdateThemeButtonIcon(_settings.IsDarkTheme);
        SetConfigPanelEnabled(true);
        InitTrayIcon();
    }

    /// <summary>把已加载的配置回填到界面控件上。</summary>
    private void ApplySettingsToUi()
    {
        // 下拉框选中的软件
        foreach (ComboBoxItem item in AdapterComboBox.Items)
        {
            if ((item.Content?.ToString() ?? "") == _settings.SelectedApp)
            {
                AdapterComboBox.SelectedItem = item;
                break;
            }
        }

        StandaloneWindowCheckBox.IsChecked = _settings.UseStandaloneChatWindow;
        ContactNameBox.Text = _settings.ContactName;
        ContactNameBox.IsEnabled = _settings.UseStandaloneChatWindow;

        BackgroundOpacitySlider.Value = _settings.BackgroundOpacity;
        TextOpacitySlider.Value = _settings.TextOpacity;
        BackgroundOpacityValueText.Text = $"{_backgroundOpacity:P0}";
        TextOpacityValueText.Text = $"{_textOpacity:P0}";

        StandaloneWindowPanel.Visibility =
            _settings.SelectedApp == "微信" ? Visibility.Visible : Visibility.Collapsed;

        CropLeftBox.Value = _settings.WeChatCropLeft;
        CropRightBox.Value = _settings.WeChatCropRight;
        CropTopBox.Value = _settings.WeChatCropTop;
        CropBottomBox.Value = _settings.WeChatCropBottom;
        WeChatCropPanel.Visibility =
            _settings.SelectedApp == "微信" ? Visibility.Visible : Visibility.Collapsed;

        QQHideModeComboBox.SelectedIndex = Math.Clamp(_settings.QQHideModeIndex, 0, 2);

        // 应用主题
        ApplicationThemeManager.Apply(_settings.IsDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }

    /// <summary>
    /// 保存设置
    /// </summary>
    private void SaveCurrentSettings()
    {
        _settings.SelectedApp = (AdapterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "微信";
        _settings.UseStandaloneChatWindow = StandaloneWindowCheckBox.IsChecked == true;
        _settings.ContactName = ContactNameBox.Text.Trim();
        _settings.BackgroundOpacity = _backgroundOpacity;
        _settings.TextOpacity = _textOpacity;

        // 保存微信裁剪区域
        _settings.WeChatCropLeft = CropLeftBox.Value ?? 0.35;
        _settings.WeChatCropTop = CropTopBox.Value ?? 0.09;
        _settings.WeChatCropRight = CropRightBox.Value ?? 1.0;
        _settings.WeChatCropBottom = CropBottomBox.Value ?? 0.82;

        // 以当前实际生效的主题为准落盘
        _settings.IsDarkTheme = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

        // overlay 得判空，不存在则保留上次的值（不覆盖）
        var (left, top, width, height) = _runtime.GetOverlayBounds();
        if (!double.IsNaN(left))
        {
            _settings.OverlayLeft = left;
            _settings.OverlayTop = top;
            _settings.OverlayWidth = width;
            _settings.OverlayHeight = height;
        }

        _settings.QQHideModeIndex = QQHideModeComboBox.SelectedIndex;

        _settingsService.Save(_settings);
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentSettings();
        if (!_runtime.Start(_settings, _backgroundOpacity, _textOpacity))
            return; // Start 返回 false 表示校验失败，通过回调设置 StatusText
        SetConfigPanelEnabled(false);
        ToggleQQWindowButton.Content = _runtime.IsQQWindowHidden ? "显示 QQ 窗口" : "隐藏 QQ 窗口";
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        await _runtime.StopAsync();
        SetConfigPanelEnabled(true);
        ToggleQQWindowButton.Content = "显示 QQ 窗口";
    }

    private void ShowOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        _runtime.ShowOverlay();
        StatusText.Text = "状态：悬浮窗已显示";
    }

    private void HideOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        _runtime.HideOverlay();
        StatusText.Text = "状态：悬浮窗已隐藏";
    }

    /// <summary>
    /// 监听运行时锁定所有配置控件,停止后解锁。
    /// 这是防止"运行中切换适配器/勾选独立窗口/改联系人"的核心。
    /// </summary>
    private void SetConfigPanelEnabled(bool enabled)
    {
        AdapterComboBox.IsEnabled = enabled;
        StandaloneWindowCheckBox.IsEnabled = enabled;

        // 裁剪区域使能
        CropLeftBox.IsEnabled = CropTopBox.IsEnabled = CropRightBox.IsEnabled = CropBottomBox.IsEnabled = enabled;

        // 联系人文本框:只有"启用 且 勾选了独立窗口"时才可输入
        ContactNameBox.IsEnabled = enabled && StandaloneWindowCheckBox.IsChecked == true;

        // 开始/停止按钮互斥(需要给 XAML 里的按钮加 x:Name,比如 StartButton / StopButton)
        StartButton.IsEnabled = enabled;
        StopButton.IsEnabled = !enabled;
    }

    private void BackgroundOpacitySlider_ValueChanged(
    object sender,
    RoutedPropertyChangedEventArgs<double> e)
    {
        _backgroundOpacity = e.NewValue;

        if (BackgroundOpacityValueText is not null)
        {
            BackgroundOpacityValueText.Text = $"{_backgroundOpacity:P0}";
        }

        _runtime.SetBackgroundOpacity(_backgroundOpacity);

        if (StatusText is not null)
        {
            StatusText.Text = $"状态：背景透明度已调整为 {_backgroundOpacity:P0}";
        }
    }

    private void TextOpacitySlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        _textOpacity = e.NewValue;

        if (TextOpacityValueText is not null)
        {
            TextOpacityValueText.Text = $"{_textOpacity:P0}";
        }

        _runtime.SetTextOpacity(_textOpacity);

        if (StatusText is not null)
        {
            StatusText.Text = $"状态：文字透明度已调整为 {_textOpacity:P0}";
        }
    }

    private void AdapterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StandaloneWindowPanel is null) return;

        var selected = (AdapterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "QQ";
        StandaloneWindowPanel.Visibility = selected == "微信" ? Visibility.Visible : Visibility.Collapsed;
        WeChatCropPanel.Visibility = selected == "微信" ? Visibility.Visible : Visibility.Collapsed;
        QQPanel.Visibility = selected == "QQ" ? Visibility.Visible : Visibility.Collapsed;

        _runtime.SwitchAdapter(selected);
    }

    private void StandaloneWindowCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (ContactNameBox is null) return;

        // 勾选时才允许输入联系人名
        ContactNameBox.IsEnabled = StandaloneWindowCheckBox.IsChecked == true;

        if (StandaloneWindowCheckBox.IsChecked != true)
        {
            ContactNameBox.Clear();
        }
    }

    private void ToggleThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var current = ApplicationThemeManager.GetAppTheme();
        var target = current == ApplicationTheme.Dark ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(target);

        bool dark = target == ApplicationTheme.Dark;
        _runtime.ApplyTheme(dark); // 同步给overlap
        _settings.IsDarkTheme = dark; // 存入配置
        UpdateThemeButtonIcon(dark);
    }

    private void ToggleQQWindowButton_Click(object sender, RoutedEventArgs e)
    {
        _runtime.ToggleQQWindow(QQHideModeComboBox.SelectedIndex);
        ToggleQQWindowButton.Content = _runtime.IsQQWindowHidden ? "显示 QQ 窗口" : "隐藏 QQ 窗口";
    }

    private void UpdateThemeButtonIcon(bool dark)
    {
        // 深色给月亮，浅色给太阳
        ToggleThemeButton.Icon = new Wpf.Ui.Controls.SymbolIcon(
            dark ? Wpf.Ui.Controls.SymbolRegular.WeatherMoon24
                    : Wpf.Ui.Controls.SymbolRegular.WeatherSunny24
        );
    }

    private async void TryCaptureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_runtime.Adapter is not WeChatAdapter wechat)
        {
            StatusText.Text = "状态：试截图仅微信适配器可用";
            return;
        }
        if (CropLeftBox.Value >= CropRightBox.Value)
        {
            StatusText.Text = "状态：左边界比例大于右边界比例";
            return;
        }
        if (CropTopBox.Value >= CropBottomBox.Value)
        {
            StatusText.Text = "状态：上边界比例大于下边界比例";
            return;
        }
        // 灌入当前 UI 上的配置, 确保是实时加载最新配置
        wechat.CropLeft = CropLeftBox.Value ?? 0.35;
        wechat.CropTop = CropTopBox.Value ?? 0.09;
        wechat.CropRight = CropRightBox.Value ?? 1.0;
        wechat.CropBottom = CropBottomBox.Value ?? 0.82;
        wechat.UseStandaloneChatWindow = StandaloneWindowCheckBox.IsChecked == true;
        wechat.StandaloneChatWindowTitle = ContactNameBox.Text.Trim();

        StatusText.Text = "状态：正在截图...";
        TryCaptureButton.IsEnabled = false;
        try
        {
            var (preview, title) = await wechat.CaptureCropPreviewAsync();
            if (preview is null)
            {
                StatusText.Text = "状态：试截图失败（窗口未找到/最小化，或缺中文 OCR）。";
                return;
            }
            using (preview) // 转成 WPF 图源后即可释放 GDI 位图
                CropPreviewImage.Source = ToBitmapImage(preview);
            StatusText.Text = string.IsNullOrWhiteSpace(title)
                ? $"状态：试截图完成 {DateTime.Now:HH:mm:ss}"
                : $"状态：试截图完成，标题识别：「{title}」 {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"状态：试截图异常：{ex.Message}";
        }
        finally
        {
            TryCaptureButton.IsEnabled = true;
        }
    }

    /// <summary>System.Drawing.Bitmap → WPF 可显示的图源(走内存 PNG,避免 GDI 句柄泄漏)</summary>
    private static System.Windows.Media.Imaging.BitmapImage ToBitmapImage(System.Drawing.Bitmap bmp)
    {
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;

        var img = new System.Windows.Media.Imaging.BitmapImage();
        img.BeginInit();
        img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; // 立刻读完
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }

    // 检查更新
    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        StatusText.Text = "状态：正在检查更新…";

        using var service = new UpdateService(_settings);
        var result = await service.CheckAsync(UpdateService.CurrentVersion);

        if (result != null)
        {
            StatusText.Text = $"状态：发现新版本 v{result.Version}";
            var window = new UpdateWindow(result, service, skippedVersion =>
            {
                _settings.SkippedVersion = skippedVersion;
                new SettingsService().Save(_settings);
            })
            {
                Owner = this
            };
            window.ShowDialog();
        }
        else if (service.LastError != null)
        {
            StatusText.Text = $"状态：检查更新失败 — {service.LastError}";
        }
        else
        {
            StatusText.Text = $"状态：版本 {UpdateService.CurrentVersion} 已是最新版本 ✓";
        }

        CheckUpdateButton.IsEnabled = true;
    }

    protected override async void OnClosed(EventArgs e)
    {
        SaveCurrentSettings();          // 退出前兜底保存
        await _runtime.ShutdownAsync();
        base.OnClosed(e);
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isReallyClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    /// <summary>
    /// 托盘功能管理,指定调用函数
    /// </summary>
    private void InitTrayIcon()
    {
        var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);

        var menu = new WinForms.ContextMenuStrip();

        // 监听开关
        _monitorMenuItem = new WinForms.ToolStripMenuItem("开始监听");
        _monitorMenuItem.Click += (_, _) => ToggleMonitorFromTray();
        menu.Items.Add(_monitorMenuItem);

        // 悬浮窗开关
        _overlayMenuItem = new WinForms.ToolStripMenuItem("显示悬浮窗");
        _overlayMenuItem.Click += (_, _) =>
        {
            _runtime.ShowOverlay();
            _overlayMenuItem!.Text = "隐藏悬浮窗";
        };
        menu.Items.Add(_overlayMenuItem);

        menu.Items.Add("显示主窗口", null, (_, _) => ShowFromTray());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("退出 HideYourChat", null, (_, _) => ExitFromTray());

        _trayIcon = new WinForms.NotifyIcon
        {
            Text = "HideYourChat",
            Icon = icon,
            ContextMenuStrip = menu,
            Visible = true,
        };

        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        _isReallyClosing = true;
        _trayIcon?.Dispose();
        _trayIcon = null;
        System.Windows.Application.Current.Shutdown();
    }

    private void ToggleMonitorFromTray()
    {
        if (_runtime.IsRunning) _ = StopMonitorFromTrayAsync();
        else StartMonitorFromTray();
    }

    private async Task StopMonitorFromTrayAsync()
    {
        await _runtime.StopAsync();
        SetConfigPanelEnabled(true);
        ToggleQQWindowButton.Content = "显示 QQ 窗口";
        if (_monitorMenuItem is not null) _monitorMenuItem.Text = "开始监听";
    }

    private void StartMonitorFromTray()
    {
        SaveCurrentSettings();
        if (!_runtime.Start(_settings, _backgroundOpacity, _textOpacity))
            return;
        SetConfigPanelEnabled(false);
        ToggleQQWindowButton.Content = _runtime.IsQQWindowHidden ? "显示 QQ 窗口" : "隐藏 QQ 窗口";
        if (_monitorMenuItem is not null) _monitorMenuItem.Text = "停止监听";
        if (_overlayMenuItem is not null) _overlayMenuItem.Text = "隐藏悬浮窗";
    }
}