namespace HideYourChat.App.Core;

public interface IChatAdapter
{
    string Id {get;}
    string DisplayName {get;}
    Task<IReadOnlyList<ChatMessage>> ReadLatestMessagesAsync(CancellationToken cancellationToken = default);
    Task<SendResult> SendMessageAsync(string sessionName, string message, CancellationToken cancellationToken = default);
}