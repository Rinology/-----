using System.Windows;
using System.Windows.Media;

namespace ChatHelper.Services;

public static class ThemeManager
{
    public const string ThemeFluentLight = "FluentLight";
    public const string ThemeMinimalHud = "MinimalHud";

    public static event Action? ThemeChanged;

    public static string CurrentTheme { get; private set; } = ThemeFluentLight;
    public static string CurrentAccent { get; private set; } = "Blue";

    public static (string Accent, string Hover, string SoftHover) GetAccentPalette(string? accentName)
    {
        return (accentName ?? "Blue").Trim() switch
        {
            "Green" => ("#10B981", "#059669", "#ECFDF5"),
            "Lavender" => ("#8B5CF6", "#7C3AED", "#F5F3FF"),
            "Peach" => ("#F97316", "#EA580C", "#FFF7ED"),
            "Pink" => ("#EC4899", "#DB2777", "#FDF2F8"),
            _ => ("#2563EB", "#1D4ED8", "#EFF6FF")
        };
    }

    public static void ApplyTheme(string? themeName = ThemeFluentLight, string? accentColor = null)
    {
        try
        {
            string normalized = string.Equals(themeName, ThemeMinimalHud, StringComparison.OrdinalIgnoreCase)
                ? ThemeMinimalHud
                : ThemeFluentLight;

            // 화이트 베이스 Windows 11 Fluent 라이트 고정 + 유저 선택 포인트 컬러 적용
            var dictUri = new Uri("Themes/ModernLightTheme.xaml", UriKind.Relative);
            var resourceDict = new ResourceDictionary { Source = dictUri };

            string accentKey = string.IsNullOrWhiteSpace(accentColor) ? CurrentAccent : accentColor.Trim();
            var (accentHex, hoverHex, softHex) = GetAccentPalette(accentKey);
            resourceDict["ThemeAccent"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accentHex));
            resourceDict["ThemeAccentHover"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hoverHex));
            resourceDict["ThemeButtonHoverBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(softHex));

            var appResources = Application.Current.Resources;
            appResources.MergedDictionaries.Clear();
            appResources.MergedDictionaries.Add(resourceDict);

            CurrentTheme = normalized;
            CurrentAccent = accentKey;
            ThemeChanged?.Invoke();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("ThemeManager", "테마 적용 실패", ex);
        }
    }
}
