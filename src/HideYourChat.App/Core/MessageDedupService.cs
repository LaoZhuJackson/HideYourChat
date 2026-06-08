namespace HideYourChat.App.Core;

public sealed class MessageDedupService
{
    private readonly HashSet<string> _seenKeys = new(); //存储看过的消息key
    private readonly Queue<string> _keyOrder = new();

    private readonly int _maxKeys;

    public MessageDedupService(int maxKeys = 50)
    {
        _maxKeys = maxKeys;
    }

    public IReadOnlyList<ChatMessage> FilterNewMessages(IEnumerable<ChatMessage> messages)
    {
        var result = new List<ChatMessage>();
        foreach(var message in messages)
        {
            var key = message.CreateDedupKey(); // 将消息转换为唯一key，用于后续去重

            if (_seenKeys.Contains(key))
            {
                continue;
            }

            _seenKeys.Add(key);
            _keyOrder.Enqueue(key);
            result.Add(message);

            while(_keyOrder.Count > _maxKeys)
            {
                var oldKey = _keyOrder.Dequeue();
                _seenKeys.Remove(oldKey);
            }
        }
        return result;
    }

    public void Clear()
    {
        _seenKeys.Clear();
        _keyOrder.Clear();
    }
}