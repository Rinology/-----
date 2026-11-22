using System.Windows;

namespace ChatHelper.Services;

public static class ThemeManager
{
    public const string ThemeFluentLight = "FluentLight";
    public const string ThemeMinimalHud = "MinimalHud";

    public static event Action? ThemeChanged;

    public static string CurrentTheme { get; private set; } = ThemeFluentLight;

    public static void ApplyTheme(string? themeName = ThemeFluentLight)
    {
        try
        {
            string normalized = string.Equals(themeName, ThemeMinimalHud, StringComparison.OrdinalIgnoreCase)
                ? ThemeMinimalHud
                : ThemeFluentLight;

            string file = normalized == ThemeMinimalHud
                ? "Themes/MinimalHudTheme.xaml"
                : "Themes/ModernLightTheme.xaml";

            var dictUri = new Uri(file, UriKind.Relative);
            var resourceDict = new ResourceDictionary { Source = dictUri };

            var appResources = Application.Current.Resources;
            appResources.MergedDictionaries.Clear();
            appResources.MergedDictionaries.Add(resourceDict);

            CurrentTheme = normalized;
            ThemeChanged?.Invoke();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("ThemeManager", "테마 적용 실패", ex);
        }
    }
}
