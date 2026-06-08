
using FlaUI.UIA3;
using FlaUI.UIA3.Patterns;
using HideYourChat.App.Core;
using HideYourChat.App.Imaging;
using Serilog;

namespace HideYourChat.App.Adapters.WeChat;

public sealed class WeChatAdapter : IChatAdapter, IDisposable
{
    private readonly UIA3Automation _automation = new();

    private readonly WeChatWindowLocator _windowLocator = new();
    private readonly ScreenCapture _capture = new();
    private readonly CaptureDebugSink _debug = new();
    // private readonly IOcrEngine _ocr = new WindowsOcrEngine();
    private readonly IOcrEngine _ocr = new PaddleOcrEngine();
    private static readonly string[] WeChatProcessNames = ["Weixin", "WeChat", "微信", "WXWork"];
    private const double CropLeft = 0.35, CropTop = 0.06, CropRight = 1.0, CropBottom = 0.82;

    // 是否监听独立聊天窗口(而不是主窗口)
    public bool UseStandaloneChatWindow { get; set; } = false;
    public string StandaloneChatWindowTitle { get; set; } = ""; // 要监听的联系人名

    // 独立窗口裁剪比例
    private const double StandaloneCropLeft = 0.02, StandaloneCropTop = 0.057, StandaloneCropRight = 0.89, StandaloneCropBottom = 0.90;

    // 避免重复ocr
    private readonly FrameChangeDetector _frameDetector = new();
    private IReadOnlyList<ChatMessage> _lastResult = [];   // 缓存上一帧结果

    private readonly WeChatSender _sender = new();

    public string Id => "wechat";
    public string DisplayName => "WeChat";

    public async Task<IReadOnlyList<ChatMessage>> ReadLatestMessagesAsync(CancellationToken cancellationToken = default)
    {
        IntPtr hwnd;
        double cropL, cropT, cropR, cropB;

        if (UseStandaloneChatWindow && !string.IsNullOrWhiteSpace(StandaloneChatWindowTitle))
        {
            hwnd = _capture.FindWindowByTitle(WeChatProcessNames, StandaloneChatWindowTitle);
            cropL = StandaloneCropLeft; cropT = StandaloneCropTop;
            cropR = StandaloneCropRight; cropB = StandaloneCropBottom;
        }
        else
        {
            hwnd = _capture.FindMainWindowHandle(WeChatProcessNames);
            cropL = CropLeft; cropT = CropTop; cropR = CropRight; cropB = CropBottom;
        }
        if (hwnd == IntPtr.Zero) return [Info("截图失败：窗口可能被最小化或遮挡")];

        if (!_ocr.IsAvailable) return [Info("系统缺少中文 OCR 语言包：设置 → 时间和语言 → 语言 → 中文 → 选项 → 安装“语言包”")];
        using var full = _capture.CaptureWindow(hwnd);
        if (full is null) return [Info("截图失败（窗口可能被最小化或遮挡）。")];
        using var cropped = _capture.Crop(full, cropL, cropT, cropR, cropB);

        // 画面变化检测:没变就直接返回上次结果,跳过 OCR
        using (var probe = ImageConvert.BitmapToMat(cropped))   // 复用 cropped 转一个 Mat
        {
            if (!_frameDetector.HasChanged(probe))
            {
                return _lastResult;   // 画面没变,沿用上次,去重服务会判定无新消息
            }
        }

        var lines = await _ocr.RecognizeAsync(cropped, cancellationToken);
        var merged = OcrLineMerger.Merge(lines); // 对同一个气泡里面的消息进行合并
        Log.Information("合并前 {Before} 行 → 合并后 {After} 段", lines.Count, merged.Count);

        IReadOnlyList<AttributedMessage> attributed;
        if (UseStandaloneChatWindow) // 独立窗口(一对一)直接用联系人名,不需要识别
        {
            attributed = merged
                .Select(l => new AttributedMessage(StandaloneChatWindowTitle, l.Text, l.Bounds))
                .ToList();
        }
        else // 群聊场景:从几何识别发送者
        {
            attributed = SenderAttributor.Attribute(merged);
        }
        // 调试:把检测框画上去再存
        if (_debug.Enabled)
        {
            _debug.Save(full, "full");
            using var annotated = _debug.DrawBoxes(cropped, merged); //画合并后的框
            _debug.Save(annotated, "cropped_with_boxes");
            // _debug.OpenFolder();
        }

        var result = attributed
            .Select(a => new { Sender = a.Sender, Text = a.Text.Trim()})
            .Where(a => IsMeaningful(a.Text))
            .Select(a => new ChatMessage
            {
                AdapterId = Id,
                SessionName = "微信",
                SenderName = a.Sender,
                Text = a.Text,
                ReceivedAt = DateTimeOffset.Now
            })
            .ToList();

        _lastResult = result;
        return result;
    }
    // 噪声过滤是“聊天适配器”的职责，留在适配器里，而不是放进通用 OCR 引擎
    private static readonly System.Text.RegularExpressions.Regex TimePattern = new(@"^\d{1,2}[:：]\d{1,2}$");
    private static readonly System.Text.RegularExpressions.Regex MeaningfulChar = new(@"[\u4e00-\u9fffA-Za-z]");
    // private static readonly HashSet<string> NoiseTokens = ["微信", "通讯录", "收藏", "朋友圈", "看一看", "搜一搜", "发送"];

    private static bool IsMeaningful(string text)
    {
        // if (string.IsNullOrWhiteSpace(text) || text.Length < 1) return false;
        if (TimePattern.IsMatch(text)) return false; //不匹配时间
        // if (!MeaningfulChar.IsMatch(text)) return false;
        // return !NoiseTokens.Contains(text);
        return !(string.IsNullOrWhiteSpace(text) || text.Length == 1);
    }

    private static ChatMessage Info(string text) => new()
    {
        AdapterId = "wechat",
        SessionName = "微信",
        SenderName = "系统",
        Text = text,
        ReceivedAt = DateTimeOffset.Now
    };

    public Task<SendResult> SendMessageAsync(string sessionName, string message, CancellationToken cancellationToken = default)
    {
        var mainWindow = _windowLocator.FindMainWindow(_automation);
        if (mainWindow is null)
        {
            return Task.FromResult(
                SendResult.Fail("wechat-uia", "未找到微信窗口。"));
        }

        return _sender.SendMessageAsync(
            mainWindow,
            sessionName,
            message,
            cancellationToken);
    }

    public bool CanFindTargetWindow()
    {
        var hwnd = UseStandaloneChatWindow && !string.IsNullOrWhiteSpace(StandaloneChatWindowTitle)
          ? _capture.FindWindowByTitle(WeChatProcessNames, StandaloneChatWindowTitle)
          : _capture.FindMainWindowHandle(WeChatProcessNames);
        return hwnd != IntPtr.Zero;
    }

    public void Dispose()
    {
        _automation.Dispose();
        (_ocr as IDisposable)?.Dispose();
    }
}