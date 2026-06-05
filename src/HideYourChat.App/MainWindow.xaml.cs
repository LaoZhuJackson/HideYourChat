using HideYourChat.App.Adapters.Mock;
using HideYourChat.App.Adapters.WeChat;
using HideYourChat.App.Automation;
using HideYourChat.App.Core;
using HideYourChat.App.Overlay;
using System.Collections.ObjectModel;
using System.Reflection.Metadata;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace HideYourChat.App;

public partial class MainWindow : FluentWindow
{
    private readonly ObservableCollection<ChatMessage> _recentMessages = new();

    private WeChatAdapter _adapter = null!;
    private readonly MessageDedupService _dedupService = new();
    private ChatMonitorService _monitorService = null!;

    private OverlayWindow? _overlayWindow;

    private double _backgroundOpacity = 0.80;
    private double _textOpacity = 1.00;

    public MainWindow()
    {
        InitializeComponent();

        RecentMessagesList.ItemsSource = _recentMessages;

        //默认使用微信进行初始化
        ReBuildAdapter("微信");

        BackgroundOpacityValueText.Text = $"{_backgroundOpacity:P0}";
        TextOpacityValueText.Text = $"{_textOpacity:P0}";

        SetConfigPanelEnabled(true);
    }
    /// <summary>根据选择的聊天软件,重建适配器和监听服务。仅在未监听时调用。</summary>
    private void ReBuildAdapter(string appName)
    {
        // 释放旧的
        (_adapter as IDisposable)?.Dispose();

        _adapter = appName switch
        {
            "微信" => new WeChatAdapter(),
            "QQ" => throw new NotImplementedException("QQ 适配器尚未实现"),
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

            foreach (var message in messages)
            {
                _recentMessages.Add(message);
            }

            while (_recentMessages.Count > 30)
            {
                _recentMessages.RemoveAt(0);
            }

            // 让列表自动滚到最新一条
            if (_recentMessages.Count > 0)
            {
                RecentMessagesList.ScrollIntoView(_recentMessages[^1]);
            }

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

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        // 防呆
        if (_monitorService.IsRunning)
        {
            StatusText.Text = "状态：监听已在运行中";
            return;
        }

        var selected = (AdapterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "微信";

        // TODO QQ
        if (selected == "QQ")
        {
            StatusText.Text = "状态：QQ 适配器尚未实现，无法开始监听。";
            return;
        }
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
            }
        }

        // 校验通过，锁定配置区，启动监听
        SetConfigPanelEnabled(false);

        EnsureOverlayWindow();
        _overlayWindow?.Show();

        _monitorService.Start(TimeSpan.FromMilliseconds(1500));

        StatusText.Text = $"状态：{selected} 监听已启动";
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        await _monitorService.StopAsync();

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

    //private void FindWindowButton_Click(object sender, RoutedEventArgs e)
    //{
    //    var keyword = WindowKeywordBox.Text.Trim();

    //    var windows = WindowFinder.FindByTitleKeyword(keyword);

    //    if (windows.Count == 0)
    //    {
    //        StatusText.Text = $"状态：没有找到标题包含“{keyword}”的窗口。";
    //        return;
    //    }

    //    var first = windows[0];

    //    StatusText.Text =
    //        $"状态：找到 {windows.Count} 个窗口。第一个：{first.Title}，句柄：0x{first.Handle.ToInt64():X}";
    //}

    protected override async void OnClosed(EventArgs e)
    {
        await _monitorService.StopAsync();

        (_adapter as IDisposable)?.Dispose();

        _overlayWindow?.Close();

        base.OnClosed(e);
    }
}