namespace AtlasEmbroidery.Domain.Models;

/// <summary>
/// Punto 2D con coordenadas enteras (micras o décimas de mm para precisión).
/// Inmutable para thread-safety y uso en collections.
/// </summary>
public readonly record struct Point(int X, int Y)
{
    public static readonly Point Zero = new(0, 0);
    public static readonly Point Empty = new(int.MinValue, int.MinValue);

    public bool IsEmpty => X == int.MinValue && Y == int.MinValue;

    public double DistanceTo(Point other) =>
        Math.Sqrt(Math.Pow(other.X - X, 2) + Math.Pow(other.Y - Y, 2));

    public Point Translate(int dx, int dy) => new(X + dx, Y + dy);
    public Point Scale(double factor) => new((int)Math.Round(X * factor), (int)Math.Round(Y * factor));
    public Point Rotate90() => new(-Y, X);
    public Point Rotate180() => new(-X, -Y);
    public Point Rotate270() => new(Y, -X);

    public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);
    public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);
    public static Point operator *(Point a, int scalar) => new(a.X * scalar, a.Y * scalar);
}