using System.Windows.Threading;
using HideYourChat.App.Adapters.QQ;
using HideYourChat.App.Adapters.WeChat;
using HideYourChat.App.Overlay;
using Application = System.Windows.Application;

namespace HideYourChat.App.Core;

/// <summary>
/// 聊天运行时服务 — 管理 adapter、monitor、overlay、发送 的完整生命周期。
/// 把 MainWindow 里 ~350 行逻辑抽到此处，MainWindow 只负责 UI 绑定。
/// </summary>
public sealed class ChatRuntimeService : IDisposable
{
    private IChatAdapter _adapter = null!;
    private readonly MessageDedupService _dedupService = new();
    private ChatMonitorService _monitorService = null!;
    private OverlayWindow? _overlayWindow;
    private readonly Dispatcher _dispatcher;
    private bool _disposed;
    private readonly SemaphoreSlim _sendLock = new(1, 1); // 一次只能允许一个线程进入的锁

    // ── 暴露给 MainWindow 的状态 ──
    public bool IsRunning => _monitorService?.IsRunning ?? false;
    public bool IsQQWindowHidden => (_adapter as QQAdapter)?.IsWindowHidden ?? false;
    public string CurrentSessionName => _adapter?.CurrentSessionName ?? "";
    public IChatAdapter Adapter => _adapter;  // 微信试截图等场景用

    // ── 事件，MainWindow 订阅来更新状态栏 / 锁定配置面板 ──
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<bool>? ConfigLockChanged;

    public ChatRuntimeService()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        ReBuildAdapter("QQ");
    }

    // ═══════════════════════════════════════════
    //  Adapter
    // ═══════════════════════════════════════════

    /// <summary>切换聊天应用。运行中禁止切换。</summary>
    public void SwitchAdapter(string appName)
    {
        if (IsRunning) return;
        ReBuildAdapter(appName);
        StatusChanged?.Invoke(this, $"已切换到 {appName} 适配器");
    }

    private void ReBuildAdapter(string appName)
    {
        (_adapter as IDisposable)?.Dispose();
        _adapter = appName switch
        {
            "微信" => new WeChatAdapter(),
            "QQ" => new QQAdapter(),
            _ => new WeChatAdapter()
        };
        _monitorService = new ChatMonitorService(_adapter, _dedupService);
        _monitorService.NewMessagesReceived += OnNewMessages;
        _monitorService.ErrorOccurred += OnError;
    }

    // ═══════════════════════════════════════════
    //  启动 / 停止
    // ═══════════════════════════════════════════

    /// <summary>启动监听。灌入当前 UI 上的配置。</summary>
    public bool Start(AppSettings settings, double bgOpacity, double textOpacity)
    {
        if (IsRunning)
        {
            StatusChanged?.Invoke(this, "监听已在运行中");
            return false;
        }

        // WeChat 配置
        if (_adapter is WeChatAdapter wechat)
        {
            if (settings.UseStandaloneChatWindow)
            {
                if (string.IsNullOrWhiteSpace(settings.ContactName))
                {
                    StatusChanged?.Invoke(this, "已勾选独立窗口模式，请先输入联系人名称");
                    return false;
                }
                wechat.UseStandaloneChatWindow = true;
                wechat.StandaloneChatWindowTitle = settings.ContactName;
            }
            else
            {
                wechat.UseStandaloneChatWindow = false;
                wechat.StandaloneChatWindowTitle = "";
                wechat.CropLeft = settings.WeChatCropLeft;
                wechat.CropTop = settings.WeChatCropTop;
                wechat.CropRight = settings.WeChatCropRight;
                wechat.CropBottom = settings.WeChatCropBottom;
            }

            if (!wechat.CanFindTargetWindow())
            {
                var hint = settings.UseStandaloneChatWindow
                    ? $"找不到标题含「{settings.ContactName}」的窗口"
                    : "找不到微信主窗口，请确认微信已打开";
                StatusChanged?.Invoke(this, hint);
                return false;
            }
        }

        // QQ 配置
        if (_adapter is QQAdapter qq)
        {
            qq.HideMode = (QQWindowMover.QQHideMode)settings.QQHideModeIndex;
            qq.HideWindow();
        }

        EnsureOverlayWindow(settings);
        _overlayWindow!.SetBackgroundOpacity(bgOpacity);
        _overlayWindow.SetTextOpacity(textOpacity);
        _overlayWindow.Show();

        _monitorService.Start(TimeSpan.FromMilliseconds(1500));
        ConfigLockChanged?.Invoke(this, false);
        StatusChanged?.Invoke(this, $"{_adapter.DisplayName} 监听已启动");
        return true;
    }

    /// <summary>停止监听</summary>
    public async Task StopAsync()
    {
        await _monitorService.StopAsync();
        (_adapter as QQAdapter)?.RestoreWindow();
        ConfigLockChanged?.Invoke(this, true);
        StatusChanged?.Invoke(this, "监听已停止");
    }

    // ═══════════════════════════════════════════
    //  Overlay
    // ═══════════════════════════════════════════

    public void ShowOverlay()
    {
        EnsureOverlayWindow(null);
        _overlayWindow?.Show();
    }

    public void HideOverlay() => _overlayWindow?.Hide();

    public void SetBackgroundOpacity(double opacity) => _overlayWindow?.SetBackgroundOpacity(opacity);

    public void SetTextOpacity(double opacity) => _overlayWindow?.SetTextOpacity(opacity);

    public void ApplyTheme(bool dark) => _overlayWindow?.ApplyTheme(dark);

    public (double Left, double Top, double Width, double Height) GetOverlayBounds()
    {
        if (_overlayWindow is null) return (double.NaN, double.NaN, 480, 380);
        return _overlayWindow.GetPersistedBounds();
    }

    private void EnsureOverlayWindow(AppSettings? settings)
    {
        if (_overlayWindow is not null) return;

        _overlayWindow = new OverlayWindow();
        _overlayWindow.SendRequested += OnSendRequested;
        _overlayWindow.Closed += (_, _) =>
        {
            if (_overlayWindow is not null)
                _overlayWindow.SendRequested -= OnSendRequested;
            _overlayWindow = null;
        };

        if (settings is not null)
        {
            _overlayWindow.ApplyPersistedBounds(
                settings.OverlayLeft, settings.OverlayTop,
                settings.OverlayWidth, settings.OverlayHeight);
            _overlayWindow.MaxMessages = settings.MaxMessageCount; // 设置最大消息上限
        }
    }

    // ═══════════════════════════════════════════
    //  QQ 窗口显隐
    // ═══════════════════════════════════════════

    public void ToggleQQWindow(int hideModeIndex)
    {
        if (_adapter is not QQAdapter qq) return;
        if (qq.IsWindowHidden)
        {
            qq.RestoreWindow();
            StatusChanged?.Invoke(this, "QQ 窗口已恢复");
        }
        else
        {
            qq.HideMode = (QQWindowMover.QQHideMode)hideModeIndex;
            qq.HideWindow();
            StatusChanged?.Invoke(this, "QQ 窗口已隐藏");
        }
    }

    // ═══════════════════════════════════════════
    //  消息事件桥接
    // ═══════════════════════════════════════════

    private void OnNewMessages(object? sender, IReadOnlyList<ChatMessage> messages)
    {
        _dispatcher.Invoke(() =>
        {
            EnsureOverlayWindow(null);
            _overlayWindow?.AddMessages(messages);

            if (messages.Count > 0 && !string.IsNullOrEmpty(messages[0].SessionName))
                _overlayWindow?.SetReplyStatus($"回复到：{messages[0].SessionName}");

            StatusChanged?.Invoke(this, $"收到 {messages.Count} 条新消息，{DateTime.Now:HH:mm:ss}");
        });
    }

    private void OnError(object? sender, string error)
    {
        _dispatcher.Invoke(() =>
        {
            StatusChanged?.Invoke(this, $"监听异常：{error}");
        });
    }

    // ═══════════════════════════════════════════
    //  发送
    // ═══════════════════════════════════════════

    private async void OnSendRequested(object? sender, string message)
    {
        if (!await _sendLock.WaitAsync(0))
        {
            // 上一个包还在发送，忽略这次请求
            _dispatcher.Invoke(() =>
                _overlayWindow?.SetReplyStatus("上一条消息仍在发送中…"));
            return;
        }
        try
        {
            var result = await _adapter.SendMessageAsync(CurrentSessionName, message);

            if (result.Success)
            {
                _overlayWindow?.SetReplyStatus("发送成功");
                StatusChanged?.Invoke(this, $"发送成功，{DateTime.Now:HH:mm:ss}");
            }
            else
            {
                _overlayWindow?.SetReplyStatus($"发送失败：{result.ErrorMessage}");
                StatusChanged?.Invoke(this, $"发送失败：{result.ErrorMessage}");
            }

            // 发送后抢回焦点
            await Task.Delay(150);
            _dispatcher.Invoke(() =>
            {
                _overlayWindow?.Activate();
                _overlayWindow?.FocusReplayInput();
            });
        }
        finally
        {
            _sendLock.Release();
        }


    }

    // ═══════════════════════════════════════════
    //  生命周期
    // ═══════════════════════════════════════════

    public async Task ShutdownAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _monitorService.StopAsync();
        (_adapter as QQAdapter)?.RestoreWindow();
        (_adapter as IDisposable)?.Dispose();
        _overlayWindow?.Close();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        (_adapter as IDisposable)?.Dispose();
        _overlayWindow?.Close();
    }
}
