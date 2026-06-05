using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HideYourChat.App.Core;

namespace HideYourChat.App.Overlay;

public partial class OverlayWindow : Window
{
    private const int ExpandedWidth = 480;
    private const int ExpandedHeight = 380;

    private double _lastExpandedWidth = 480;
    private double _lastExpandedHeight = 380;

    private double _backgroundOpacity = 0.80;
    private double _textOpacity = 1.00;

    private const int CollapsedSize = 72;

    private bool _isCollapsed;
    private int _unreadCount;
    // 记录添加消息前用户是否在底部(用于智能滚动)
    private bool _wasAtBottom = true;

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public event EventHandler<string>? SendRequested;


    public OverlayWindow()
    {
        InitializeComponent();

        DataContext = this;

        SetBackgroundOpacity(0.80);
        SetTextOpacity(1.00);
    }

    public void AddMessages(IEnumerable<ChatMessage> messages)
    {
        var newMessages = messages.ToList();
        if(newMessages.Count == 0) return;

        // 添加消息前判断用户是否在底部
        _wasAtBottom = IsScrolledToBottom();

        foreach (var message in newMessages)
        {
            Messages.Add(message);
        }

        while (Messages.Count > 8)
        {
            Messages.RemoveAt(0);
        }

        // 智能滚动
        if(!_isCollapsed && _wasAtBottom)
        {
            // 延迟一帧等ui布局完成后再滚动
            Dispatcher.InvokeAsync(() =>
            {
                MessageScrollViewer.ScrollToBottom();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        if (_isCollapsed && newMessages.Count > 0)
        {
            _unreadCount += newMessages.Count;
            UpdateUnreadBadge();
        }
    }
    /// <summary>判断滚动条是否在底部(阈值 5px,允许微小误差)</summary>
    private bool IsScrolledToBottom()
    {
        const double threshold = 5.0;
        var sv = MessageScrollViewer;
        // ExtentHeight 是内容总高度,ViewportHeight 是可见区域高度,VerticalOffset 是滚动偏移
        return sv.VerticalOffset + sv.ViewportHeight >= sv.ExtentHeight - threshold;
    }

    private void MessageScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // 用户主动滚动(非程序触发)时,更新 _wasAtBottom 状态
        // ExtentHeightChange == 0 说明不是因为内容变化触发的滚动,而是用户手动滚的
        if(e.ExtentHeightChange == 0.0) _wasAtBottom = IsScrolledToBottom();
    }

    public void SetBackgroundOpacity(double opacity)
    {
        _backgroundOpacity = Math.Clamp(opacity, 0.05, 1.0);

        SetBrushAlpha("OverlayBackgroundBrush", _backgroundOpacity);

        SetBrushAlpha(
            "MessageCardBackgroundBrush",
            Math.Clamp(_backgroundOpacity * 0.45, 0.05, 0.75));

        SetBrushAlpha(
            "BadgeBackgroundBrush",
            Math.Clamp(_backgroundOpacity + 0.10, 0.10, 1.0));

        // 控件背景和边框也跟随背景透明度
        SetBrushAlpha(
            "ControlBackgroundBrush",
            Math.Clamp(_backgroundOpacity * 0.75, 0.08, 0.95));

        SetBrushAlpha(
            "ControlHoverBackgroundBrush",
            Math.Clamp(_backgroundOpacity * 0.85, 0.10, 1.0));

        SetBrushAlpha(
            "ControlPressedBackgroundBrush",
            Math.Clamp(_backgroundOpacity * 0.95, 0.12, 1.0));

        SetBrushAlpha(
            "ControlBorderBrush",
            Math.Clamp(_backgroundOpacity * 0.70, 0.08, 0.90));

        SetBrushAlpha(
            "ControlFocusedBorderBrush",
            Math.Clamp(_backgroundOpacity * 0.95, 0.15, 1.0));
    }

    public void SetTextOpacity(double opacity)
    {
        _textOpacity = Math.Clamp(opacity, 0.20, 1.0);

        SetBrushAlpha("PrimaryTextBrush", _textOpacity);
        SetBrushAlpha("SecondaryTextBrush", Math.Clamp(_textOpacity * 0.70, 0.15, 1.0));
    }

    private void SetBrushAlpha(string resourceKey, double opacity)
    {
        if (Resources[resourceKey] is not SolidColorBrush oldBrush)
        {
            return;
        }

        var alpha = (byte)Math.Round(Math.Clamp(opacity, 0.0, 1.0) * 255);

        var oldColor = oldBrush.Color;

        var newBrush = new SolidColorBrush(Color.FromArgb(
            alpha,
            oldColor.R,
            oldColor.G,
            oldColor.B));

        Resources[resourceKey] = newBrush;
    }

    public void SetReplyStatus(string text)
    {
        ReplyStatusText.Text = text;
    }

    private void SendCurrentReply()
    {
        var text = ReplyTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            ReplyStatusText.Text = "不能发送空消息。";
            return;
        }

        SendRequested?.Invoke(this, text);

        ReplyTextBox.Clear();
        ReplyStatusText.Text = "已提交发送请求。";
    }

    // 折叠
    private void Collapse()
    {
        _isCollapsed = true;

        _lastExpandedWidth = Width;
        _lastExpandedHeight = Height;

        ExpandedPanel.Visibility = Visibility.Collapsed;
        CollapsedRoot.Visibility = Visibility.Visible;

        Width = CollapsedSize;
        Height = CollapsedSize;

        ResizeMode = ResizeMode.NoResize;

        UpdateUnreadBadge();
    }

    private void Expand()
    {
        _isCollapsed = false;

        ResizeMode = ResizeMode.CanResizeWithGrip;

        Width = _lastExpandedWidth;
        Height = _lastExpandedHeight;

        CollapsedRoot.Visibility = Visibility.Collapsed;
        ExpandedPanel.Visibility = Visibility.Visible;

        _unreadCount = 0;
        UpdateUnreadBadge();
    }

    private void UpdateUnreadBadge()
    {
        if (_unreadCount <= 0)
        {
            UnreadBadge.Visibility = Visibility.Collapsed;
            UnreadBadgeText.Text = "0";
            return;
        }

        UnreadBadge.Visibility = Visibility.Visible;
        UnreadBadgeText.Text = _unreadCount > 99 ? "99+" : _unreadCount.ToString();
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        SendCurrentReply();
    }

    private void ReplyTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SendCurrentReply();
            e.Handled = true;
        }
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        Collapse();
    }

    private void CollapsedRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            Expand();
            e.Handled = true;
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // DragMove 在极少数情况下会因为鼠标状态异常抛错，MVP 阶段忽略即可。
            }
        }
    }

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // 同上，避免拖拽异常导致程序崩溃。
            }
        }
    }

    private void MessageScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        var nextOffset = scrollViewer.VerticalOffset - e.Delta / 3.0;
        nextOffset = Math.Clamp(nextOffset, 0, scrollViewer.ScrollableHeight);

        scrollViewer.ScrollToVerticalOffset(nextOffset);

        e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
    }
}