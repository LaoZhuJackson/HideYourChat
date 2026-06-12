using System.IO;
using System.Windows;
using HideYourChat.App.Core;
using HideYourChat.App.Update;
using Serilog;

namespace HideYourChat.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private SettingsService? _settingsService;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 日志存到 ./logs/
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HideYourChat", "logs"
        );
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug() // 开发期记到 Debug 级
            .WriteTo.Debug() // → VSCode 调试控制台(F5 时)
            .WriteTo.File(
                Path.Combine(logDir, "app-.log"),
                rollingInterval: RollingInterval.Day, // 按天滚动
                retainedFileCountLimit: 7, // 只留最近 7 天
                encoding: new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true),  // 带 BOM
                outputTemplate:"{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
            ).CreateLogger();
        Log.Information("应用启动");

        _settingsService = new SettingsService();

        // 检查更新
        Task.Delay(3000).ContinueWith(_ =>
        {
            Dispatcher.Invoke(async () =>
            {
                await CheckForUpdateAsync();
            });
        });
    }

    private async Task CheckForUpdateAsync()
    {
        var settings = _settingsService!.Load();
        using var service = new UpdateService(settings);
        var result = await service.CheckAsync(UpdateService.CurrentVersion);

        if(result == null)
        {
            if(service.LastError != null)
                Log.Warning("启动检查更新失败：{Error}", service.LastError);
            return;
        }
        if(settings.SkippedVersion == result.Version) return; // 如果之前设置了跳过版本

        var window = new UpdateWindow(result, service, skippedVersion =>
        {
            settings.SkippedVersion = skippedVersion;
            _settingsService!.Save(settings);
        })
        {
            Owner = MainWindow
        };
        window.ShowDialog();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("应用退出");
        Log.CloseAndFlush(); // 确保缓冲的日志写盘,别丢最后几条
        base.OnExit(e);
    }
}

