using System.IO;
using System.Text;
using System.Windows.Automation;
using FlaUI.Core.AutomationElements;
using AutomationElement = FlaUI.Core.AutomationElements.AutomationElement;   // 消除与 WPF 同名类型的歧义

namespace HideYourChat.App.Adapters.QQ;

/// <summary>
/// 一次性诊断工具:把某个 UIA 元素的子树打印成缩进文本,存到 %TEMP%/HideYourChat/uia/。
/// 用来摸清 QQ 控件结构(ControlType/Name/AutomationId/ClassName/Value)
/// </summary>
public static class UiaTreeDumper
{
    public static string Dump(AutomationElement root, int maxDepth = 14, int maxNodes = 6000)
    {
        var sb = new StringBuilder();
        int count = 0;
        Walk(root, 0, maxDepth, maxNodes, sb, ref count);

        var dir = Path.Combine(Path.GetTempPath(), "HideYourChat", "uia");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"qq_tree_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true)); // 带 BOM,记事本不乱码
        return path;
    }

    private static void Walk(AutomationElement el, int depth, int maxDepth, int maxNodes, StringBuilder sb, ref int count)
    {
        if(count >= maxNodes || depth > maxDepth) return;
        count++;

        sb.Append(' ', depth * 2);
        sb.AppendLine(Describe(el));

        AutomationElement[] children;
        try { children = el.FindAllChildren(); }
        catch { return; }

        foreach(var child in children)
        {
            Walk(child, depth + 1, maxDepth, maxNodes, sb, ref count);
            if(count >= maxNodes) break;
        }
    }

    private static string Describe(AutomationElement el)
    {
        // 异常安全的包装器方法
        static string Safe(Func<string?> f){try{return f() ?? "";} catch{return "?";}}
        var parts = new List<string> {Safe(() => el.ControlType.ToString())};

        var name = Safe(()=>el.Name);
        var autoId = Safe(()=>el.AutomationId);
        var cls = Safe(()=> el.ClassName);

        string value = "";
        try{value = el.Patterns.Value.PatternOrDefault?.Value ?? "";} catch {}

        if(!string.IsNullOrEmpty(name)) parts.Add($"Name=\"{Trunc(name)}\"");
        if (!string.IsNullOrEmpty(autoId)) parts.Add($"AutomationId=\"{autoId}\"");
        if (!string.IsNullOrEmpty(cls)) parts.Add($"Class=\"{cls}\"");
        if (!string.IsNullOrEmpty(value)) parts.Add($"Value=\"{Trunc(value)}\"");

        return string.Join(" ", parts);
    }
    // 字符串截断
    private static string Trunc(string s, int max = 50) => s.Length <= max ? s : string.Concat(s.AsSpan(0, max),"...");
}