namespace HideYourChat.App.Update;

public class UpdateCheckResult
{
    public string TagName { get; set; } = "";
    public string Version { get; set; } = "";  // 去掉 v 前缀，如 "1.0.1"
    public string DownloadUrl { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public long Size { get; set; }
    public bool ForceUpdate { get; set; }
}
