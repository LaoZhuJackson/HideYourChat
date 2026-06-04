using System.Collections.ObjectModel;
using System.Windows;
using HideYourChat.App.Adapters.Mock;
using HideYourChat.App.Automation;
using HideYourChat.App.Core;
using HideYourChat.App.Overlay;

namespace HideYourChat.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ChatMessage> _recentMessages = new();

    private readonly MockChatAdapter _adapter = new();
    private readonly MessageDedupService _dedupService = new();
    private readonly ChatMonitorService _monitorService;

    private OverlayWindow? _overlayWindow;

    private double _backgroundOpacity = 0.80;
    private double _textOpacity = 1.00;

    public MainWindow()
    {
        InitializeComponent();

        RecentMessagesList.ItemsSource = _recentMessages;

        _monitorService = new ChatMonitorService(_adapter, _dedupService);

        _monitorService.NewMessagesReceived += MonitorService_NewMessagesReceived;
        _monitorService.ErrorOccurred += MonitorService_ErrorOccurred;

        BackgroundOpacityValueText.Text = $"{_backgroundOpacity:P0}";
        TextOpacityValueText.Text = $"{_textOpacity:P0}";
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
                _recentMessages.Insert(0, message);
            }

            while (_recentMessages.Count > 30)
            {
                _recentMessages.RemoveAt(_recentMessages.Count - 1);
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
        EnsureOverlayWindow();
        _overlayWindow?.Show();

        _monitorService.Start(TimeSpan.FromMilliseconds(1500));

        StatusText.Text = "状态：监听已启动。Mock 适配器会每 1.5 秒生成一条消息。";
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        await _monitorService.StopAsync();

        StatusText.Text = "状态：监听已停止。";
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

    private void FindWindowButton_Click(object sender, RoutedEventArgs e)
    {
        var keyword = WindowKeywordBox.Text.Trim();

        var windows = WindowFinder.FindByTitleKeyword(keyword);

        if (windows.Count == 0)
        {
            StatusText.Text = $"状态：没有找到标题包含“{keyword}”的窗口。";
            return;
        }

        var first = windows[0];

        StatusText.Text =
            $"状态：找到 {windows.Count} 个窗口。第一个：{first.Title}，句柄：0x{first.Handle.ToInt64():X}";
    }

    protected override async void OnClosed(EventArgs e)
    {
        await _monitorService.StopAsync();

        _overlayWindow?.Close();

        base.OnClosed(e);
    }
}