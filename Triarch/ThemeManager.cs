using System.Windows;
using System.Windows.Media;

namespace Triarch;

public static class ThemeManager
{
	private static readonly (object Key, string Dark, string Light)[] Palette = new (object, string, string)[29]
	{
		("Bg", "#0B0D13", "#F4F6FA"),
		("Panel", "#10131C", "#EBEEF5"),
		("Card", "#151924", "#FFFFFF"),
		("CardElevated", "#1B2030", "#F8FAFC"),
		("Hover", "#1E2434", "#E2E7F1"),
		("Line", "#22283A", "#DCE1EC"),
		("LineStrong", "#323B54", "#B8C1D4"),
		("WindowBorder", "#283046", "#CBD2E1"),
		("Fg", "#F3F5F9", "#0F1420"),
		("Muted", "#949EB5", "#4E596F"),
		("Faint", "#58627A", "#7B869E"),
		("Ink", "#0B0D13", "#FFFFFF"),
		("Accent", "#0EA5E9", "#0284C7"),
		("AccentHover", "#38BDF8", "#0369A1"),
		("AccentSoft", "#260EA5E9", "#1F0284C7"),
		("AccentBorder", "#550EA5E9", "#440284C7"),
		("AccentFg", "#FFFFFF", "#FFFFFF"),
		("Success", "#10B981", "#059669"),
		("SuccessSoft", "#2210B981", "#1A059669"),
		("Warning", "#F59E0B", "#D97706"),
		("WarningSoft", "#22F59E0B", "#1AD97706"),
		("Danger", "#EF4444", "#DC2626"),
		("DangerSoft", "#22EF4444", "#1ADC2626"),
		("ThumbBrush", "#2A3248", "#C5CEE0"),
		("ThumbHover", "#455273", "#9AA6BF"),
		("ThumbDrag", "#0EA5E9", "#0284C7"),
		(SystemColors.ControlBrushKey, "#0B0D13", "#F4F6FA"),
		(SystemColors.HighlightBrushKey, "#1E2434", "#E2E7F1"),
		(SystemColors.InactiveSelectionHighlightBrushKey, "#1E2434", "#E2E7F1")
	};

	public static bool IsLight { get; private set; }

	public static void Apply(bool light)
	{
		IsLight = light;
		ResourceDictionary res = Application.Current.Resources;
		(object, string, string)[] palette = Palette;
		for (int i = 0; i < palette.Length; i++)
		{
			(object, string, string) tuple = palette[i];
			object key = tuple.Item1;
			string dark = tuple.Item2;
			string lightHex = tuple.Item3;
			SolidColorBrush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? lightHex : dark));
			brush.Freeze();
			res[key] = brush;
		}
	}
}
