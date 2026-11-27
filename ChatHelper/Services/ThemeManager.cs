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

    /// <summary>
    /// 톤다운된 파스텔 포인트 컬러 팔레트 (흰색에 가까운 부드러운 파스텔 배경 + 마우스 오버 시 더 연한 틴트 + 선명한 짙은 텍스트)
    /// </summary>
    public static (string AccentBg, string HoverBg, string BorderHex, string TextHex) GetAccentPalette(string? accentName)
    {
        return (accentName ?? "Blue").Trim() switch
        {
            "Green" => ("#D1FAE5", "#ECFDF5", "#6EE7B7", "#065F46"),
            "Lavender" => ("#EDE9FE", "#F5F3FF", "#C4B5FD", "#4C1D95"),
            "Peach" => ("#FFEDD5", "#FFF7ED", "#FDBA74", "#7C2D12"),
            "Pink" => ("#FCE7F3", "#FDF2F8", "#F9A8D4", "#831843"),
            _ => ("#DBEAFE", "#EFF6FF", "#93C5FD", "#1E3A8A")
        };
    }

    /// <summary>
    /// 선택한 UI 테마 색상보다 한 단계 더 선명하고 강조된 ChatGENIE 임시메모 전용 팔레트
    /// </summary>
    public static (string Bg, string HeaderBg, string BorderHex, string TextHex, string AddBg) GetEmphasizedMemoPalette(string? accentName)
    {
        return (accentName ?? "Blue").Trim() switch
        {
            "Green" => ("#E6FAF0", "#C6F3DF", "#10B981", "#064E3B", "#A7F3D0"),
            "Lavender" => ("#F2ECFF", "#E2D6FF", "#8B5CF6", "#4C1D95", "#DDD6FE"),
            "Peach" => ("#FEF8E0", "#FDEDB4", "#F59E0B", "#78350F", "#FDE68A"),
            "Pink" => ("#FFEFF7", "#FBD8EA", "#EC4899", "#831843", "#FBCFE8"),
            _ => ("#EBF4FF", "#D2E5FF", "#3B82F6", "#1E3A8A", "#BFDBFE")
        };
    }

    public static void ApplyTheme(string? themeName = ThemeFluentLight, string? accentColor = null)
    {
        try
        {
            string normalized = string.Equals(themeName, ThemeMinimalHud, StringComparison.OrdinalIgnoreCase)
                ? ThemeMinimalHud
                : ThemeFluentLight;

            // 화이트 베이스 Windows 11 Fluent 라이트 고정 + 유저 선택 파스텔 포인트 컬러 적용
            var dictUri = new Uri("Themes/ModernLightTheme.xaml", UriKind.Relative);
            var resourceDict = new ResourceDictionary { Source = dictUri };

            string accentKey = string.IsNullOrWhiteSpace(accentColor) ? CurrentAccent : accentColor.Trim();
            var (accentBg, hoverBg, borderHex, textHex) = GetAccentPalette(accentKey);
            resourceDict["ThemeAccent"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accentBg));
            resourceDict["ThemeAccentHover"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hoverBg));
            resourceDict["ThemeButtonHoverBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hoverBg));
            resourceDict["ThemeAccentBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(borderHex));
            resourceDict["ThemeAccentText"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(textHex));

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
