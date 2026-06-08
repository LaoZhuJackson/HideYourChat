using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using HideYourChat.App.Core;

namespace HideYourChat.App.Adapters.WeChat;

public sealed class WeChatReader
{
    public IReadOnlyList<ChatMessage> ReadLatestMessages(Window mainWindow)
    {
        var texts = ReadVisibleTexts(mainWindow);

        if (texts.Count == 0) return [];
        // MVP 阶段先不区分发送人、时间、消息气泡。
        // 先把 UIA 能读到的最后几段文本当作当前会话消息
        return texts.Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct()
            .TakeLast(8)
            .Select(texts => new ChatMessage
            {
                AdapterId = "wechat",
                SessionName = GetSessionName(mainWindow),
                SenderName = "微信",
                Text = texts,
                ReceivedAt = DateTimeOffset.Now
            }).ToList();
    }

    private static IReadOnlyList<string> ReadVisibleTexts(Window mainWindow)
    {
        var result = new List<string>();

        try
        {
            var descendants = mainWindow.FindAllDescendants();
            foreach ( var element in descendants )
            {
                try
                {
                    var controlType = element.ControlType;
                    if(controlType != ControlType.Text &&
                        controlType != ControlType.Edit &&
                        controlType != ControlType.Document &&
                        controlType != ControlType.ListItem)
                    {
                        continue;
                    }
                    var text = ExtractText(element);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    result.Add(text);
                }
                catch
                {
                    // 单个控件读取失败不影响整体读取
                }
            }
        }
        catch
        {
            return [];
        }

        return result;
    }

    private static string ExtractText(AutomationElement element)
    {
        var name = element.Name;

        if (!string.IsNullOrWhiteSpace(name)) return name;

        try
        {
            var valuePattern = element.Patterns.Value.PatternOrDefault;
            if(valuePattern is not null)
            {
                var value = valuePattern.Value;
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        catch
        {
            // 忽略
        }

        return "";
    }

    private static string NormalizeText(string text)
    {
        return text.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static bool ShouldIgnoreText(string text)
    {
        if (text.Length < 1) return true;
        var ignoredTexts = new HashSet<string>
        {
            "微信",
            "通讯录",
            "收藏",
            "聊天文件",
            "朋友圈",
            "小程序",
            "设置",
            "搜索",
            "发送"
        };

        return ignoredTexts.Contains(text);
    }

    private static string GetSessionName(Window mainWindow)
    {
        var title = mainWindow.Title;
        if(!string.IsNullOrWhiteSpace(title)) return title;

        return "微信当前会话";
    }
}