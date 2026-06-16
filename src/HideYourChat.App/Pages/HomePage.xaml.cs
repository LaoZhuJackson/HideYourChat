using System.IO;
using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using HideYourChat.App.Adapters.QQ;
using HideYourChat.App.Adapters.WeChat;
using HideYourChat.App.Core;
using Wpf.Ui.Appearance;

namespace HideYourChat.App.Pages;

public partial class HomePage : UserControl
{
    private readonly ChatRuntimeService _runtime;
    private readonly AppSettings _settings;

    private double _backgroundOpacity = 0.80;
    private double _textOpacity = 1.00;
    private bool _initialized;
    private System.Drawing.Bitmap? _lastCropPreview;

    public HomePage(ChatRuntimeService runtime, AppSettings settings)
    {
        InitializeComponent();
        _runtime = runtime;
        _settings = settings;

        _backgroundOpacity = settings.BackgroundOpacity;
        _textOpacity = settings.TextOpacity;

        _initialized = true;

        ApplySettingsToUi();
        SetRunningMode(false);
    }

    public double BackgroundOpacity => _backgroundOpacity;
    public double TextOpacity => _textOpacity;

    // ═══════════════ 事件（MainWindow 订阅） ═══════════════

    public event Action? StartRequested;
    public event Action? StopRequested;

    // ═══════════════ 配置回填 / 保存 / 锁定 ═══════════════

    public void ApplySettingsToUi()
    {
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

        CropLeftBox.Value = _settings.WeChatCropLeft;
        CropRightBox.Value = _settings.WeChatCropRight;
        CropTopBox.Value = _settings.WeChatCropTop;
        CropBottomBox.Value = _settings.WeChatCropBottom;
        QQHideModeComboBox.SelectedIndex = Math.Clamp(_settings.QQHideModeIndex, 0, 2);

        BackgroundOpacitySlider.Value = _settings.BackgroundOpacity;
        TextOpacitySlider.Value = _settings.TextOpacity;
        BackgroundOpacityValueText.Text = $"{_backgroundOpacity:P0}";
        TextOpacityValueText.Text = $"{_textOpacity:P0}";

        UpdateAdapterPanels();
    }

    public void SaveToSettings()
    {
        _settings.SelectedApp = (AdapterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "QQ";
        _settings.UseStandaloneChatWindow = StandaloneWindowCheckBox.IsChecked == true;
        _settings.ContactName = ContactNameBox.Text.Trim();
        _settings.WeChatCropLeft = CropLeftBox.Value ?? 0.35;
        _settings.WeChatCropTop = CropTopBox.Value ?? 0.09;
        _settings.WeChatCropRight = CropRightBox.Value ?? 1.0;
        _settings.WeChatCropBottom = CropBottomBox.Value ?? 0.82;
        _settings.QQHideModeIndex = QQHideModeComboBox.SelectedIndex;
    }

    public void SetRunningMode(bool isRunning)
    {
        AdapterComboBox.IsEnabled = !isRunning;
        StartButton.IsEnabled = !isRunning;
        StopButton.IsEnabled = isRunning;
        StandaloneWindowCheckBox.IsEnabled = !isRunning;
        ContactNameBox.IsEnabled = !isRunning && StandaloneWindowCheckBox.IsChecked == true;
        CropLeftBox.IsEnabled = !isRunning;
        CropRightBox.IsEnabled = !isRunning;
        CropTopBox.IsEnabled = !isRunning;
        CropBottomBox.IsEnabled = !isRunning;
        QQHideModeComboBox.IsEnabled = !isRunning;
    }

    // ═══════════════ 按钮处理 ═══════════════

    private void StartButton_Click(object sender, RoutedEventArgs e)
        => StartRequested?.Invoke();

    private void StopButton_Click(object sender, RoutedEventArgs e)
        => StopRequested?.Invoke();

    private void ShowOverlayButton_Click(object sender, RoutedEventArgs e)
        => _runtime.ShowOverlay();

    private void HideOverlayButton_Click(object sender, RoutedEventArgs e)
        => _runtime.HideOverlay();

    private void ToggleQQWindowButton_Click(object sender, RoutedEventArgs e)
    {
        _runtime.ToggleQQWindow(_settings.QQHideModeIndex);
        ToggleQQWindowButton.Content = _runtime.IsQQWindowHidden
            ? "显示 QQ 窗口" : "隐藏 QQ 窗口";
    }

    private void StandaloneWindowCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        ContactNameBox.IsEnabled = StandaloneWindowCheckBox.IsChecked == true;
        if (StandaloneWindowCheckBox.IsChecked != true) ContactNameBox.Clear();
    }

    // ═══════════════ 适配器切换 ═══════════════

    private void AdapterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        var selected = (AdapterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "微信";
        _runtime.SwitchAdapter(selected);
        UpdateAdapterPanels();
    }

    private void UpdateAdapterPanels()
    {
        var isQQ = _runtime.Adapter is QQAdapter;
        var isWeChat = !isQQ;

        StandaloneWindowPanel.Visibility = isWeChat ? Visibility.Visible : Visibility.Collapsed;
        WeChatCropPanel.Visibility = isWeChat ? Visibility.Visible : Visibility.Collapsed;
        QQPanel.Visibility = isQQ ? Visibility.Visible : Visibility.Collapsed;

        if (isQQ)
        {
            ToggleQQWindowButton.Content = _runtime.IsQQWindowHidden
                ? "显示 QQ 窗口" : "隐藏 QQ 窗口";
        }
    }

    // ═══════════════ 透明度 ═══════════════

    private void BackgroundOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        _backgroundOpacity = e.NewValue;
        BackgroundOpacityValueText.Text = $"{_backgroundOpacity:P0}";
        _runtime.SetBackgroundOpacity(_backgroundOpacity);
    }

    private void TextOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        _textOpacity = e.NewValue;
        TextOpacityValueText.Text = $"{_textOpacity:P0}";
        _runtime.SetTextOpacity(_textOpacity);
    }

    // ═══════════════ 试截图 ═══════════════

    private async void TryCaptureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_runtime.Adapter is not WeChatAdapter wechat)
        {
            NotifyStatus("试截图仅微信适配器可用");
            return;
        }
        if (CropLeftBox.Value >= CropRightBox.Value)
        {
            NotifyStatus("左边界比例大于右边界比例");
            return;
        }
        if (CropTopBox.Value >= CropBottomBox.Value)
        {
            NotifyStatus("上边界比例大于下边界比例");
            return;
        }

        wechat.CropLeft = CropLeftBox.Value ?? 0.35;
        wechat.CropTop = CropTopBox.Value ?? 0.09;
        wechat.CropRight = CropRightBox.Value ?? 1.0;
        wechat.CropBottom = CropBottomBox.Value ?? 0.82;
        wechat.UseStandaloneChatWindow = StandaloneWindowCheckBox.IsChecked == true;
        wechat.StandaloneChatWindowTitle = ContactNameBox.Text.Trim();

        NotifyStatus("正在截图...");
        TryCaptureButton.IsEnabled = false;
        try
        {
            var (preview, title) = await wechat.CaptureCropPreviewAsync();
            if (preview is null)
            {
                NotifyStatus("试截图失败（窗口未找到/最小化，或缺中文 OCR）。");
                return;
            }
            _lastCropPreview?.Dispose();
            _lastCropPreview = preview;
            CropPreviewImage.Source = ToBitmapImage(preview);
            var now = DateTime.Now;
            NotifyStatus(string.IsNullOrWhiteSpace(title)
                ? $"试截图完成 {now:HH:mm:ss}"
                : $"试截图完成，标题识别：「{title}」 {now:HH:mm:ss}");
        }
        catch (Exception ex)
        {
            NotifyStatus($"试截图异常：{ex.Message}");
        }
        finally
        {
            TryCaptureButton.IsEnabled = true;
        }
    }

    private void CropPreviewImage_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_lastCropPreview is null) return;

        var img = ToBitmapImage(_lastCropPreview);
        var previewWindow = new Window
        {
            Title = "预览",
            Width = Math.Min(_lastCropPreview.Width + 40, SystemParameters.PrimaryScreenWidth * 0.9),
            Height = Math.Min(_lastCropPreview.Height + 60, SystemParameters.PrimaryScreenHeight * 0.9),
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Owner = System.Windows.Window.GetWindow(this),
            ResizeMode = ResizeMode.CanResize,
            WindowStyle = WindowStyle.ToolWindow,
        };
        var imageControl = new System.Windows.Controls.Image
        {
            Source = img,
            Stretch = System.Windows.Media.Stretch.Uniform,
            Margin = new Thickness(10),
        };
        previewWindow.Content = imageControl;
        previewWindow.ShowDialog();
    }

    // ═══════════════ 工具方法 ═══════════════

    private void NotifyStatus(string text)
    {
        // 通过 MainWindow 的状态栏显示
        var mw = (MainWindow)System.Windows.Window.GetWindow(this);
        mw.SetStatus(text);
    }

    private static System.Windows.Media.Imaging.BitmapImage ToBitmapImage(System.Drawing.Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;

        var img = new System.Windows.Media.Imaging.BitmapImage();
        img.BeginInit();
        img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }
}