using System.Drawing;

namespace HideYourChat.App.Adapters;

/// <summary>消息归属方</summary>
public enum MessageSide { Unknown, Mine, Other }

/// <summary>
/// 根据气泡颜色判断消息是"我"还是"对方"发的。
/// 关键:用"绿色度"(G 通道是否突出)而非绝对颜色,
/// 这样微信切换深色模式后(白气泡变深灰)依然有效。
/// </summary>
public static class BubbleClassifier
{
    private const bool GreenIsMine = true;
    /// <param name="bubbleColor">BubbleColorSampler 采到的气泡色。</param>
    public static MessageSide Classify(Color bubbleColor)
    {
        int r = bubbleColor.R, g = bubbleColor.G, b = bubbleColor.B;
        // 绿色度:G 明显高于 R 和 B。阈值 12 是经验值,可调。
        const int greenMargin = 12;
        bool isGreen = g > r + greenMargin && g > b + greenMargin;

        if(!isGreen) return MessageSide.Other;
        return MessageSide.Mine;
    }
}