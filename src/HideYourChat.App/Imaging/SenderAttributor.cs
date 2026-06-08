using System.Drawing;
using Serilog;

namespace HideYourChat.App.Imaging;

/// <summary>带发送者归属的消息段。发送者识别失败时 Sender 为空字符串。</summary>
public sealed record AttributedMessage(string Sender, string Text, System.Drawing.RectangleF Bounds);

/// <summary>
/// 在合并后的消息段上识别发送者。
/// 群聊里昵称单独成行,字号比正文小、位置略靠左、下方紧跟正文。
/// 识别出昵称行后,作为后续正文段的发送者,直到下一个昵称出现(处理连发)。
/// 纯几何启发式,微信/QQ 群聊通用。
/// </summary>
public static class SenderAttributor
{
    public static IReadOnlyList<AttributedMessage> Attribute(
        IReadOnlyList<OcrLine> segments,
        double nameHeightRatio = 0.8,   // 昵称行高 < 下方正文行高 × 此值,才算昵称
        int nameMaxLength = 12         // 昵称最长字数,超过视为正文
    )
    {
        var result = new List<AttributedMessage>();
        string currentSender = "";
        for(int i = 0; i<segments.Count; i++)
        {
            var seg = segments[i];
            // 往下看一段,作为"正文参照"。最后一段没有下文,无法当昵称。
            bool looksLikeName = false;
            if(i+1 < segments.Count)
            {
                var next = segments[i+1];
                double heightRatio = seg.Bounds.Height / next.Bounds.Height;
                double verticalGap = next.Bounds.Top - seg.Bounds.Bottom;

                looksLikeName = 
                    heightRatio < nameHeightRatio &&            // 比下一段矮(昵称小字)
                    seg.Text.Length <= nameMaxLength &&         // 文本短
                    verticalGap >= 0 &&                          // 在下一段上方
                    verticalGap < seg.Bounds.Height * 2.0 &&     // 和下方正文挨得不太远
                    seg.Bounds.Left <= next.Bounds.Left + 5;     // 左边缘不比正文更靠右
            }
            if (looksLikeName)
            {
                currentSender = seg.Text; // 更新当前发送者,这一段(昵称)本身不作为消息输出
                Log.Debug("识别为发送者昵称：{Sender}", currentSender);
            }
            else
            {
                result.Add(new AttributedMessage(currentSender, seg.Text, seg.Bounds));
            }
        }
        return result;
    }
}