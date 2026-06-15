using FlaUI.UIA3;
using HideYourChat.App.Core;
using Serilog;
using FlaUI.Core.AutomationElements;

namespace HideYourChat.App.Adapters.QQ;

public sealed class QQAdapter : IChatAdapter, IDisposable
{
    private readonly UIA3Automation _automation = new();
    private readonly QQWindowLocator _windowLocator = new();
    private readonly QQReader _reader = new();
    private readonly QQSender _sender = new();

    private bool _dumpMode = false; // true是只导出树不读消息，调试用

    private IntPtr _hwnd;
    private QQWindowMover.Pos? _savedPos; //隐藏前的原位
    public bool IsWindowHidden { get; private set; }
    public QQWindowMover.QQHideMode HideMode { get; set; } = QQWindowMover.QQHideMode.Auto;

    public string Id => "qq";
    public string DisplayName => "QQ";

    public Task<IReadOnlyList<ChatMessage>> ReadLatestMessagesAsync(CancellationToken cancellationToken = default)
    {
        var mainWindow = _windowLocator.FindMainWindow(_automation);
        if (mainWindow is null) return Task.FromResult<IReadOnlyList<ChatMessage>>(
            [Info("未找到 QQ 窗口,请确认 QQ 已登录并打开主面板(非最小化到托盘)")]
        );
        // 第一次：导出UIA树，后续可注释或者删除这段
        if (_dumpMode)
        {
            var path = UiaTreeDumper.Dump(mainWindow);
            Log.Information("QQ UIA 树已导出: {Path}", path);
            return Task.FromResult<IReadOnlyList<ChatMessage>>([Info($"UIA 树已导出到 {path}")]);
        }
        var title = ExtractSessionTitle(mainWindow);
        bool isGroup = IsGroupChat(mainWindow);
        var messages = _reader.ReadLatestMessages(mainWindow, title, isGroup);
        return Task.FromResult(messages);
    }

    public bool CanFindTargetWindow() => _windowLocator.FindMainWindow(_automation) is not null;

    public Task<SendResult> SendMessageAsync(string sessionName, string message, CancellationToken cancellationToken = default)
    {
        var win = _windowLocator.FindMainWindow(_automation);
        if(win is null)
            return Task.FromResult(SendResult.Fail("qq-uia", "未找到 QQ 窗口"));
        // 发到当前监视的会话(QQ 当前打开哪个会话就发哪个),sessionName 暂时忽略
        return Task.FromResult(_sender.Send(win, message));
    }

    private static ChatMessage Info(string text) => new()
    {
        AdapterId = "qq",
        SessionName = "QQ",
        SenderName = "系统",
        Text = text,
        ReceivedAt = DateTimeOffset.Now
    };

    /// <summary>是否群聊:树里存在"群成员列表"窗口即判定为群。比数发送人可靠。</summary>
    private static bool IsGroupChat(AutomationElement mainWindow)
        => mainWindow.FindFirstDescendant(cf => cf.ByName("群成员列表")) is not null;

    /// <summary>会话标题:去掉窗口名的"等N个会话"后缀。</summary>
    private static string ExtractSessionTitle(AutomationElement mainWindow)
    {
        string name;
        try { name = mainWindow.Properties.Name.ValueOrDefault ?? ""; } catch { return ""; }

        var m = System.Text.RegularExpressions.Regex.Match(name, @"^(.*?)…?等\d+个会话$");
        return m.Success ? m.Groups[1].Value : name;
    }

    /// <summary>记住当前位置,挪到屏外。已隐藏则跳过(避免把屏外坐标当原位存进去)。</summary>
    public void HideWindow()
    {
        if(IsWindowHidden) return;
        _hwnd = _windowLocator.FindMainWindowHandle();
        if(_hwnd == IntPtr.Zero) return; // QQ没开

        _savedPos = QQWindowMover.GetPosition(_hwnd); // 存入原位
        QQWindowMover.Stash(_hwnd, HideMode);
        QQWindowMover.SetTopMost(_hwnd, true);
        IsWindowHidden = true;
    }

    public void RestoreWindow()
    {
        if(!IsWindowHidden) return;
        if(_hwnd != IntPtr.Zero && _savedPos is { } p) // 如果 _savedPos 不为 null，则赋值给 p
            QQWindowMover.MoveTo(_hwnd, p.X, p.Y);
        QQWindowMover.SetTopMost(_hwnd, false);
        IsWindowHidden = false;
    }

    public void Dispose() => _automation.Dispose();
}