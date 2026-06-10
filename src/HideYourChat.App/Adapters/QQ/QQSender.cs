using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using HideYourChat.App.Core;
using Serilog;

namespace HideYourChat.App.Adapters.QQ;

public sealed class QQSender
{
    /// <summary>UIA 方式发送:写输入框 + 点发送按钮。不依赖窗口前台。</summary>
    public SendResult Send(AutomationElement root, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return SendResult.Fail("qq-uia", "消息内容不能为空");
        // 0) 记录窗口当前位置/是否在屏外 —— 把"窗口状态"和"发送结果"关联起来
        try
        {
            var r = root.BoundingRectangle;
            Log.Information("【发送诊断】窗口区域 X={X} Y={Y} W={W} H={H}", r.X, r.Y, r.Width, r.Height);
        }
        catch (Exception ex) { Log.Warning("【发送诊断】读窗口区域失败：{E}", ex.Message); }
        // 1) 找候选输入框 —— 把所有可写元素都列出来,看不同窗口状态下选中的是不是同一个
        AutomationElement? input = null;
        double bestTop = double.MinValue;
        foreach (var e in root.FindAllDescendants())
        {
            try
            {
                if (SafeName(e) == "搜索") continue; // 排除顶部搜索框
                var vp = e.Patterns.Value;
                if (!vp.IsSupported || vp.Pattern.IsReadOnly.ValueOrDefault) continue;

                var r = e.BoundingRectangle;
                if(r.Width <= 1 || r.Height <= 1) continue; // 排除 0x0 的 TitleBar 幽灵元素

                if(r.Top > bestTop) { bestTop = r.Top; input = e; }
            }
            catch { }
        }
        if(input is null)
            return SendResult.Fail("qq-uia", "没找到可写输入框");

        // 2) 写入,然后立刻读回 —— 这一步是核心:写进去了没有?
        try { input.Patterns.Value.Pattern.SetValue(message); }
        catch (Exception ex) { return SendResult.Fail("qq-uia", $"写入失败：{ex.Message}"); }

        System.Threading.Thread.Sleep(120);
        string afterWrite = ReadValue(input);
        Log.Information("【发送诊断】写入后读回 = \"{V}\"  (期望=\"{M}\")", afterWrite, message);
        bool writeOk = afterWrite == message;

        // 3) 点发送
        var sendBtn = root.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.Button).And(cf.ByName("发送")));
        if (sendBtn is null)
            return SendResult.Fail("qq-uia", "没找到发送按钮");

        bool invokeSupported = false;
        try
        {
            var invoke = sendBtn.Patterns.Invoke;
            invokeSupported = invoke.IsSupported;
            if (invoke.IsSupported) invoke.Pattern.Invoke();
            else sendBtn.Click();
        }
        catch (Exception ex) { return SendResult.Fail("qq-uia", $"点击发送失败：{ex.Message}"); }

        // 4) 发送后再读回 —— 输入框被清空 = 真的发出去了
        System.Threading.Thread.Sleep(200);
        string afterSend = ReadValue(input);
        Log.Information("【发送诊断】Invoke支持={S}; 发送后读回=\"{V}\"", invokeSupported, afterSend);

        bool cleared = string.IsNullOrWhiteSpace(afterSend);
        Log.Information("【发送诊断】结论: 写入生效={W}, 发送后清空={C}", writeOk, cleared);

        // 暂时仍返回 Ok,别让 UI 干扰你看日志;诊断完再按真实信号改
        return SendResult.Ok("qq-uia");
    }

    private static string ReadValue(AutomationElement el)
    {
        try { return el.Patterns.Value.Pattern.Value.ValueOrDefault ?? ""; } catch { return "<读取失败>"; }
    }

    private static string SafeType(AutomationElement el) { try { return el.ControlType.ToString(); } catch { return "?"; } }

    private static string SafeRect(AutomationElement el)
    {
        try
        {
            var r = el.BoundingRectangle; return $"{r.X},{r.Y},{r.Width}x{r.Height}";
        }
        catch
        {
            return "?";
        }
    }

    private static string SafeName(AutomationElement el)
    {
        try { return el.Name ?? ""; } catch { return ""; }
    }
}