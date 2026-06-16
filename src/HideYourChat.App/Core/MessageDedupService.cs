using Serilog;

namespace HideYourChat.App.Core;

public sealed class MessageDedupService
{
    private readonly HashSet<string> _seenKeys = new();
    private readonly Queue<string> _keyOrder = new();
    private readonly object _lock = new();

    private readonly int _maxKeys;

    public MessageDedupService(int maxKeys = 50)
    {
        _maxKeys = maxKeys;
    }

    public IReadOnlyList<ChatMessage> FilterNewMessages(IEnumerable<ChatMessage> messages)
    {
        var result = new List<ChatMessage>();

        lock (_lock)
        {
            foreach (var message in messages)
            {
                var key = message.CreateDedupKey();
                Log.Debug("dedup key = {Key}", key);
                if (_seenKeys.Contains(key))
                {
                    continue;
                }

                _seenKeys.Add(key);
                _keyOrder.Enqueue(key);
                result.Add(message);

                while (_keyOrder.Count > _maxKeys)
                {
                    var oldKey = _keyOrder.Dequeue();
                    _seenKeys.Remove(oldKey);
                }
            }
        }
        return result;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _seenKeys.Clear();
            _keyOrder.Clear();
        }
    }
}