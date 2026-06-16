using FlaUI.UIA3;
using System.Drawing;
using HideYourChat.App.Core;
using HideYourChat.App.Imaging;
using Serilog;

namespace HideYourChat.App.Adapters.WeChat;

public sealed class WeChatAdapter : IChatAdapter, IDisposable
{
    private readonly ScreenCapture _capture = new();
    private readonly CaptureDebugSink _debug = new();
    // private readonly IOcrEngine _ocr = new WindowsOcrEngine();
    private readonly IOcrEngine _ocr = new PaddleOcrEngine();
    private static readonly string[] WeChatProcessNames = ["Weixin", "WeChat", "微信", "WXWork"];

    // 聊天裁剪区域
    public double CropLeft { get; set; } = 0.35;
    public double CropTop { get; set; } = 0.09;
    public double CropRight { get; set; } = 1.0;
    public double CropBottom { get; set; } = 0.82;
    // 标题条由主 crop 派生:左对齐、紧贴聊天区上方,宽度取聊天区左侧 60%(避开右上角图标)
    private double TitleCropLeft => CropLeft;
    private double TitleCropRight => CropLeft + (CropRight - CropLeft) * 0.6;
    private double TitleCropBottom => CropTop;
    private double TitleCropTop => Math.Max(0, CropTop - TitleBarHeight);
    private double TitleBarHeight { get; set; } = 0.045; // 唯一可能需微调的:标题条高度
    // 独立窗口裁剪比例
    private const double StandaloneCropLeft = 0.02, StandaloneCropTop = 0.057, StandaloneCropRight = 0.89, StandaloneCropBottom = 0.90;

    private readonly FrameChangeDetector _titleFrameDetector = new();
    private string _lastSessionTitle = "";
    private static readonly System.Text.RegularExpressions.Regex GroupCountSuffix = new(@"\s*[\(（]\d+[\)）]\s*$");

    // 是否监听独立聊天窗口(而不是主窗口)
    public bool UseStandaloneChatWindow { get; set; } = false;
    public string CurrentSessionName {get; private set;} = "微信";
    public string StandaloneChatWindowTitle { get; set; } = ""; // 要监听的联系人名

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
        // if (_debug.Enabled)
        // {
        //     _debug.Save(full, "full");
        //     using var annotated = _debug.DrawBoxes(cropped, merged); //画合并后的框
        //     _debug.Save(annotated, "cropped_with_boxes");
        //     // _debug.OpenFolder();
        // }

        // 根据场景和归属方确定senderName
        var attributed = SenderAttributor.Attribute(merged); // 先做群聊昵称归属:昵称行被"消化"掉,只剩正文段

        // 会话标题:独立窗口=已知联系人名(免 OCR);主窗口=OCR 顶部标题条
        string sessionTitle = UseStandaloneChatWindow
            ? StandaloneChatWindowTitle
            : await ReadMainSessionTitleAsync(full, cancellationToken);

        // 判断是否为群聊
        bool isGroup = attributed.Any(m => !string.IsNullOrWhiteSpace(m.Sender));

        var result = new List<ChatMessage>();

        foreach (var msg in attributed)
        {
            var text = msg.Text.Trim();
            if (!IsMeaningful(text)) continue;

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
    private static readonly System.Text.RegularExpressions.Regex UnreadDivider = new(@"^\d+\s*条新消息$");
    private static bool IsMeaningful(string text)
    {
        // if (string.IsNullOrWhiteSpace(text) || text.Length < 1) return false;
        if (TimePattern.IsMatch(text)) return false; //不匹配时间
        // if (!MeaningfulChar.IsMatch(text)) return false;
        // return !NoiseTokens.Contains(text);
        if (UnreadDivider.IsMatch(text)) return false;
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

    public async Task<SendResult> SendMessageAsync(string sessionName, string message, CancellationToken cancellationToken = default)
    {
        // 和读取一致:监听独立窗口就发独立窗口,否则发主窗口当前会话
        IntPtr hwnd = UseStandaloneChatWindow && !string.IsNullOrWhiteSpace(StandaloneChatWindowTitle)
            ? _capture.FindWindowByTitle(WeChatProcessNames, StandaloneChatWindowTitle)
            : _capture.FindMainWindowHandle(WeChatProcessNames);

        if (hwnd == IntPtr.Zero)
            return SendResult.Fail("wechat-paste", "未找到微信窗口，请确认微信已打开");

        double rx = UseStandaloneChatWindow ? 0.5 : (CropLeft + CropRight) / 2.0;
        double ry = 0.92;

        return await _sender.SendAsync(hwnd, message, rx, ry);
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
        using var titleStrip = _capture.Crop(full, TitleCropLeft, TitleCropTop, TitleCropRight, TitleCropBottom);
        using (var probe = ImageConvert.BitmapToMat(titleStrip))
        {
            if (!_titleFrameDetector.HasChanged(probe)) return _lastSessionTitle; // 标题没变
        }
        if (_debug.Enabled) _debug.Save(titleStrip, "title_strip");

        var titleLines = await _ocr.RecognizeAsync(titleStrip, ct);

        // 取最靠上，第一条非空文字当标题
        var title = titleLines
            .OrderBy(l => l.Bounds.Top)
            .Select(l => l.Text.Trim())
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? "";

        _lastSessionTitle = title;
        return title;
    }

    /// <summary>按当前裁剪比例试截一张:返回聊天区"带 OCR 框"预览图 + 识别到的会话标题。仅供 UI 预览</summary>
    public async Task<(System.Drawing.Bitmap? Preview, string Title)> CaptureCropPreviewAsync(CancellationToken ct = default)
    {
        IntPtr hwnd;
        double cropL, cropT, cropR, cropB;
        if (UseStandaloneChatWindow && !string.IsNullOrWhiteSpace(StandaloneChatWindowTitle))
        {
            hwnd = _capture.FindWindowByTitle(WeChatProcessNames, StandaloneChatWindowTitle);
            cropL = StandaloneCropLeft; cropT = StandaloneCropTop; cropR = StandaloneCropRight; cropB = StandaloneCropBottom;
        }
        else
        {
            hwnd = _capture.FindMainWindowHandle(WeChatProcessNames);
            cropL = CropLeft; cropT = CropTop; cropR = CropRight; cropB = CropBottom;
        }
        if (hwnd == IntPtr.Zero || !_ocr.IsAvailable) return (null, "");
        using var full = _capture.CaptureWindow(hwnd);
        if (full is null) return (null, "");

        var cropped = _capture.Crop(full, cropL, cropT, cropR, cropB);
        var lines = await _ocr.RecognizeAsync(cropped, ct);
        var merged = OcrLineMerger.Merge(lines);
        var annotated = _debug.DrawBoxes(cropped, merged); // 画框,返回新图(调用方负责 Dispose)
        cropped.Dispose();
        // 标题：仅主窗口模式需要单独识别标题
        string title = "";
        if (!UseStandaloneChatWindow)
        {
            using var titleStrip = _capture.Crop(full, TitleCropLeft, TitleCropTop, TitleCropRight, TitleCropBottom);
            var tlines = await _ocr.RecognizeAsync(titleStrip, ct);
            title = tlines.OrderBy(l => l.Bounds.Top)
                    .Select(l => l.Text.Trim())
                    .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? "";
        }
        return (annotated, title);
    }

    // public string DumpUiaTree()
    // {
    //     using var automation = new UIA3Automation();
    //     var win = new WeChatWindowLocator().FindMainWindow(automation);
    //     if(win is null) return "未找到微信主窗口";
    //     return QQ.UiaTreeDumper.Dump(win);
    // }

    public void Dispose()
    {
        (_ocr as IDisposable)?.Dispose();
    }
}