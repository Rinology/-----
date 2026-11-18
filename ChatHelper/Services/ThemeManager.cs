using System.Windows;

namespace ChatHelper.Services;

public static class ThemeManager
{
    public static event Action? ThemeChanged;

    public static string CurrentTheme => "Win98";

    public static void ApplyTheme(string themeName = "Win98")
    {
        try
        {
            var dictUri = new Uri("Themes/Win98Theme.xaml", UriKind.Relative);
            var resourceDict = new ResourceDictionary { Source = dictUri };

            var appResources = Application.Current.Resources;
            appResources.MergedDictionaries.Clear();
            appResources.MergedDictionaries.Add(resourceDict);

            ThemeChanged?.Invoke();
            System.Diagnostics.Debug.WriteLine("[ThemeManager] Windows 98 ME 클래식 테마 적용 완료");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeManager] 테마 적용 실패: {ex.Message}");
        }
    }
}
