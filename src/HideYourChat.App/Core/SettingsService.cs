using System.IO;
using System.Linq.Expressions;
using System.Text.Json;
using Serilog;

namespace HideYourChat.App.Core;

/// <summary>负责把 AppSettings 读写到 %APPDATA%/HideYourChat/settings.json。</summary>
public sealed class SettingsService
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, // 存成带缩进的刻度JSON
        // 让中文不被转成 \uXXXX，文件里直接是中文
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HideYourChat"
        );
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    /// <summary>读取配置。文件不存在或损坏时,返回默认配置(不抛异常)。</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                Log.Information("配置文件不存在,使用默认配置");
                return new AppSettings();
            }

            var json = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
            return settings ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // 配置损坏不应该让程序起不来 → 回退默认值
            Log.Warning(ex, "读取配置失败,使用默认配置");
            return new AppSettings();
        }
    }

    /// <summary>保存配置。失败只记日志,不影响主流程。</summary>
    public void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, Options);
            File.WriteAllText(_path, json);
            Log.Debug("配置已保存");
        }
        catch(Exception ex)
        {
            Log.Warning(ex, "保存配置失败");
        }
    }
}