using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HideYourChat.App.Core;
using Color = System.Windows.Media.Color;

namespace HideYourChat.App.Overlay;

public partial class OverlayWindow : Window
{
    private const int ExpandedWidth = 480;
    private const int ExpandedHeight = 380;

    private double _lastExpandedWidth = 480;
    private double _lastExpandedHeight = 380;

    public int MaxMessages { get; set; } = 30;

    private double _backgroundOpacity = 0.80;
    private double _textOpacity = 1.00;
    private bool _isDark = true;
    // 画刷跟随哪条透明度轴
    private enum Axis { Background, Text };
    /// <summary>
    /// 一种画刷的"配方":深/浅两套基础 RGB + 跟随的透明度轴 + 由该轴不透明度算 alpha 的参数。
    /// 最终 alpha = Clamp(opacity * Mul + Add, Min, Max)。把颜色和透明度两个维度彻底拆开。
    /// </summary>
    private sealed record BrushSpec(
        Color Dark, Color Light, Axis Axis,
        double Mul, double Add, double Min, double Max
    );
    // 资源键 → 配方。颜色只在这里定义一份,XAML 里的 <SolidColorBrush> 仅作设计期占位。
    private static readonly IReadOnlyDictionary<string, BrushSpec> BrushSpecs =
        new Dictionary<string, BrushSpec>
        {
            // 背景族(跟随 _backgroundOpacity)
            ["OverlayBackgroundBrush"] = new(Rgb(0x11, 0x11, 0x11), Rgb(0xF5, 0xF5, 0xF5),
    Axis.Background, 1.00, 0.00, 0.01, 1.00),
            ["MessageCardBackgroundBrush"] = new(Rgb(0x11, 0x11, 0x11), Rgb(0x00, 0x00, 0x00),
    Axis.Background, 0.45, 0.00, 0.01, 0.75),
            ["BadgeBackgroundBrush"] = new(Rgb(0x3A, 0x3A, 0x3A), Rgb(0xC0, 0xC0, 0xC0),
    Axis.Background, 1.00, 0.10, 0.01, 1.00),
            ["ControlBackgroundBrush"] = new(Rgb(0x33, 0x33, 0x33), Rgb(0xDD, 0xDD, 0xDD),
    Axis.Background, 0.75, 0.00, 0.01, 0.95),
            ["ControlHoverBackgroundBrush"] = new(Rgb(0x44, 0x44, 0x44), Rgb(0xCC, 0xCC, 0xCC),
    Axis.Background, 0.85, 0.00, 0.01, 1.00),
            ["ControlPressedBackgroundBrush"] = new(Rgb(0x22, 0x22, 0x22), Rgb(0xBB, 0xBB, 0xBB),
    Axis.Background, 0.95, 0.00, 0.01, 1.00),
            ["ControlBorderBrush"] = new(Rgb(0x66, 0x66, 0x66), Rgb(0xAA, 0xAA, 0xAA),
    Axis.Background, 0.70, 0.00, 0.01, 0.90),
            ["ControlFocusedBorderBrush"] = new(Rgb(0x88, 0x88, 0x88), Rgb(0x88, 0x88, 0x88),
    Axis.Background, 0.95, 0.00, 0.01, 1.00),
            // 文字族(跟随 _textOpacity)
            ["PrimaryTextBrush"] = new(Rgb(0xFF, 0xFF, 0xFF), Rgb(0x22, 0x22, 0x22), Axis.Text,
         1.00, 0.00, 0.01, 1.00),
            ["SecondaryTextBrush"] = new(Rgb(0xFF, 0xFF, 0xFF), Rgb(0x22, 0x22, 0x22), Axis.Text,
         0.70, 0.00, 0.01, 1.00),
        };

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

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

        ApplyAppearance();
    }

    public void AddMessages(IEnumerable<ChatMessage> messages)
    {
        var newMessages = messages.ToList();
        if (newMessages.Count == 0) return;

        // 添加消息前判断用户是否在底部
        _wasAtBottom = IsScrolledToBottom();

        foreach (var message in newMessages)
        {
            Messages.Add(message);
        }

        while (Messages.Count > MaxMessages)
        {
            Messages.RemoveAt(0);
        }

        // 智能滚动
        if (!_isCollapsed && _wasAtBottom)
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
        if (e.ExtentHeightChange == 0.0) _wasAtBottom = IsScrolledToBottom();
    }

    public void SetBackgroundOpacity(double opacity)
    {
        _backgroundOpacity = Math.Clamp(opacity, 0.01, 1.0);
        ApplyAppearance();
    }

    public void SetTextOpacity(double opacity)
    {
        _textOpacity = Math.Clamp(opacity, 0.01, 1.0);
        ApplyAppearance();
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
            ReplyStatusText.Text = "不能发送空消息";
            return;
        }

        SendRequested?.Invoke(this, text);

        ReplyTextBox.Clear();
        ReplyStatusText.Text = "已提交发送请求";
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

    private void ReplyTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
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

    /// <summary>读取当前应持久化的位置和展开尺寸,交给上层保存</summary>
    public (double Left, double Top, double Width, double Height) GetPersistedBounds()
    {
        // 只返回展开时的数据
        double w = _isCollapsed ? _lastExpandedWidth : Width;
        double h = _isCollapsed ? _lastExpandedHeight : Height;
        return (Left, Top, w, h);
    }
    /// <summary>应用保存的位置和尺寸。位置不合法(NaN 或在屏幕外)时居中回退</summary>
    public void ApplyPersistedBounds(double left, double top, double width, double height)
    {
        // 尺寸：有合理值就用，否则保持默认
        if (width > 0) { Width = width; _lastExpandedWidth = width; }
        if (height > 0) { Height = height; _lastExpandedHeight = height; }
        // 位置：校验是否在可见屏内
        if (!double.IsNaN(left) && !double.IsNaN(top) && IsOnScreen(left, top, Width, Height))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        // 否则使用默认位置
    }

    /// <summary>判断矩形是否大致落在所有显示器组成的虚拟屏幕内</summary>
    private static bool IsOnScreen(double left, double top, double width, double height)
    {
        // 虚拟屏幕 = 所有显示器拼出的大矩形（WPF自带）
        double vLeft = SystemParameters.VirtualScreenLeft;
        double vTop = SystemParameters.VirtualScreenTop;
        double vRight = vLeft + SystemParameters.VirtualScreenWidth;
        double vBottom = vTop + SystemParameters.VirtualScreenHeight;

        // 窗口左上角在虚拟屏幕范围内,且至少留一部分可见(留 50px 余量防止贴边拖不回来)
        const double margin = 50;
        return left >= vLeft + margin && left <= vRight - margin &&
                top >= vTop + margin && top <= vBottom - margin;
    }

    public void ApplyTheme(bool dark)
    {
        _isDark = dark;
        ApplyAppearance();
    }
    
    /// <summary>
    /// 按当前(主题 + 两条透明度)重算所有画刷,一次性写回资源。
    /// 主题切换、背景透明度、文字透明度,全部走这一个出口
    /// </summary>
    private void ApplyAppearance()
    {
        foreach(var(key, spec) in BrushSpecs)
        {
            Color rgb = _isDark ? spec.Dark : spec.Light;
            double axisOpacity = spec.Axis == Axis.Background ? _backgroundOpacity : _textOpacity;

            double a = Math.Clamp(axisOpacity * spec.Mul + spec.Add, spec.Min, spec.Max);
            byte alpha = (byte)Math.Round(a * 255);

            var brush = new SolidColorBrush(Color.FromArgb(alpha, rgb.R, rgb.G, rgb.B));
            brush.Freeze(); // 整只替换、不再改 → 冻结省一点开销
            Resources[key] = brush;
        }
    }

    /// <summary>把焦点设回overlap回复输入框（发送消息后外部调用）</summary>
    public void FocusReplayInput()
    {
        // 先确保窗口被激活，再设焦点到输入框
        // 用 Dispatcher 延迟一帧，确保窗口已在前台
        Dispatcher.InvokeAsync(() =>
        {
            ReplyTextBox.Focus();
            Keyboard.Focus(ReplyTextBox);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
    }
}