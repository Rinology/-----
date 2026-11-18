using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using ChatHelper.Models;

namespace ChatHelper.Services;

public class ConfigManager
{
    private readonly string _filePath;
    private readonly object _fileLock = new();
    private AppConfig _config;

    public AppConfig Config
    {
        get
        {
            lock (_fileLock)
            {
                return _config;
            }
        }
        private set
        {
            lock (_fileLock)
            {
                _config = value;
            }
        }
    }

    public ConfigManager(string fileName = "config.json")
    {
        _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        _config = LoadInternal();
    }

    public AppConfig Load()
    {
        lock (_fileLock)
        {
            _config = LoadInternal();
            return _config;
        }
    }

    private AppConfig LoadInternal()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                var json = reader.ReadToEnd();
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null)
                {
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ConfigManager] 설정 로드 예외 발생: {ex.Message}");
        }

        var defaultConfig = new AppConfig();
        SaveInternal(defaultConfig);
        return defaultConfig;
    }

    public void Save(AppConfig? config = null)
    {
        lock (_fileLock)
        {
            if (config != null)
            {
                _config = config;
            }

            SaveInternal(_config);
        }
    }

    private void SaveInternal(AppConfig configToSave)
    {
        string tempPath = _filePath + ".tmp";
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var json = JsonSerializer.Serialize(configToSave, options);

            // 1. 임시 파일에 먼저 안전하게 쓰기
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fs))
            {
                writer.Write(json);
                writer.Flush();
            }

            // 2. 원자적(Atomic) 교체로 파일 손상 방지
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ConfigManager] 설정 저장 예외 발생: {ex.Message}");
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch { }
        }
    }
}
