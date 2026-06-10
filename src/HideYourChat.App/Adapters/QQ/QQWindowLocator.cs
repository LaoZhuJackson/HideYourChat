using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace HideYourChat.App.Adapters.QQ;

public sealed class QQWindowLocator
{
    private static readonly string[] CandidateProcessNames = [
        "QQ",
        "TIM"
    ];

    public Window? FindMainWindow(UIA3Automation automation)
    {
        foreach(var processName in CandidateProcessNames)
        {
            var processes = Process.GetProcessesByName(processName);
            foreach(var process in processes)
            {
                try
                {
                    if(process.MainWindowHandle == IntPtr.Zero) continue;

                    var app = FlaUI.Core.Application.Attach(process);
                    var mainWindow = app.GetMainWindow(automation);
                    if(mainWindow is not null) return mainWindow;
                }
                catch
                {
                    // 某些进程可能无法 attach，跳过即可。
                }
            }
        }
        return null;
    }

    public IntPtr FindMainWindowHandle()
    {
        foreach(var name in CandidateProcessNames)
            foreach(var p in System.Diagnostics.Process.GetProcessesByName(name))
                if(p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle;
        return IntPtr.Zero;
    }
}