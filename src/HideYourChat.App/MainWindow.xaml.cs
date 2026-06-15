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
using System.Threading.Tasks;

namespace HideYourChat.App;

public partial class MainWindow : FluentWindow
{
    private IChatAdapter _adapter = null!;
    private readonly MessageDedupService _dedupService = new();
    private ChatMonitorService _monitorService = null!;

    private OverlayWindow? _overlayWindow;

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

        ReBuildAdapter(_settings.SelectedApp);

        ApplySettingsToUi(); // 把配置回填到各控件
        UpdateThemeButtonIcon(_settings.IsDarkTheme);

        SetConfigPanelEnabled(true);
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

    /// <summary>根据选择的聊天软件,重建适配器和监听服务。仅在未监听时调用。</summary>
    private void ReBuildAdapter(string appName)
    {
        // 释放旧的
        (_adapter as IDisposable)?.Dispose();

        _adapter = appName switch
        {
            "微信" => new WeChatAdapter(),
            "QQ" => new QQAdapter(),
            _ => new WeChatAdapter()
        };

        _monitorService = new ChatMonitorService(_adapter, _dedupService);
        _monitorService.NewMessagesReceived += MonitorService_NewMessagesReceived;
        _monitorService.ErrorOccurred += MonitorService_ErrorOccurred;
    }

    private void EnsureOverlayWindow()
    {
        if (_overlayWindow is not null)
        {
            return;
        }

        _overlayWindow = new OverlayWindow();
        _overlayWindow.SetBackgroundOpacity(_backgroundOpacity);
        _overlayWindow.SetTextOpacity(_textOpacity);

        // 应用保存的位置和尺寸
        _overlayWindow.ApplyPersistedBounds(
            _settings.OverlayLeft, _settings.OverlayTop,
            _settings.OverlayWidth, _settings.OverlayHeight
        );

        _overlayWindow.SendRequested += OverlayWindow_SendRequested;

        _overlayWindow.Closed += (_, _) =>
        {
            if (_overlayWindow is not null)
            {
                _overlayWindow.SendRequested -= OverlayWindow_SendRequested;
            }

            _overlayWindow = null;
        };
    }

    private void MonitorService_NewMessagesReceived(
        object? sender,
        IReadOnlyList<ChatMessage> messages)
    {
        Dispatcher.Invoke(() =>
        {
            EnsureOverlayWindow();

            _overlayWindow?.AddMessages(messages);

            if (_overlayWindow is { IsVisible: false })
            {
                _overlayWindow.Show();
            }

            StatusText.Text = $"状态：收到 {messages.Count} 条新消息，时间 {DateTime.Now:HH:mm:ss}";
        });
    }

    private void MonitorService_ErrorOccurred(object? sender, string error)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = $"状态：监听异常：{error}";
        });
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
        if (_overlayWindow is not null)
        {
            var (left, top, width, height) = _overlayWindow.GetPersistedBounds();
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
        // 防呆
        if (_monitorService.IsRunning)
        {
            StatusText.Text = "状态：监听已在运行中";
            return;
        }

        var selected = (AdapterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "微信";

        if (selected == "微信" && _adapter is WeChatAdapter wechat)
        {
            bool useStandalone = StandaloneWindowCheckBox.IsChecked == true;
            string contactName = ContactNameBox.Text.Trim();

            if (useStandalone)
            {
                //校验:勾了独立窗口但没填联系人(含纯空白)
                if (string.IsNullOrWhiteSpace(contactName))
                {
                    StatusText.Text = "状态：已勾选独立窗口模式，请先输入联系人名称";
                    ContactNameBox.Focus();
                    return;
                }
                // 启动前探测窗口是否存在,找不到就别启动
                if (_adapter is WeChatAdapter w && !w.CanFindTargetWindow())
                {
                    StatusText.Text = useStandalone
                        ? $"状态：找不到标题含「{contactName}」的窗口，请确认已打开该联系人的独立聊天窗口。"
                        : "状态：找不到微信主窗口，请确认微信已打开。";
                    SetConfigPanelEnabled(true); // 解锁回去
                    return;
                }

                // 把配置传给适配器
                wechat.UseStandaloneChatWindow = true;
                wechat.StandaloneChatWindowTitle = contactName;
            }
            else
            {
                wechat.UseStandaloneChatWindow = false;
                wechat.StandaloneChatWindowTitle = "";
                // 裁剪区域
                wechat.CropLeft = CropLeftBox.Value ?? 0.35;
                wechat.CropTop = CropTopBox.Value ?? 0.09;
                wechat.CropRight = CropRightBox.Value ?? 1.0;
                wechat.CropBottom = CropBottomBox.Value ?? 0.82;
            }
        }
        else if (_adapter is QQAdapter qqStart)
        {
            qqStart.HideMode = (QQWindowMover.QQHideMode)QQHideModeComboBox.SelectedIndex;
            qqStart.HideWindow(); // 移走QQ窗口
            ToggleQQWindowButton.Content = "显示 QQ 窗口";
        }

        // 校验通过，锁定配置区，保存用户配置
        SaveCurrentSettings();
        SetConfigPanelEnabled(false);

        EnsureOverlayWindow();
        _overlayWindow?.Show();

        _monitorService.Start(TimeSpan.FromMilliseconds(1500));

        StatusText.Text = $"状态：{selected} 监听已启动";
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        await _monitorService.StopAsync();
        if (_adapter is QQAdapter qqStop) qqStop.RestoreWindow(); // 恢复QQ窗口

        // 解锁配置区
        SetConfigPanelEnabled(true);

        StatusText.Text = "状态：监听已停止。";
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

    private void ShowOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        EnsureOverlayWindow();
        _overlayWindow?.Show();

        StatusText.Text = "状态：悬浮窗已显示。";
    }

    private void HideOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        _overlayWindow?.Hide();

        StatusText.Text = "状态：悬浮窗已隐藏。";
    }

    private async void OverlayWindow_SendRequested(object? sender, string message)
    {
        var result = await _adapter.SendMessageAsync("测试群聊", message);

        if (result.Success)
        {
            _overlayWindow?.SetReplyStatus("发送成功。下一次轮询会显示到消息列表。");
            StatusText.Text = $"状态：Overlay 发送成功，时间 {DateTime.Now:HH:mm:ss}";
        }
        else
        {
            _overlayWindow?.SetReplyStatus($"发送失败：{result.ErrorMessage}");
            StatusText.Text = $"状态：Overlay 发送失败：{result.ErrorMessage}";
        }
        // 发送后抢回焦点，防止焦点落在QQ窗口
        await Task.Delay(150);
        Dispatcher.Invoke(() =>
        {
            _overlayWindow?.Activate();
            _overlayWindow?.FocusReplayInput();
        });
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

        _overlayWindow?.SetBackgroundOpacity(_backgroundOpacity);

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

        _overlayWindow?.SetTextOpacity(_textOpacity);

        if (StatusText is not null)
        {
            StatusText.Text = $"状态：文字透明度已调整为 {_textOpacity:P0}";
        }
    }

    private void AdapterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StandaloneWindowPanel is null) return;

        var selected = (AdapterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "微信"; //默认使用微信
        //只有微信才显示独立窗口配置
        StandaloneWindowPanel.Visibility = selected == "微信" ? Visibility.Visible : Visibility.Collapsed;
        WeChatCropPanel.Visibility = selected == "微信" ? Visibility.Visible : Visibility.Collapsed;

        QQPanel.Visibility = selected == "QQ" ? Visibility.Visible : Visibility.Collapsed;

        //切换适配器
        ReBuildAdapter(selected);
        StatusText.Text = $"状态：已切换到 {selected} 适配器。";

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
        _overlayWindow?.ApplyTheme(dark); // 同步给overlap
        _settings.IsDarkTheme = dark; // 存入配置
        UpdateThemeButtonIcon(dark);
    }

    private void ToggleQQWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_adapter is not QQAdapter qq) return;

        if (qq.IsWindowHidden)
        {
            qq.RestoreWindow();
            ToggleQQWindowButton.Content = "隐藏 QQ 窗口";
        }
        else
        {
            qq.HideMode = (QQWindowMover.QQHideMode)QQHideModeComboBox.SelectedIndex;  // 用当前选择
            qq.HideWindow();
            ToggleQQWindowButton.Content = "显示 QQ 窗口";
        }
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
        if (_adapter is not WeChatAdapter wechat)
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

        if(result != null)
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
        else if(service.LastError != null)
        {
            StatusText.Text = $"状态：检查更新失败 — {service.LastError}";
        }
        else
        {
            StatusText.Text = "状态：已是最新版本 ✓";
        }

        CheckUpdateButton.IsEnabled = true;
    }

    protected override async void OnClosed(EventArgs e)
    {
        SaveCurrentSettings();          // 退出前兜底保存
        await _monitorService.StopAsync();
        (_adapter as QQAdapter)?.RestoreWindow(); // 退出前恢复QQ窗口
        (_adapter as IDisposable)?.Dispose();
        _overlayWindow?.Close();
        base.OnClosed(e);
    }
}