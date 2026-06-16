namespace HideYourChat.App.Core;

/// <summary>
/// 应用配置。纯数据,可被 JSON 序列化。
/// 所有字段给默认值 → 首次运行/字段缺失时有合理回退。
/// </summary>
public sealed class AppSettings
{
    public string SelectedApp { get; set; } = "QQ";
    public bool UseStandaloneChatWindow { get; set; } = false;
    public string ContactName { get; set; } = "";
    public double BackgroundOpacity { get; set; } = 0.80;
    public double TextOpacity { get; set; } = 1.00;
    public int MonitorIntervalMs { get; set; } = 1500;
    // Overlay 窗口位置和展开尺寸。double.NaN 表示"未保存过",用默认位置
    public double OverlayLeft { get; set; } = double.NaN;
    public double OverlayTop { get; set; } = double.NaN;
    public double OverlayWidth { get; set; } = 480;
    public double OverlayHeight { get; set; } = 380;
    public bool IsDarkTheme { get; set; } = true;
    public int QQHideModeIndex { get; set; } = 0;
    public double WeChatCropLeft { get; set; } = 0.35;
    public double WeChatCropTop { get; set; } = 0.09;
    public double WeChatCropRight { get; set; } = 1.0;
    public double WeChatCropBottom { get; set; } = 0.82;
    public string SkippedVersion { get; set; } = "";
    public string ProxyHost { get; set; } = "127.0.0.1";
    public int ProxyPort {get;set;} = 0; // 如 7890，0 表示不走代理
    public string GitHubToken{get;set;} = "";
    public bool CloseToExit { get; set; } = false; // true = 关窗口直接退出，false = 缩到托盘
}