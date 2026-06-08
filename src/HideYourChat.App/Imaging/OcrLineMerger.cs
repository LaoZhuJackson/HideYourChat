using System.Diagnostics;
using System.Drawing;
using Serilog;

namespace HideYourChat.App.Imaging;

/// <summary>
/// 把 OCR 输出的零散文本行合并成"消息段"。
/// 同一条消息换行时:垂直间距小 且 左边缘对齐 → 合并为一行。
/// 阈值以"行高"为单位,自动适配字号/DPI/窗口尺寸。
/// 纯几何逻辑,与具体聊天软件无关,微信/QQ 适配器都可复用。
/// </summary>
public static class OcrLineMerger
{
    /// <param name="lines">已按 Top→Left 排序的 OCR 行(PaddleOcrEngine 已保证)。</param>
    /// <param name="gapFactor">行间距 < 行高×gapFactor 视为同段。越大越容易合并。</param>
    /// <param name="alignFactor">左边缘差 < 行高×alignFactor 视为对齐(同一气泡)。</param>
    public static IReadOnlyList<OcrLine> Merge(IReadOnlyList<OcrLine> lines, double gapFactor = 0.5, double alignFactor = 1.0)
    {
        if(lines.Count == 0) return lines;
        var groups = new List<List<OcrLine>>();
        var current = new List<OcrLine> { lines[0] };

        for(int i = 1; i < lines.Count; i++)
        {
            var line = lines[i];
            var prev = current[^1]; // 组内最后一行，算垂直间距
            var head = current[0]; // 组内第一行，用来比较左边缘

            double lineHeight = prev.Bounds.Height;
            double verticalGap = line.Bounds.Top - prev.Bounds.Bottom;
            double leftDelta = Math.Abs(line.Bounds.Left - head.Bounds.Left);
            // 用行高区分名称和消息内容
            double heightRatio = line.Bounds.Height / lineHeight;

            bool sameMessage = verticalGap > -lineHeight * 0.5 && // 允许有重叠
                                verticalGap < lineHeight * gapFactor &&
                                leftDelta < lineHeight * alignFactor &&
                                heightRatio > 0.75 && heightRatio < 1.33;
            Log.Debug("当前消息：{now}, 是否与上一条同一条消息: {same}, 垂直间距：{ver}, 行高：{lineH}, 左偏差值：{left}", line, sameMessage,verticalGap,lineHeight,leftDelta);
            if(sameMessage) current.Add(line);
            else {groups.Add(current); current = [line]; }
        }
        groups.Add(current);

        // 每组合并：文本拼接，边界取并集（DrawBoxes 画整段框，方便肉眼核对）
        return groups
                .Select(g => new OcrLine(
                    JoinTexts(g),
                    g.Select(l => l.Bounds).Aggregate(RectangleF.Union)))
                .ToList();
    }

    /// <summary>
    /// 把一组行的文本拼成一段。英文/数字边界补空格,中文之间不补。
    /// 规则:相接的两个字符都是 ASCII 字母数字时才加空格(英文单词边界),
    /// 否则(至少一端是中文/标点)直接相连。
    /// </summary>
    private static string JoinTexts(List<OcrLine> group)
    {
        var sb = new System.Text.StringBuilder();
        foreach(var line in group)
        {
            if(line.Text.Length == 0) continue;
            if(sb.Length > 0)
            {
                char last = sb[^1];
                char first = line.Text[0];
                // 两端都是 ASCII 字母/数字 → 是英文单词被换行截断,补一个空格
                if(IsAsciiWordChar(last) && IsAsciiWordChar(first)) sb.Append(' ');
            }
            sb.Append(line.Text); //第一条text会直接append进sb
        }
        return sb.ToString();
    }

    private static bool IsAsciiWordChar(char c) => char.IsAsciiLetterOrDigit(c);
}