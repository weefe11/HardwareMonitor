using System.Windows.Media;


using Color = System.Windows.Media.Color;
namespace HardwareMonitor;

public static class ColorSpectrum
{
    public static Color FromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1); value = Math.Clamp(value, 0, 1);
        double c = value * saturation, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = value - c;
        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x),
            < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x)
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
    public static (double Hue, double Saturation, double Value) ToHsv(Color color)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        double hue = delta == 0 ? 0 : max == r ? 60 * ((g - b) / delta % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        return ((hue + 360) % 360, max == 0 ? 0 : delta / max, max);
    }
    public static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    public static bool TryHex(string? text, out Color color)
    {
        color = Colors.White;
        if (text == null || text.Length != 7 || text[0] != '#' || !uint.TryParse(text.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out uint rgb)) return false;
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb); return true;
    }
}
