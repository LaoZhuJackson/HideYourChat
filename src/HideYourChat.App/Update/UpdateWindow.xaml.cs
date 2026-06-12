using System.ComponentModel;
using System.Windows;
using HideYourChat.App.Core;
using Serilog;
using Wpf.Ui.Controls;

namespace HideYourChat.App.Update;

public partial class UpdateWindow : FluentWindow //partial 关键字允许将一个类的定义分散在多个文件中
{
    private readonly UpdateCheckResult _result;
    private readonly UpdateService _service;
    private readonly Action<string>? _onSkipVersion; //Action<string> 是一个预定义的委托类型，表示一个无返回值、接受一个 string 参数的方法
    private CancellationTokenSource? _cts;

    /// <summary>
    /// </summary>
    /// <param name="result">更新检查结果</param>
    /// <param name="service">UpdateService（调用方负责 Dispose）</param>
    /// <param name="onSkipVersion">用户勾选「不再提醒」时回调，传入被跳过的版本号</param>
    public UpdateWindow(
        UpdateCheckResult result,
        UpdateService service,
        Action<string>? onSkipVersion = null
    )
    {
        InitializeComponent();
        _result = result;
        _service = service;
        _onSkipVersion = onSkipVersion;

        Loaded += OnLoaded; //Loaded 事件 - 窗口加载完成时触发
        Closing += OnClosing; //Closing 事件 - 窗口关闭时触发
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CurrentVersionText.Text = UpdateService.CurrentVersion;
        LatestVersionText.Text = $"v{_result.Version}";
        FileSizeText.Text = FormatFileSize(_result.Size);
        ReleaseNotesBox.Text = _result.ReleaseNotes;

        if (_result.ForceUpdate) // 强制更新
        {
            ForceUpdateInfoBar.Visibility = Visibility.Visible;
            SkipButton.Visibility = Visibility.Collapsed;
            DontRemindCheckBox.Visibility = Visibility.Collapsed;
        }
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        // 进入下载状态：禁用按钮，显示进度条
        InstallButton.IsEnabled = false;
        SkipButton.IsEnabled = false;
        DontRemindCheckBox.IsEnabled = false;
        DownloadProgressBar.Visibility = Visibility.Visible;
        StatusText.Text = "正在下载更新…";

        _cts = new CancellationTokenSource();
        var progress = new Progress<double>(p =>
        {
            DownloadProgressBar.Value = p * 100;
            StatusText.Text = $"正在下载更新… {p:P0}";
        });

        string? msiPath;
        try
        {
            msiPath = await _service.DownloadAsync(_result.DownloadUrl, progress, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "下载已取消";
            Log.Information("更新下载被用户取消");
            return;
        }

        if (msiPath == null)
        {
            StatusText.Text = "下载失败，请稍后重试";
            InstallButton.IsEnabled = true;
            SkipButton.IsEnabled = true;
            DontRemindCheckBox.IsEnabled = true;
            DownloadProgressBar.Visibility = Visibility.Collapsed;
            return;
        }

        StatusText.Text = "下载完成，正在安装…";
        _service.Install(msiPath);

        // 安装已触发（msiexec 由 UAC 提权运行），关闭窗口
        Log.Information("已触发安装程序，版本 {Version}", _result.Version);
        Close();
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        if (DontRemindCheckBox.IsChecked == true)
            _onSkipVersion?.Invoke(_result.Version);

        Log.Information("用户跳过版本 {Version}", _result.Version);
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // 窗口关闭时取消正在进行的下载
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
            _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB"
        };
    }
}