namespace HideYourChat.App.Core;

public sealed class ChatMessage
{
    public string AdapterId {get; init;} = "mock";
    public string SessionName{get; init;} = "";
    public string SenderName { get; init; } = "";
    public string Text { get; init; } = "";
    public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.Now;
    public string DedupSalt {get; init;} = ""; //仅参与去重，不显示；用于区分重复内容如"[图片]"

    public string CreateDedupKey()
    {
        // MVP 阶段先用会话名 + 发送人 + 文本做去重。
        // 后续真实适配器可以加入消息 ID、OCR 区域、时间窗口等信息。
        return $"{AdapterId}|{SessionName}|{SenderName}|{Text}|{DedupSalt}".Trim();
    }

    public override string ToString()
    {
        return $"[{SessionName}] {SenderName}: {Text}";
    }
}