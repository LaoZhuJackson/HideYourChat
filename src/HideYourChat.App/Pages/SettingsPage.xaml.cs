using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using HideYourChat.App.Core;
using HideYourChat.App.Update;

namespace HideYourChat.App.Pages;

public partial class SettingsPage : UserControl
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;

    public SettingsPage(AppSettings settings, SettingsService settingsService)
    {
        InitializeComponent();
        _settings = settings;
        _settingsService = settingsService;
        ApplySettingsToUi();
    }

    public void ApplySettingsToUi()
    {
        CloseToExitCheckBox.IsChecked = _settings.CloseToExit;
        MaxMessageCountBox.Value = _settings.MaxMessageCount;
        CurrentVersionText.Text = $"当前版本：v{UpdateService.CurrentVersion}";
    }

    public void SaveToSettings()
    {
        _settings.CloseToExit = CloseToExitCheckBox.IsChecked == true;
        _settings.MaxMessageCount = (int)(MaxMessageCountBox.Value ?? 30);
    }

    public event Action<string>? StatusChanged;

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        StatusChanged?.Invoke("正在检查更新…");

        using var service = new UpdateService(_settings);
        var result = await service.CheckAsync(UpdateService.CurrentVersion);

        if (result is not null)
        {
            StatusChanged?.Invoke($"发现新版本 v{result.Version}");
            var mainWindow = (MainWindow)System.Windows.Window.GetWindow(this);
            var window = new UpdateWindow(result, service, skippedVersion =>
            {
                _settings.SkippedVersion = skippedVersion;
                _settingsService.Save(_settings);
            })
            {
                Owner = mainWindow
            };
            window.ShowDialog();
        }
        else if (service.LastError is not null)
        {
            StatusChanged?.Invoke($"检查更新失败 — {service.LastError}");
        }
        else
        {
            StatusChanged?.Invoke($"版本 {UpdateService.CurrentVersion} 已是最新版本 ✓");
        }

        CheckUpdateButton.IsEnabled = true;
    }

    private void CloseToExitCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _settings.CloseToExit = CloseToExitCheckBox.IsChecked == true;
        _settingsService.Save(_settings);
    }
}