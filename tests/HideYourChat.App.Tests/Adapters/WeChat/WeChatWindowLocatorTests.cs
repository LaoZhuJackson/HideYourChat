using FlaUI.Core.AutomationElements;
using FlaUI.Core.Exceptions;
using FlaUI.UIA3;
using HideYourChat.App.Adapters.WeChat;
using Xunit;
using Xunit.Abstractions;

namespace HideYourChat.App.Tests.Adapters.WeChat;

public sealed class WeChatWindowLocatorTests
{
    private readonly ITestOutputHelper _output;

    public WeChatWindowLocatorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void FindMainWindow_Should_NotThrow()
    {
        using var automation = new UIA3Automation();

        var locator = new WeChatWindowLocator();

        var exception = Record.Exception(() =>
        {
            var window = locator.FindMainWindow(automation);

            if (window is null)
            {
                _output.WriteLine("未找到微信窗口。请确认微信 PC 客户端已启动，并且主窗口没有最小化到托盘。");
                return;
            }

            WriteWindowInfo(window);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void FindMainWindow_Should_Find_WeChat_When_WeChat_Is_Open()
    {
        using var automation = new UIA3Automation();

        var locator = new WeChatWindowLocator();

        var window = locator.FindMainWindow(automation);

        Assert.NotNull(window);

        WriteWindowInfo(window!);
    }

    private void WriteWindowInfo(Window window)
    {
        _output.WriteLine($"找到窗口：Title={SafeGet(() => window.Title)}");
        _output.WriteLine($"AutomationId={SafeGet(() => window.AutomationId)}");
        _output.WriteLine($"ClassName={SafeGet(() => window.ClassName)}");
        _output.WriteLine($"Name={SafeGet(() => window.Name)}");
    }

    private static string SafeGet(Func<string?> getter)
    {
        try
        {
            return getter() ?? "";
        }
        catch (PropertyNotSupportedException)
        {
            return "<NotSupported>";
        }
        catch (Exception ex)
        {
            return $"<Error: {ex.GetType().Name}>";
        }
    }
}