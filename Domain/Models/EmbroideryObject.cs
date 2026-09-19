namespace AtlasEmbroidery.Domain.Models;

/// <summary>
/// Tipos de objeto de bordado (elementos que contienen puntadas)
/// </summary>
public enum EmbroideryObjectType : byte
{
    Unknown = 0,
    Shape = 1,          // Forma cerrada (polígono, círculo, rectángulo)
    Path = 2,           // Camino abierto (línea, curva)
    Text = 3,           // Texto/Lettering
    Image = 4,          // Imagen rasterizada a puntadas
    Applique = 5,       // Aplicación de tela
    Sequins = 6,        // Lentejuelas
    Manual = 7,         // Puntos editados manualmente
    Group = 100         // Grupo de objetos
}

/// <summary>
/// Objeto base de bordado - contenedor de geometría y parámetros de puntada
/// </summary>
public abstract class EmbroideryObject : ICloneable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public EmbroideryObjectType ObjectType { get; protected set; }
    public StitchParams StitchParams { get; set; } = new();
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; } = false;
    public int SequenceOrder { get; set; } = 0;
    public Rectangle Bounds { get; protected set; } = Rectangle.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    // Geometría base (puntos de control, paths, etc.)
    public abstract IEnumerable<Point> GetControlPoints();
    public abstract void RecalculateBounds();

    public virtual object Clone()
    {
        var clone = (EmbroideryObject)MemberwiseClone();
        clone.Id = Guid.NewGuid();
        clone.StitchParams = StitchParams.DeepClone();
        clone.Metadata = new Dictionary<string, object>(Metadata);
        clone.CreatedAt = DateTime.UtcNow;
        clone.ModifiedAt = DateTime.UtcNow;
        return clone;
    }

    public virtual EmbroideryObject DeepClone() => (EmbroideryObject)Clone();

    public void Touch() => ModifiedAt = DateTime.UtcNow;
}

/// <summary>
/// Objeto forma cerrada (polígono, rectángulo, elipse, etc.)
/// </summary>
public sealed class ShapeObject : EmbroideryObject
{
    public List<Point> Vertices { get; set; } = new();
    public bool IsClosed { get; set; } = true;
    public FillRule FillRule { get; set; } = FillRule.EvenOdd;

    public ShapeObject()
    {
        ObjectType = EmbroideryObjectType.Shape;
    }

    public static ShapeObject CreateRectangle(Rectangle rect, string? name = null) =>
        new()
        {
            Name = name ?? "Rectangle",
            Vertices = new List<Point> { rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft },
            IsClosed = true,
            Bounds = rect
        };

    public static ShapeObject CreateEllipse(Point center, int radiusX, int radiusY, int segments = 32, string? name = null)
    {
        var vertices = new List<Point>();
        for (int i = 0; i < segments; i++)
        {
            double angle = 2 * Math.PI * i / segments;
            int x = center.X + (int)Math.Round(radiusX * Math.Cos(angle));
            int y = center.Y + (int)Math.Round(radiusY * Math.Sin(angle));
            vertices.Add(new Point(x, y));
        }
        return new ShapeObject { Name = name ?? "Ellipse", Vertices = vertices, IsClosed = true };
    }

    public override IEnumerable<Point> GetControlPoints() => Vertices;

    public override void RecalculateBounds()
    {
        if (Vertices.Count == 0)
        {
            Bounds = Rectangle.Empty;
            return;
        }
        int minX = Vertices.Min(v => v.X);
        int minY = Vertices.Min(v => v.Y);
        int maxX = Vertices.Max(v => v.X);
        int maxY = Vertices.Max(v => v.Y);
        Bounds = new Rectangle(minX, minY, maxX - minX, maxY - minY);
    }

    public override object Clone()
    {
        var clone = (ShapeObject)base.Clone();
        clone.Vertices = new List<Point>(Vertices);
        return clone;
    }
}

/// <summary>
/// Objeto camino abierto (línea, curva, bezier)
/// </summary>
public sealed class PathObject : EmbroideryObject
{
    public List<PathSegment> Segments { get; set; } = new();
    public bool IsClosed { get; set; } = false;

    public PathObject()
    {
        ObjectType = EmbroideryObjectType.Path;
    }

    public override IEnumerable<Point> GetControlPoints() =>
        Segments.SelectMany(s => s.GetControlPoints());

    public override void RecalculateBounds()
    {
        var points = GetControlPoints().ToList();
        if (points.Count == 0)
        {
            Bounds = Rectangle.Empty;
            return;
        }
        int minX = points.Min(p => p.X);
        int minY = points.Min(p => p.Y);
        int maxX = points.Max(p => p.X);
        int maxY = points.Max(p => p.Y);
        Bounds = new Rectangle(minX, minY, maxX - minX, maxY - minY);
    }

    public override object Clone()
    {
        var clone = (PathObject)base.Clone();
        clone.Segments = Segments.Select(s => s.DeepClone()).ToList();
        return clone;
    }
}

/// <summary>
/// Segmento de camino (línea, curva cuadrática, curva cúbica, arco)
/// </summary>
public abstract class PathSegment : ICloneable
{
    public Point Start { get; set; }
    public abstract IEnumerable<Point> GetControlPoints();
    public abstract PathSegment DeepClone();
    public abstract IEnumerable<Point> Flatten(double tolerance = 0.5);

    public object Clone() => DeepClone();
}

public sealed class LineSegment : PathSegment
{
    public Point End { get; set; }

    public override IEnumerable<Point> GetControlPoints() => new[] { Start, End };

    public override PathSegment DeepClone() => new LineSegment { Start = Start, End = End };

    public override IEnumerable<Point> Flatten(double tolerance = 0.5) => new[] { Start, End };
}

public sealed class QuadraticBezierSegment : PathSegment
{
    public Point Control { get; set; }
    public Point End { get; set; }

    public override IEnumerable<Point> GetControlPoints() => new[] { Start, Control, End };

    public override PathSegment DeepClone() => new QuadraticBezierSegment { Start = Start, Control = Control, End = End };

    public override IEnumerable<Point> Flatten(double tolerance = 0.5)
    {
        // Adaptive flattening using De Casteljau
        var result = new List<Point> { Start };
        FlattenRecursive(Start, Control, End, tolerance, result);
        return result;
    }

    private static void FlattenRecursive(Point p0, Point p1, Point p2, double tolerance, List<Point> result)
    {
        Point q0 = new((p0.X + p1.X) / 2, (p0.Y + p1.Y) / 2);
        Point q1 = new((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
        Point r = new((q0.X + q1.X) / 2, (q0.Y + q1.Y) / 2);

        double dx = p2.X - p0.X;
        double dy = p2.Y - p0.Y;
        double dist = Math.Abs(dx * (p1.Y - p0.Y) - dy * (p1.X - p0.X)) / Math.Sqrt(dx * dx + dy * dy);

        if (dist > tolerance)
        {
            FlattenRecursive(p0, q0, r, tolerance, result);
            FlattenRecursive(r, q1, p2, tolerance, result);
        }
        else
        {
            result.Add(p2);
        }
    }
}

public sealed class CubicBezierSegment : PathSegment
{
    public Point Control1 { get; set; }
    public Point Control2 { get; set; }
    public Point End { get; set; }

    public override IEnumerable<Point> GetControlPoints() => new[] { Start, Control1, Control2, End };

    public override PathSegment DeepClone() => new CubicBezierSegment { Start = Start, Control1 = Control1, Control2 = Control2, End = End };

    public override IEnumerable<Point> Flatten(double tolerance = 0.5)
    {
        var result = new List<Point> { Start };
        FlattenRecursive(Start, Control1, Control2, End, tolerance, result);
        return result;
    }

    private static void FlattenRecursive(Point p0, Point p1, Point p2, Point p3, double tolerance, List<Point> result)
    {
        Point q0 = new((p0.X + p1.X) / 2, (p0.Y + p1.Y) / 2);
        Point q1 = new((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
        Point q2 = new((p2.X + p3.X) / 2, (p2.Y + p3.Y) / 2);
        Point r0 = new((q0.X + q1.X) / 2, (q0.Y + q1.Y) / 2);
        Point r1 = new((q1.X + q2.X) / 2, (q1.Y + q2.Y) / 2);
        Point s = new((r0.X + r1.X) / 2, (r0.Y + r1.Y) / 2);

        double dx = p3.X - p0.X;
        double dy = p3.Y - p0.Y;
        double dist1 = Math.Abs(dx * (p1.Y - p0.Y) - dy * (p1.X - p0.X)) / Math.Sqrt(dx * dx + dy * dy);
        double dist2 = Math.Abs(dx * (p2.Y - p0.Y) - dy * (p2.X - p0.X)) / Math.Sqrt(dx * dx + dy * dy);
        double dist = Math.Max(dist1, dist2);

        if (dist > tolerance)
        {
            FlattenRecursive(p0, q0, r0, s, tolerance, result);
            FlattenRecursive(s, r1, q2, p3, tolerance, result);
        }
        else
        {
            result.Add(p3);
        }
    }
}

public enum FillRule : byte
{
    EvenOdd = 0,
    NonZero = 1
}