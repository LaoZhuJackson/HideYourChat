namespace HideYourChat.App.Core;

public sealed class SendResult
{
    public bool Success {get; init;}
    public string Strategy {get; init;} = "";
    public string? ErrorMessage {get; init;} //?表示可以为null
    public static SendResult Ok(string strategy)
    {
        return new SendResult
        {
            Success = true,
            Strategy = strategy
        };
    }
    public static SendResult Fail(string strategy, string errorMessage)
    {
        return new SendResult
        {
            Success = false,
            Strategy = strategy,
            ErrorMessage = errorMessage
        };
    }
}