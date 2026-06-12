using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.IO;
using Serilog;
using YamlDotNet.Serialization;
using HideYourChat.App.Core;

namespace HideYourChat.App.Update;

public class UpdateService : IDisposable
{
    private const string ApiUrl = "https://api.github.com/repos/LaoZhuJackson/HideYourChat/releases/latest";
    private readonly HttpClient _http;
    private bool _disposed;
    public string? LastError {get; private set;}

    public static string CurrentVersion => System.Reflection.Assembly.GetExecutingAssembly()
        .GetName().Version?.ToString(3) ?? "0.0.0";

    public UpdateService(AppSettings? settings = null)
    {
        var handler = new HttpClientHandler();
        // 配置了代理则走代理
        if (settings != null && !string.IsNullOrEmpty(settings.ProxyHost) && settings.ProxyPort > 0)
        {
            handler.Proxy = new System.Net.WebProxy(settings.ProxyHost, settings.ProxyPort);
            handler.UseProxy = true;
            Log.Information("使用代理 {Host}:{Port}", settings.ProxyHost, settings.ProxyPort);
        }
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("HideYourChat");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.Timeout = TimeSpan.FromSeconds(10);

        if (settings != null && !string.IsNullOrEmpty(settings.GitHubToken))
        {
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.GitHubToken);
            Log.Information("已设置 GitHub Token，解除 API 限流");
        }
    }

    /// <summary>比较版本号，返回 null 表示没有新版本或检查失败</summary>
    public async Task<UpdateCheckResult?> CheckAsync(string currentVersion)
    {
        LastError = null;
        try
        {
            var json = await _http.GetStringAsync(ApiUrl);
            using var doc = JsonDocument.Parse(json);

            var tagName = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var latestVersion = tagName.TrimStart('v');  // 去掉 v 前缀

            if (!IsNewer(latestVersion, currentVersion))
                return null;  // 没有新版本

            var assets = doc.RootElement.GetProperty("assets");
            var msi = assets.EnumerateArray().FirstOrDefault(a => a.GetProperty("name").GetString()?.EndsWith(".msi") == true);

            if (msi.ValueKind == JsonValueKind.Undefined)
            {
                Log.Warning("GitHub Release 中未找到 .msi 资源，跳过更新");
                return null;
            }

            var body = doc.RootElement.GetProperty("body").GetString() ?? "";

            return new UpdateCheckResult
            {
                TagName = tagName,
                Version = latestVersion,
                DownloadUrl = msi.GetProperty("browser_download_url").GetString() ?? "",
                ReleaseNotes = body,
                Size = msi.GetProperty("size").GetInt64(),
                ForceUpdate = body.Contains("[force]", StringComparison.OrdinalIgnoreCase)
            };
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            LastError = "仓库尚未发布任何版本";
            return null;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            LastError = "GitHub API 限流（403），建议在 settings.json 中配置 GitHubToken";
            // 403 说明 API 限流，提示配 Token
            Log.Warning(LastError);
            return null;
        }
        catch(HttpRequestException ex)
        {
            LastError = $"网络连接失败：{ex.Message}";
            Log.Warning(ex, "检查更新失败");
            return null;
        }
        catch (Exception ex)
        {
            LastError = $"检查更新异常：{ex.Message}";
            Log.Warning(ex, "检查更新失败");
            return null;  // 请求失败或解析错误时视为没有新版本
        }
    }

    public async Task<string?> DownloadAsync(string url, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "HideYourChat_Update.msi");
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;
            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = File.Create(tempPath);
            var buffer = new byte[8192];
            long totalRead = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                totalRead += read;
                if (totalBytes > 0)
                    progress?.Report((double)totalRead / totalBytes);
            }
            return tempPath;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "下载更新失败");
            try { File.Delete(tempPath); } catch { }  // 删除不完整的文件
            return null;  // 下载失败时返回 null
        }
    }

    /// <summary>静默安装。需要管理员权限，会触发UAC弹窗。</summary>
    public void Install(string msiPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "msiexec.exe",
            // /i 安装 /quiet 静默 /norestart 不重启
            Arguments = $"/i \"{msiPath}\" /quiet /norestart",
            UseShellExecute = true,
            Verb = "runas"  // 以管理员权限运行
        };
        Process.Start(psi);
    }

    private static bool IsNewer(string latest, string current)
    {
        if (!Version.TryParse(latest, out var vLatest) || !Version.TryParse(current, out var vCurrent))
            return false;  // 版本格式不正确时视为没有新版本
        return vLatest > vCurrent;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _http.Dispose();
        _disposed = true;
    }
}