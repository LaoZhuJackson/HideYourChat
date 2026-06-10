using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using HideYourChat.App.Core;
using Serilog;

namespace HideYourChat.App.Adapters.QQ;

/// <summary>
/// 从 QQNT UIA 树读消息。消息列表的直接子级里:
///   有 Name 的 Group = 发送人标记(更新当前发送人);
///   无 Name 的 Group = 一条消息内容;
///   裸 Text(不在 Group 里) = 等级/头衔等噪声,跳过。
/// 群聊每条消息前都重出发送人标记,天然支持连发归属。
/// </summary>
public sealed class QQReader
{
    // 时间/日期分隔符:命中即跳过,并清空当前发送人(防止跨时间段错误沿用)
    private static readonly Regex[] SeparatorPatterns =
    {
        new(@"^\d{4}/\d{1,2}/\d{1,2}"),   // 2026/05/20 10:37
        new(@"^\d{1,2}:\d{2}$"),          // 20:10
        new(@"^(昨天|今天|星期|周)"),      // 昨天 21:37 / 星期五 17:34
    };

    public IReadOnlyList<ChatMessage> ReadLatestMessages(AutomationElement mainWindow, string sessionTitle, bool isGroup, string myNickname = "")
    {
        var listContainer = mainWindow.FindFirstDescendant(cf => cf.ByName("消息列表"));
        if(listContainer is null) return [];

        AutomationElement[] rows;
        try{rows = listContainer.FindAllChildren();}
        catch{return [];}

        var result = new List<ChatMessage>();
        string currentSender = "";

        foreach(var row in rows)
        {
            // 只处理 Group,裸 Text(LV/100/群主/重复昵称)直接忽略
            if(row.ControlType != ControlType.Group) continue;

            string name = SafeName(row);
            // 有 Name 的 Group → 发送人标记
            if (!string.IsNullOrWhiteSpace(name))
            {
                currentSender = name;
                continue;
            }

            // 无 Name 的 Group → 消息内容
            string text = ExtractContent(row);
            if(string.IsNullOrWhiteSpace(text)) continue;

            // 时间分隔符:跳过并清空发送人
            if(IsSeparator(text)) { currentSender = ""; continue;}

            result.Add(new ChatMessage
            {
                AdapterId = "qq",
                SessionName = string.IsNullOrWhiteSpace(sessionTitle) ? "QQ" : sessionTitle,
                SenderName = ResolveSender(currentSender, sessionTitle, isGroup, myNickname),
                Text = text,
                ReceivedAt = DateTimeOffset.Now
            });
        }

        return result;
    }

    private static string ResolveSender(string sender, string sessionTitle, bool isGroup, string myNickname)
    {
        if(string.IsNullOrWhiteSpace(sender)) return "";

        bool isMe = isGroup
            ? (!string.IsNullOrWhiteSpace(myNickname) && sender == myNickname) // 群聊：得到自己的昵称
            : (sender != sessionTitle); // 单聊
        Log.Debug("运行ResolveSender,isMe={me},isGroup={group}",isMe,isGroup);
        return isMe ? "我" : sender;
    }

    /// <summary>拼接内容 Group 里的所有 Text;Image"图片"→[图片]。</summary>
    private static string ExtractContent(AutomationElement row)
    {
        var parts = new List<string>();

        AutomationElement[] descendants;
        try { descendants = row.FindAllDescendants();}
        catch {return "";}

        foreach(var el in descendants)
        {
            try
            {
                var ct = el.ControlType;
                if(ct == ControlType.Text)
                {
                    var t = el.Name?.Trim();
                    if(!string.IsNullOrWhiteSpace(t)) parts.Add(t);
                }
                else if(ct == ControlType.Image && el.Name?.Trim() == "图片")
                {
                    parts.Add("[图片]");
                }
            }
            catch {/* 单控件失败不影响整体 */}
        }

        return string.Join(" ", parts);
    }

    private static bool IsSeparator(string text) => SeparatorPatterns.Any(p => p.IsMatch(text));

    private static string SafeName(AutomationElement el)
    {
        try{return el.Name ?? "";}
        catch{return "";}
    }
}