namespace HideYourChat.App.Core;

/// <summary>
/// 应用配置。纯数据,可被 JSON 序列化。
/// 所有字段给默认值 → 首次运行/字段缺失时有合理回退。
/// </summary>
public sealed class AppSettings
{
    public string SelectedApp {get; set;} = "微信";
    public bool UseStandaloneChatWindow {get; set;} = false;
    public string ContactName {get; set;} = "";
    public double BackgroundOpacity {get; set;} = 0.80;
    public double TextOpacity {get; set;} = 1.00;
    public int MonitorIntervalMs {get; set;} = 1500;
    // Overlay 窗口位置和展开尺寸。double.NaN 表示"未保存过",用默认位置
    public double OverlayLeft { get; set; } = double.NaN;
    public double OverlayTop { get; set; } = double.NaN;
    public double OverlayWidth { get; set; } = 480;
    public double OverlayHeight { get; set; } = 380;
    public bool IsDarkTheme {get; set; } = true;
}