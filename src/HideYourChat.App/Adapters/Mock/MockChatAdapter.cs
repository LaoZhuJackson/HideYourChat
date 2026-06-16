using HideYourChat.App.Core;
namespace HideYourChat.App.Adapters.Mock;

public sealed class MockChatAdapter : IChatAdapter
{
    private readonly object _lock = new();
    private readonly List<ChatMessage> _history = new();
    private int _index;
    private readonly string[] _senders = [
        "张三",
        "李四",
        "甲",
        "乙",
        "丙"
    ];
    private readonly string[] _texts =
    [
        "你现在方便看一下这个问题吗？",
        "这个功能感觉可以先做一个 MVP。",
        "我刚刚发了一个新的需求。",
        "今天先把悬浮窗跑起来就很不错了。",
        "这个消息是 Mock 适配器生成的。",
        "后面可以把这里替换成微信或 QQ 适配器。",
        "先验证链路，再优化架构。"
    ];

    public string Id => "mock";
    public string DisplayName => "Mock Chat";
    public string CurrentSessionName => "测试群聊";
    public Task<IReadOnlyList<ChatMessage>> ReadLatestMessagesAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock) // 确保多线程环境下的数据一致性
        {
            var sender = _senders[_index % _senders.Length];
            var text = _texts[_index % _texts.Length];

            _index++;

            _history.Add(new ChatMessage
            {
                AdapterId = Id,
                SessionName = "测试群聊",
                SenderName = sender,
                Text = $"{text} #{_index}",
                ReceivedAt = DateTimeOffset.Now
            });

            // 模拟真实聊天窗口：每次读取都会读到最近几条消息。
            // 去重服务会负责只显示新消息。
            return Task.FromResult<IReadOnlyList<ChatMessage>>(_history.TakeLast(5).ToList());
        }
    }

    public Task<SendResult> SendMessageAsync(string sessionName, string message, CancellationToken cancellationToken = default)
    {
        if(string.IsNullOrWhiteSpace(message)) return Task.FromResult(SendResult.Fail("mock","消息内容不能为空"));

        lock (_lock)
        {
            _history.Add(new ChatMessage
            {
                AdapterId = Id,
                SessionName = string.IsNullOrWhiteSpace(sessionName) ? "测试群聊" : sessionName,
                SenderName = "我",
                Text = message,
                ReceivedAt = DateTimeOffset.Now
            });
        }

        return Task.FromResult(SendResult.Ok("mock"));
    }
}