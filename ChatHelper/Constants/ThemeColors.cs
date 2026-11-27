using System.Windows.Media;

namespace ChatHelper.Constants;

/// <summary>
/// UI 전반에서 반복 사용되는 컬러 및 동결(Frozen) 브러시 상수 모음.
/// </summary>
public static class ThemeColors
{
    public static readonly FontFamily AppFontFamily =
        new(new Uri("pack://application:,,,/ChatGENIE;component/Assets/Fonts/"), "./#Paperlogy");

    public static readonly Brush Win98Navy = CreateFrozenBrush("#000080");
    public static readonly Brush Win98Silver = CreateFrozenBrush("#C0C0C0");
    public static readonly Brush Win98Face = CreateFrozenBrush("#D4D0C8");
    public static readonly Brush Win98BorderGray = CreateFrozenBrush("#808080");
    public static readonly Brush Win98CreamYellow = CreateFrozenBrush("#FFFFE1");

    public static readonly Brush AiPrimaryBlue = CreateFrozenBrush("#1D4ED8");
    public static readonly Brush AiSurfaceBlue = CreateFrozenBrush("#EFF6FF");

    public static readonly Brush StatusGreen = CreateFrozenBrush("#00B000");
    public static readonly Brush StatusAmber = CreateFrozenBrush("#D08000");
    public static readonly Brush StatusDarkGreen = CreateFrozenBrush("#006000");
    public static readonly Brush StatusDarkRed = CreateFrozenBrush("#800000");
    public static readonly Brush InfoBlue = CreateFrozenBrush("#2563EB");
    public static readonly Brush ErrorRed = CreateFrozenBrush("#DC2626");

    private static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
