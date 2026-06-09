
using System.Windows.Shapes;
using FlaUI.UIA3;
using System.Drawing;
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
    private const double CropLeft = 0.35, CropTop = 0.09, CropRight = 1.0, CropBottom = 0.82;
    // 单聊联系人名称裁剪区域
    private const double TitleCropLeft = 0.35, TitleCropTop = 0.045, TitleCropRight = 0.72, TitleCropBottom = 0.09;
    private readonly FrameChangeDetector _titleFrameDetector = new();
    private string _lastSessionTitle = "";
    private static readonly System.Text.RegularExpressions.Regex GroupCountSuffix = new(@"\s*[\(（]\d+[\)）]\s*$");

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
        // Log.Information("合并前 {Before} 行 → 合并后 {After} 段", lines.Count, merged.Count);
        // 调试:把检测框画上去再存
        if (_debug.Enabled)
        {
            _debug.Save(full, "full");
            using var annotated = _debug.DrawBoxes(cropped, merged); //画合并后的框
            _debug.Save(annotated, "cropped_with_boxes");
            // _debug.OpenFolder();
        }

        // 根据场景和归属方确定senderName
        var attributed = SenderAttributor.Attribute(merged); // 先做群聊昵称归属:昵称行被"消化"掉,只剩正文段
        
        // 会话标题:独立窗口=已知联系人名(免 OCR);主窗口=OCR 顶部标题条
        string sessionTitle = UseStandaloneChatWindow
            ? StandaloneChatWindowTitle
            : await ReadMainSessionTitleAsync(full, cancellationToken);
        
        // 判断是否为群聊
        bool isGroup = attributed.Any(m => !string.IsNullOrWhiteSpace(m.Sender));
        
        var result = new List<ChatMessage>();

        foreach(var msg in attributed)
        {
            var text = msg.Text.Trim();
            if(!IsMeaningful(text)) continue;

            // 判断左右
            var color = BubbleColorSampler.Sample(cropped, msg.Bounds);
            var side = BubbleClassifier.Classify(color);

            // 发送人昵称
            string senderName = side switch
            {
                MessageSide.Mine => "我",
                MessageSide.Other => !string.IsNullOrWhiteSpace(msg.Sender) 
                    ? msg.Sender  // 群聊:OCR 到的群成员昵称
                    : (string.IsNullOrWhiteSpace(sessionTitle) 
                    ? "对方" 
                    : sessionTitle), // 单聊:联系人名
                _ => ""
            };
            // 会话名：群聊="群名*群成员名",单聊="联系人名"
            string sessionName = isGroup
                ? $"{(string.IsNullOrWhiteSpace(sessionTitle) ? "群聊" : sessionTitle)}"
                : (string.IsNullOrWhiteSpace(sessionTitle) ? "微信" : sessionTitle);

            sessionName = GroupCountSuffix.Replace(sessionName, "").Trim();

            result.Add(new ChatMessage
            {
                AdapterId = Id,
                SessionName = sessionName,
                SenderName = senderName,
                Text = text,
                ReceivedAt = DateTimeOffset.Now
            });
        }

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

    /// <summary>
  /// OCR 主窗口聊天区顶部的当前会话名(单聊时即对方联系人名)。
  /// 标题条没变就用缓存,标题区很小、又带变化检测,开销可忽略。
  /// </summary>
  private async Task<string> ReadMainSessionTitleAsync(Bitmap full, CancellationToken ct)
    {
        using var titleStrip = _capture.Crop(full, TitleCropLeft, TitleCropTop,TitleCropRight,TitleCropBottom);
        using (var probe = ImageConvert.BitmapToMat(titleStrip))
        {
            if(!_titleFrameDetector.HasChanged(probe)) return _lastSessionTitle; // 标题没变
        }
        if(_debug.Enabled) _debug.Save(titleStrip, "title_strip");

        var titleLines = await _ocr.RecognizeAsync(titleStrip, ct);

        // 取最靠上，第一条非空文字当标题
        var title = titleLines
            .OrderBy(l => l.Bounds.Top)
            .Select(l => l.Text.Trim())
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? "";

        _lastSessionTitle = title;
        return title;
    }

    public void Dispose()
    {
        _automation.Dispose();
        (_ocr as IDisposable)?.Dispose();
    }
}