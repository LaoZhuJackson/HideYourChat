namespace HideYourChat.App.Core;

public sealed class ChatMonitorService
{
    private readonly IChatAdapter _adapter;
    private readonly MessageDedupService _dedupService; //取消令牌的源

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public ChatMonitorService(IChatAdapter adapter, MessageDedupService dedupService)
    {
        _adapter = adapter;
        _dedupService = dedupService;
    }

    public bool IsRunning => _cts is not null;
    public event EventHandler<IReadOnlyList<ChatMessage>>? NewMessagesReceived; // event回调
    public event EventHandler<string>? ErrorOccurred;

    public void Start(TimeSpan interval)
    {
        if(_cts is not null) return;

        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(()=> RunLoopAsync(interval,_cts.Token));
    }

    public async Task StopAsync()
    {
        if(_cts is null) return;

        var cts = _cts;
        _cts = null;

        cts.Cancel();

        try
        {
            if(_loopTask is not null) await _loopTask;
        }
        catch(OperationCanceledException)
        {
            // 正常停止
        }
        finally
        {
            cts.Dispose();
            _loopTask = null;
        }
    }

    private async Task RunLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var messages = await _adapter.ReadLatestMessagesAsync(cancellationToken);
                var newMessages = _dedupService.FilterNewMessages(messages);

                if(newMessages.Count > 0) NewMessagesReceived?.Invoke(this, newMessages);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex.Message);
            }

            await Task.Delay(interval, cancellationToken);
        }
    }
}