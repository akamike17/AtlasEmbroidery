namespace AtlasEmbroidery.Domain.Models;

/// <summary>
/// Rectángulo inmutable definido por esquina superior-izquierda y tamaño.
/// Coordenadas en micras para precisión sub-milimétrica.
/// </summary>
public readonly record struct Rectangle(int X, int Y, int Width, int Height)
{
    public static readonly Rectangle Empty = new(0, 0, 0, 0);

    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;
    public Point Center => new(CenterX, CenterY);
    public Point TopLeft => new(X, Y);
    public Point TopRight => new(Right, Y);
    public Point BottomLeft => new(X, Bottom);
    public Point BottomRight => new(Right, Bottom);
    public Point Size => new(Width, Height);
    public long Area => (long)Width * Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public bool IsValid => Width > 0 && Height > 0;

    public bool Contains(Point p) =>
        p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;

    public bool Contains(Rectangle other) =>
        other.X >= X && other.Right <= Right && other.Y >= Y && other.Bottom <= Bottom;

    public bool IntersectsWith(Rectangle other) =>
        X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;

    public Rectangle Intersect(Rectangle other)
    {
        int left = Math.Max(X, other.X);
        int top = Math.Max(Y, other.Y);
        int right = Math.Min(Right, other.Right);
        int bottom = Math.Min(Bottom, other.Bottom);

        if (left >= right || top >= bottom)
            return Empty;

        return new Rectangle(left, top, right - left, bottom - top);
    }

    public Rectangle Union(Rectangle other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;

        int left = Math.Min(X, other.X);
        int top = Math.Min(Y, other.Y);
        int right = Math.Max(Right, other.Right);
        int bottom = Math.Max(Bottom, other.Bottom);

        return new Rectangle(left, top, right - left, bottom - top);
    }

    public Rectangle Inflate(int dx, int dy) =>
        new(X - dx, Y - dy, Width + 2 * dx, Height + 2 * dy);

    public Rectangle Offset(int dx, int dy) =>
        new(X + dx, Y + dy, Width, Height);

    public Rectangle Scale(double factor) =>
        new((int)Math.Round(X * factor), (int)Math.Round(Y * factor),
            (int)Math.Round(Width * factor), (int)Math.Round(Height * factor));

    public override string ToString() => $"Rect[{X},{Y} {Width}x{Height}]";
}