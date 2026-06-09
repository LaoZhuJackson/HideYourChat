using System.Drawing;

namespace HideYourChat.App.Imaging;

/// <summary>
/// 从聊天截图里采样消息气泡的背景色。
/// 优先采"文字左侧的气泡内边距"(无文字、纯背景),失败则回退区域中位数。
/// </summary>
public static class BubleColorSampler
{
    
}