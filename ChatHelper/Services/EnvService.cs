using System.IO;

namespace ChatHelper.Services;

public static class EnvService
{
    private static readonly Dictionary<string, string> _envVars = new(StringComparer.OrdinalIgnoreCase);
    private static bool _isLoaded = false;

    public static void Load(string? customPath = null)
    {
        if (_isLoaded && customPath == null) return;

        var searchPaths = new List<string>();
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            searchPaths.Add(customPath);
        }

        // 1. 현재 실행 폴더
        searchPaths.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env"));
        // 2. 상위 폴더들 (프로젝트 루트, 솔루션 루트 탐색)
        searchPaths.Add(Path.Combine(Directory.GetCurrentDirectory(), ".env"));
        searchPaths.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ".env"));
        searchPaths.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".env"));
        searchPaths.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".env"));

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                ParseEnvFile(path);
                _isLoaded = true;
                break;
            }
        }
    }

    private static void ParseEnvFile(string filePath)
    {
        try
        {
            var lines = File.ReadAllLines(filePath);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                    continue;

                int equalIdx = trimmed.IndexOf('=');
                if (equalIdx > 0)
                {
                    string key = trimmed.Substring(0, equalIdx).Trim();
                    string val = trimmed.Substring(equalIdx + 1).Trim();

                    // 따옴표 제거
                    if ((val.StartsWith("\"") && val.EndsWith("\"")) || (val.StartsWith("'") && val.EndsWith("'")))
                    {
                        if (val.Length >= 2)
                        {
                            val = val.Substring(1, val.Length - 2);
                        }
                    }

                    _envVars[key] = val;
                    Environment.SetEnvironmentVariable(key, val);
                }
            }
        }
        catch { }
    }

    public static string Get(string key, string defaultValue = "")
    {
        if (!_isLoaded)
        {
            Load();
        }

        if (_envVars.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
        {
            return val;
        }

        var sysVal = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(sysVal))
        {
            return sysVal;
        }

        return defaultValue;
    }
}
