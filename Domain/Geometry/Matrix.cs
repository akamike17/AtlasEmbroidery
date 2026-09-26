namespace AtlasEmbroidery.Domain.Geometry;

using AtlasEmbroidery.Domain.Models;
using System;

/// <summary>
/// 2D transformation matrix
/// </summary>
public readonly struct Matrix
{
    public readonly double M11, M12, M21, M22, OffsetX, OffsetY;

    public Matrix(double m11, double m12, double m21, double m22, double offsetX, double offsetY)
    {
        M11 = m11; M12 = m12; M21 = m21; M22 = m22; OffsetX = offsetX; OffsetY = offsetY;
    }

    public static Matrix Identity => new(1, 0, 0, 1, 0, 0);

    public static Matrix CreateTranslation(double x, double y) => new(1, 0, 0, 1, x, y);
    public static Matrix CreateScale(double scale) => new(scale, 0, 0, scale, 0, 0);
    public static Matrix CreateScale(double scaleX, double scaleY) => new(scaleX, 0, 0, scaleY, 0, 0);

    public static Matrix operator *(Matrix a, Matrix b)
    {
        return new Matrix(
            a.M11 * b.M11 + a.M12 * b.M21,
            a.M11 * b.M12 + a.M12 * b.M22,
            a.M21 * b.M11 + a.M22 * b.M21,
            a.M21 * b.M12 + a.M22 * b.M22,
            a.M11 * b.OffsetX + a.M12 * b.OffsetY + a.OffsetX,
            a.M21 * b.OffsetX + a.M22 * b.OffsetY + a.OffsetY
        );
    }

    public Point Transform(Point p) => new(
        (int)Math.Round(M11 * p.X + M12 * p.Y + OffsetX),
        (int)Math.Round(M21 * p.X + M22 * p.Y + OffsetY)
    );

    public Rectangle TransformRect(Rectangle r)
    {
        var tl = Transform(new Point(r.X, r.Y));
        var tr = Transform(new Point(r.Right, r.Y));
        var bl = Transform(new Point(r.X, r.Bottom));
        var br = Transform(new Point(r.Right, r.Bottom));

        int minX = Math.Min(Math.Min(tl.X, tr.X), Math.Min(bl.X, br.X));
        int minY = Math.Min(Math.Min(tl.Y, tr.Y), Math.Min(bl.Y, br.Y));
        int maxX = Math.Max(Math.Max(tl.X, tr.X), Math.Max(bl.X, br.X));
        int maxY = Math.Max(Math.Max(tl.Y, tr.Y), Math.Max(bl.Y, br.Y));

        return new Rectangle(minX, minY, maxX - minX, maxY - minY);
    }

    public Matrix Invert()
    {
        double det = M11 * M22 - M12 * M21;
        if (Math.Abs(det) < 1e-10) return Identity;

        double invDet = 1.0 / det;
        return new Matrix(
            M22 * invDet, -M12 * invDet,
            -M21 * invDet, M11 * invDet,
            (M21 * OffsetY - M22 * OffsetX) * invDet,
            (M12 * OffsetX - M11 * OffsetY) * invDet
        );
    }
}

/// <summary>
/// Rectangle extensions
/// </summary>
public static class RectangleExtensions
{
    public static bool Contains(this Rectangle r, Point p) =>
        p.X >= r.X && p.X < r.Right && p.Y >= r.Y && p.Y < r.Bottom;

    public static bool Intersects(this Rectangle a, Rectangle b) =>
        a.X < b.Right && a.Right > b.X && a.Y < b.Bottom && a.Bottom > b.Y;
}