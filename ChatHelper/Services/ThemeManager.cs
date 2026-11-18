using System.Windows;

namespace ChatHelper.Services;

public static class ThemeManager
{
    public static event Action? ThemeChanged;

    public static string CurrentTheme { get; private set; } = "Win98";

    public static void ApplyTheme(string themeName)
    {
        CurrentTheme = themeName switch
        {
            "ModernDark" => "ModernDark",
            "ModernLight" => "ModernLight",
            _ => "Win98"
        };

        try
        {
            var dictUri = new Uri($"Themes/{CurrentTheme}Theme.xaml", UriKind.Relative);
            var resourceDict = new ResourceDictionary { Source = dictUri };

            var appResources = Application.Current.Resources;
            appResources.MergedDictionaries.Clear();
            appResources.MergedDictionaries.Add(resourceDict);

            ThemeChanged?.Invoke();
            System.Diagnostics.Debug.WriteLine($"[ThemeManager] 테마 적용 완료: {CurrentTheme}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeManager] 테마 적용 실패: {ex.Message}");
        }
    }
}
