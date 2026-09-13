using System.Windows;
using System.Windows.Media;

namespace CompanyCodeAgent.VisualStudio;

/// <summary>Central design tokens for the IDE-native agent surface.</summary>
internal static class AgentUiTheme
{
    public static readonly Brush Background = Brush("#181818");
    public static readonly Brush Surface = Brush("#202020");
    public static readonly Brush Elevated = Brush("#242424");
    public static readonly Brush Hover = Brush("#2A2A2A");
    public static readonly Brush Border = Brush("#343434");
    public static readonly Brush Text = Brush("#F2F2F2");
    public static readonly Brush SecondaryText = Brush("#B0B0B0");
    public static readonly Brush MutedText = Brush("#858585");
    public static readonly Brush Accent = Brush("#6D62D9");
    public static readonly Brush Success = Brush("#8FC69A");
    public static readonly Brush Error = Brush("#E28B8B");
    public static readonly Brush DiffAdded = Brush("#1D3827");
    public static readonly Brush DiffRemoved = Brush("#3C2225");

    public static readonly Thickness Spacing1 = new(4);
    public static readonly Thickness Spacing2 = new(8);
    public static readonly CornerRadius RadiusSmall = new(6);
    public static readonly CornerRadius RadiusMedium = new(10);

    private static SolidColorBrush Brush(string value)
    {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    }
}
