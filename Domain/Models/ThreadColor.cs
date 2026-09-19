namespace AtlasEmbroidery.Domain.Models;

/// <summary>
/// Color de hilo con información de marca y código.
/// Inmutable, serializable a JSON.
/// </summary>
public readonly record struct ThreadColor(
    byte R,
    byte G,
    byte B,
    string Brand = "",
    string Code = "",
    string Name = "",
    string? Description = null)
{
    public static readonly ThreadColor Black = new(0, 0, 0, "Generic", "000", "Black");
    public static readonly ThreadColor White = new(255, 255, 255, "Generic", "FFF", "White");
    public static readonly ThreadColor Red = new(255, 0, 0, "Generic", "F00", "Red");
    public static readonly ThreadColor Green = new(0, 255, 0, "Generic", "0F0", "Green");
    public static readonly ThreadColor Blue = new(0, 0, 255, "Generic", "00F", "Blue");
    public static readonly ThreadColor Yellow = new(255, 255, 0, "Generic", "FF0", "Yellow");
    public static readonly ThreadColor Magenta = new(255, 0, 255, "Generic", "F0F", "Magenta");
    public static readonly ThreadColor Cyan = new(0, 255, 255, "Generic", "0FF", "Cyan");

    public uint Argb => (uint)((255 << 24) | (R << 16) | (G << 8) | B);
    public uint Rgb => (uint)((R << 16) | (G << 8) | B);

    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    public double Luminance => 0.299 * R + 0.587 * G + 0.114 * B;
    public bool IsDark => Luminance < 128;

    public ThreadColor WithAlpha(byte alpha) => this; // ThreadColor no tiene alpha, es opaco

    public static ThreadColor FromArgb(uint argb) =>
        new((byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));

    public static ThreadColor FromHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Black;

        var h = hex.TrimStart('#');
        if (h.Length == 6)
        {
            return new ThreadColor(
                byte.Parse(h.Substring(0, 2), System.Globalization.NumberStyles.HexNumber),
                byte.Parse(h.Substring(2, 2), System.Globalization.NumberStyles.HexNumber),
                byte.Parse(h.Substring(4, 2), System.Globalization.NumberStyles.HexNumber));
        }
        if (h.Length == 3)
        {
            return new ThreadColor(
                byte.Parse(h[0].ToString() + h[0], System.Globalization.NumberStyles.HexNumber),
                byte.Parse(h[1].ToString() + h[1], System.Globalization.NumberStyles.HexNumber),
                byte.Parse(h[2].ToString() + h[2], System.Globalization.NumberStyles.HexNumber));
        }
        return Black;
    }

    public double DistanceTo(ThreadColor other)
    {
        int dr = R - other.R;
        int dg = G - other.G;
        int db = B - other.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    public ThreadColor Blend(ThreadColor other, double t) =>
        new(
            (byte)Math.Round(R + (other.R - R) * t),
            (byte)Math.Round(G + (other.G - G) * t),
            (byte)Math.Round(B + (other.B - B) * t),
            Brand, Code, Name);

    public override string ToString() =>
        string.IsNullOrEmpty(Brand) ? Hex : $"{Brand} {Code} ({Name}) - {Hex}";
}