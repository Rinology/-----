using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using ChatHelper.Models;

namespace ChatHelper.Services;

public class ConfigManager
{
    private readonly string _filePath;
    public AppConfig Config { get; private set; }

    public ConfigManager(string fileName = "config.json")
    {
        _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        Config = Load();
    }

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null)
                    return config;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ConfigManager] 로드 에러: {ex.Message}");
        }

        var def = new AppConfig();
        Save(def);
        return def;
    }

    public void Save(AppConfig? config = null)
    {
        try
        {
            if (config != null)
                Config = config;

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var json = JsonSerializer.Serialize(Config, options);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ConfigManager] 저장 에러: {ex.Message}");
        }
    }
}
