namespace AtlasEmbroidery.Domain.Geometry;

using AtlasEmbroidery.Domain.Models;

/// <summary>
/// Operaciones geométricas básicas para bordado
/// </summary>
public static class GeometryUtils
{
    /// <summary>
    /// Calcula el área de un polígono (algoritmo de la zapata)
    /// Retorna área positiva si vértices en sentido antihorario
    /// </summary>
    public static double PolygonArea(IReadOnlyList<Point> vertices)
    {
        if (vertices.Count < 3) return 0;
        double area = 0;
        for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
        {
            area += (vertices[j].X + vertices[i].X) * (double)(vertices[j].Y - vertices[i].Y);
        }
        return Math.Abs(area) / 2.0;
    }

    /// <summary>
    /// Determina si un punto está dentro de un polígono (ray casting)
    /// </summary>
    public static bool PointInPolygon(Point p, IReadOnlyList<Point> polygon)
    {
        if (polygon.Count < 3) return false;
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];
            bool intersect = ((pi.Y > p.Y) != (pj.Y > p.Y)) &&
                (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / (double)(pj.Y - pi.Y) + pi.X);
            if (intersect) inside = !inside;
        }
        return inside;
    }

    /// <summary>
    /// Calcula el centroide de un polígono
    /// </summary>
    public static Point PolygonCentroid(IReadOnlyList<Point> vertices)
    {
        if (vertices.Count == 0) return Point.Zero;
        if (vertices.Count == 1) return vertices[0];
        if (vertices.Count == 2) return new Point((vertices[0].X + vertices[1].X) / 2, (vertices[0].Y + vertices[1].Y) / 2);

        double cx = 0, cy = 0;
        double area = 0;
        for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
        {
            double cross = vertices[j].X * vertices[i].Y - vertices[i].X * vertices[j].Y;
            area += cross;
            cx += (vertices[j].X + vertices[i].X) * cross;
            cy += (vertices[j].Y + vertices[i].Y) * cross;
        }
        area *= 0.5;
        if (Math.Abs(area) < 1e-10) return new Point(
            (int)Math.Round(vertices.Average(v => v.X)),
            (int)Math.Round(vertices.Average(v => v.Y)));
        cx /= (6 * area);
        cy /= (6 * area);
        return new Point((int)Math.Round(cx), (int)Math.Round(cy));
    }

    /// <summary>
    /// Simplifica un polígono usando algoritmo Douglas-Peucker
    /// </summary>
    public static List<Point> SimplifyPolygon(IReadOnlyList<Point> points, double tolerance)
    {
        if (points.Count <= 2) return points.ToList();
        var result = new List<Point>();
        SimplifyDP(points, 0, points.Count - 1, tolerance, result);
        
        // Ensure the last point is included
        if (result.Count == 0 || !result[^1].Equals(points[^1]))
        {
            result.Add(points[^1]);
        }
        
        return result;
    }

    private static void SimplifyDP(IReadOnlyList<Point> points, int start, int end, double tolerance, List<Point> result)
    {
        if (end <= start + 1) return;

        double maxDist = 0;
        int maxIndex = start;

        Point pStart = points[start];
        Point pEnd = points[end];

        double dx = pEnd.X - pStart.X;
        double dy = pEnd.Y - pStart.Y;
        double lenSq = dx * dx + dy * dy;

        for (int i = start + 1; i < end; i++)
        {
            double dist;
            if (lenSq == 0)
            {
                dist = Math.Sqrt(Math.Pow(points[i].X - pStart.X, 2) + Math.Pow(points[i].Y - pStart.Y, 2));
            }
            else
            {
                double t = ((points[i].X - pStart.X) * dx + (points[i].Y - pStart.Y) * dy) / lenSq;
                t = Math.Clamp(t, 0, 1);
                double projX = pStart.X + t * dx;
                double projY = pStart.Y + t * dy;
                dist = Math.Sqrt(Math.Pow(points[i].X - projX, 2) + Math.Pow(points[i].Y - projY, 2));
            }
            if (dist > maxDist)
            {
                maxDist = dist;
                maxIndex = i;
            }
        }

        if (maxDist > tolerance)
        {
            SimplifyDP(points, start, maxIndex, tolerance, result);
            result.Add(points[maxIndex]);
            SimplifyDP(points, maxIndex, end, tolerance, result);
        }
        else
        {
            // For collinear points, add the start point to maintain the path
            if (result.Count == 0 || !result[^1].Equals(points[start]))
            {
                result.Add(points[start]);
            }
        }
    }

    /// <summary>
    /// Calcula distancia punto a segmento
    /// </summary>
    public static double DistancePointToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        if (dx == 0 && dy == 0) return p.DistanceTo(a);

        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);
        t = Math.Clamp(t, 0, 1);
        double projX = a.X + t * dx;
        double projY = a.Y + t * dy;
        return Math.Sqrt(Math.Pow(p.X - projX, 2) + Math.Pow(p.Y - projY, 2));
    }

    /// <summary>
    /// Offset de polígono (inset/outset) - versión simple
    /// Para producción usar Clipper library
    /// </summary>
    public static List<Point> OffsetPolygon(IReadOnlyList<Point> polygon, int offsetDistance)
    {
        // Placeholder - implementación completa requiere Clipper o similar
        // Por ahora retorna original
        return polygon.ToList();
    }

    /// <summary>
    /// Intersección de dos segmentos de línea
    /// </summary>
    public static bool LineSegmentsIntersect(Point p1, Point p2, Point p3, Point p4, out Point intersection)
    {
        intersection = Point.Empty;

        double x1 = p1.X, y1 = p1.Y;
        double x2 = p2.X, y2 = p2.Y;
        double x3 = p3.X, y3 = p3.Y;
        double x4 = p4.X, y4 = p4.Y;

        double denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
        if (Math.Abs(denom) < 1e-10) return false; // Paralelas

        double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / denom;
        double u = -((x1 - x2) * (y1 - y3) - (y1 - y2) * (x1 - x3)) / denom;

        if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
        {
            intersection = new Point(
                (int)Math.Round(x1 + t * (x2 - x1)),
                (int)Math.Round(y1 + t * (y2 - y1)));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Aproxima un arco con segmentos de línea
    /// </summary>
    public static List<Point> ApproximateArc(Point center, int radiusX, int radiusY,
        double startAngle, double sweepAngle, int maxSegments = 32)
    {
        var points = new List<Point>();
        int segments = Math.Max(2, Math.Min(maxSegments, (int)Math.Ceiling(Math.Abs(sweepAngle) / (Math.PI / 16))));
        for (int i = 0; i <= segments; i++)
        {
            double angle = startAngle + sweepAngle * i / segments;
            int x = center.X + (int)Math.Round(radiusX * Math.Cos(angle));
            int y = center.Y + (int)Math.Round(radiusY * Math.Sin(angle));
            points.Add(new Point(x, y));
        }
        return points;
    }

    /// <summary>
    /// Bounding box de una lista de puntos
    /// </summary>
    public static Rectangle BoundingBox(IEnumerable<Point> points)
    {
        var pts = points.ToList();
        if (pts.Count == 0) return Rectangle.Empty;
        int minX = pts.Min(p => p.X);
        int minY = pts.Min(p => p.Y);
        int maxX = pts.Max(p => p.X);
        int maxY = pts.Max(p => p.Y);
        return new Rectangle(minX, minY, maxX - minX, maxY - minY);
    }

    // Alias para compatibilidad
    public static Rectangle ComputeBounds(IEnumerable<Point> points) => BoundingBox(points);

    /// <summary>
    /// Transforma puntos con matriz 2D simple
    /// </summary>
    public static List<Point> TransformPoints(IEnumerable<Point> points,
        double m11, double m12, double m21, double m22, double dx, double dy)
    {
        return points.Select(p => new Point(
            (int)Math.Round(p.X * m11 + p.Y * m12 + dx),
            (int)Math.Round(p.X * m21 + p.Y * m22 + dy))).ToList();
    }

    /// <summary>
    /// Rota puntos alrededor de un centro
    /// </summary>
    public static List<Point> RotatePoints(IEnumerable<Point> points, Point center, double angleRadians)
    {
        double cos = Math.Cos(angleRadians);
        double sin = Math.Sin(angleRadians);
        return points.Select(p =>
        {
            int dx = p.X - center.X;
            int dy = p.Y - center.Y;
            return new Point(
                center.X + (int)Math.Round(dx * cos - dy * sin),
                center.Y + (int)Math.Round(dx * sin + dy * cos));
        }).ToList();
    }

    /// <summary>
    /// Escala puntos desde un centro
    /// </summary>
    public static List<Point> ScalePoints(IEnumerable<Point> points, Point center, double scaleX, double scaleY)
    {
        return points.Select(p => new Point(
            center.X + (int)Math.Round((p.X - center.X) * scaleX),
            center.Y + (int)Math.Round((p.Y - center.Y) * scaleY))).ToList();
    }
}