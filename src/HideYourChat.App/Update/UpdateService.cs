using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HideYourChat.App.Update;

public class UpdateService{
    private const string ApiUrl = "https://api.github.com/LaoZhuJackson/HideYourChat/releases/latest";
    private readonly HttpClient _http;

    public UpdateService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("HideYourChat");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.Timeout = TimeSpan.FromSeconds(5);
    }

    /// <summary>比较版本号，返回 true 如果 latestVersion 比 currentVersion 新</summary>
    public async Task<UpdateCheckResult?> CheckAsync(string currentVersion)
    {
        try{
            var json = await _http.GetStringAsync(ApiUrl);
            using var doc = JsonDocument.Parse(json);

            var tagName = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var latestVersion = tagName.TrimStart('v');  // 去掉 v 前缀

            if(!IsNewer(latestVersion, currentVersion))
                return null;  // 没有新版本
            
            var assets = doc.RootElement.GetProperty("assets");
            var msi = assets.EnumerateArray().FirstOrDefault(a => a.GetProperty("name").GetString()?.EndsWith(".msi") == true);

            return new UpdateCheckResult
            {
                TagName = tagName,
                Version = latestVersion,
                DownloadUrl = msi.GetProperty("browser_download_url").GetString() ?? "",
                ReleaseNotes = doc.RootElement.GetProperty("body").GetString() ?? "",
                Size = msi.GetProperty("size").GetInt64(),
                ForceUpdate = (doc.RootElement.GetProperty("body").GetString() ?? "").Contains("[force]")
            };
        }
        catch{
            return null;  // 请求失败或解析错误时视为没有新版本
        }
    }

    public async Task<string?> DownloadAsync(string url, IProgress<double>? progress = null)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "HideYourChat_Update.msi");
        try{
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if(!response.IsSuccessStatusCode)
                return null;
            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(tempPath);
            var buffer = new byte[8192];
            long totalRead = 0;
            int read;
            while((read = await stream.ReadAsync(buffer)) > 0){
                await fileStream.WriteAsync(buffer.AsMemory(0, read));
                totalRead += read;
                if(totalBytes > 0)
                    progress?.Report((double)totalRead / totalBytes);
            }
            return tempPath;
        }
        catch{
            try{ File.Delete(tempPath); } catch{}  // 删除不完整的文件
            return null;  // 下载失败时返回 null
        }
    }

    /// <summary>静默安装。需要管理员权限，会触发UAC弹窗。</summary>
    public void Install(string msiPath){
        var psi = new ProcessStartinfo{
            FileName = "msiexec.exe",
            //i安装/quiet静默/norestart不重启
            Arguments = $"/i \"{msiPath}\" /quiet /norestart",
            UseShellExecute = true,
            Verb = "runas"  // 以管理员权限运行
        };
        Process.Start(psi);
    }

    private static bool IsNewer(string latest, string current){
        if(!Version.TryParse(latest, out var vLatest) || !Version.TryParse(current, out var vCurrent))
            return false;  // 版本格式不正确时视为没有新版本
        return vLatest > vCurrent;
    }
}