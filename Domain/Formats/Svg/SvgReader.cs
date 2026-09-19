namespace AtlasEmbroidery.Domain.Formats.Svg;

using System.IO;
using System.Xml.Linq;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Formats.Dst;

/// <summary>
/// SVG Reader - Lee SVG y convierte a objetos de bordado
/// Soporta: rect, circle, ellipse, path, polygon, polyline, line
/// </summary>
public sealed class SvgReader : IFormatAdapter
{
    public string FormatName => "SVG";
    public string FileExtension => ".svg";
    public string MimeType => "image/svg+xml";
    public FormatCapabilities Capabilities => new()
    {
        SupportsReading = true,
        SupportsWriting = false, // No writing SVG for now
        SupportsTrim = false,
        SupportsJump = false,
        SupportsColorChange = false,
        SupportsStop = false,
        MaxColors = 256,
    };

    /// <summary>
    /// Lee un archivo SVG y convierte a AtlasProject
    /// </summary>
    public async Task<AtlasProject> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(ct);
        
        return ParseSvg(content, ct);
    }

    public Task WriteAsync(AtlasProject project, Stream stream, CancellationToken ct = default)
    {
        throw new NotImplementedException("SVG writing not implemented yet");
    }

    public AtlasProject Normalize(AtlasProject project) => project;

    public Task<RoundTripResult> RoundTripTestAsync(Stream originalStream, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public List<SemanticDifference> SemanticDiff(AtlasProject a, AtlasProject b) => new();

    public byte[] GenerateFuzzInput(int seed = 0) => Array.Empty<byte>();

    /// <summary>
    /// Parsea contenido SVG a AtlasProject
    /// </summary>
    public AtlasProject ParseSvg(string svgContent, CancellationToken ct = default)
    {
        var doc = XDocument.Parse(svgContent);
        var svg = doc.Root;
        
        if (svg == null || svg.Name.LocalName != "svg")
            throw new InvalidDataException("Invalid SVG: missing root svg element");

        var project = new AtlasProject
        {
            Name = "Imported SVG",
            SourceFilePath = "imported.svg"
        };

        // Parse viewBox or width/height for canvas size
        ParseCanvasSize(svg, project);

        // Parse shapes
        foreach (var element in svg.Descendants())
        {
            ct.ThrowIfCancellationRequested();
            
            var shapeObj = ParseElement(element);
            if (shapeObj != null)
            {
                project.Objects.Add(shapeObj);
            }
        }

        // Build thread palette from fill/stroke colors
        BuildThreadPalette(project);

        project.RecalculateBounds();
        project.Touch();

        return project;
    }

    private void ParseCanvasSize(XElement svg, AtlasProject project)
    {
        // Try viewBox first
        var viewBox = svg.Attribute("viewBox")?.Value;
        if (!string.IsNullOrEmpty(viewBox))
        {
            var parts = viewBox.Split(' ', ',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4)
            {
                double x = double.Parse(parts[0]);
                double y = double.Parse(parts[1]);
                double w = double.Parse(parts[2]);
                double h = double.Parse(parts[3]);
                
                project.CanvasWidth = (int)(w * 1000); // Convert to microns (assuming mm)
                project.CanvasHeight = (int)(h * 1000);
                project.CanvasOrigin = new Point((int)(x * 1000), (int)(y * 1000));
                return;
            }
        }

        // Fallback to width/height attributes
        var widthAttr = svg.Attribute("width")?.Value;
        var heightAttr = svg.Attribute("height")?.Value;
        
        if (!string.IsNullOrEmpty(widthAttr) && !string.IsNullOrEmpty(heightAttr))
        {
            project.CanvasWidth = ParseSvgLength(widthAttr);
            project.CanvasHeight = ParseSvgLength(heightAttr);
        }
        else
        {
            // Default 400mm x 400mm
            project.CanvasWidth = 400000;
            project.CanvasHeight = 400000;
        }
    }

    private int ParseSvgLength(string value)
    {
        // Remove units (px, mm, cm, in, pt, pc, %)
        value = value.Trim();
        var unitStart = 0;
        while (unitStart < value.Length && (char.IsDigit(value[unitStart]) || value[unitStart] == '.' || value[unitStart] == '-'))
            unitStart++;
        
        var numberPart = value.Substring(0, unitStart);
        var unitPart = value.Substring(unitStart).ToLowerInvariant();
        
        if (!double.TryParse(numberPart, out double number))
            return 400000;

        return unitPart switch
        {
            "mm" => (int)(number * 1000),
            "cm" => (int)(number * 10000),
            "in" => (int)(number * 25400),
            "pt" => (int)(number * 352.777),
            "pc" => (int)(number * 4233.33),
            "px" => (int)(number * 264.583), // 96 DPI
            _ => (int)(number * 1000) // Assume mm
        };
    }

    private EmbroideryObject? ParseElement(XElement element)
    {
        var localName = element.Name.LocalName.ToLowerInvariant();
        
        return localName switch
        {
            "rect" => ParseRect(element),
            "circle" => ParseCircle(element),
            "ellipse" => ParseEllipse(element),
            "path" => ParsePath(element),
            "polygon" => ParsePolygon(element),
            "polyline" => ParsePolyline(element),
            "line" => ParseLine(element),
            _ => null
        };
    }

    private ShapeObject? ParseRect(XElement rect)
    {
        double x = ParseDouble(rect.Attribute("x")?.Value) ?? 0;
        double y = ParseDouble(rect.Attribute("y")?.Value) ?? 0;
        double width = ParseDouble(rect.Attribute("width")?.Value) ?? 0;
        double height = ParseDouble(rect.Attribute("height")?.Value) ?? 0;

        if (width <= 0 || height <= 0) return null;

        var rx = ParseDouble(rect.Attribute("rx")?.Value) ?? 0;
        var ry = ParseDouble(rect.Attribute("ry")?.Value) ?? 0;

        var rectObj = new ShapeObject
        {
            Name = rect.Attribute("id")?.Value ?? "rect",
            IsClosed = true
        };

        int ix = (int)(x * 1000);
        int iy = (int)(y * 1000);
        int iw = (int)(width * 1000);
        int ih = (int)(height * 1000);

        if (rx > 0 || ry > 0)
        {
            // Rounded rect - approximate with ellipse corners
            // For simplicity, create as regular rect
            rectObj.Vertices = new List<Point>
            {
                new(ix, iy),
                new(ix + iw, iy),
                new(ix + iw, iy + ih),
                new(ix, iy + ih)
            };
        }
        else
        {
            rectObj.Vertices = new List<Point>
            {
                new(ix, iy),
                new(ix + iw, iy),
                new(ix + iw, iy + ih),
                new(ix, iy + ih)
            };
        }

        rectObj.RecalculateBounds();
        ApplyStyle(rect, rectObj);
        return rectObj;
    }

    private ShapeObject? ParseCircle(XElement circle)
    {
        double cx = ParseDouble(circle.Attribute("cx")?.Value) ?? 0;
        double cy = ParseDouble(circle.Attribute("cy")?.Value) ?? 0;
        double r = ParseDouble(circle.Attribute("r")?.Value) ?? 0;

        if (r <= 0) return null;

        var circleObj = ShapeObject.CreateEllipse(
            new Point((int)(cx * 1000), (int)(cy * 1000)),
            (int)(r * 1000),
            (int)(r * 1000),
            32,
            circle.Attribute("id")?.Value ?? "circle"
        );

        ApplyStyle(circle, circleObj);
        return circleObj;
    }

    private ShapeObject? ParseEllipse(XElement ellipse)
    {
        double cx = ParseDouble(ellipse.Attribute("cx")?.Value) ?? 0;
        double cy = ParseDouble(ellipse.Attribute("cy")?.Value) ?? 0;
        double rx = ParseDouble(ellipse.Attribute("rx")?.Value) ?? 0;
        double ry = ParseDouble(ellipse.Attribute("ry")?.Value) ?? 0;

        if (rx <= 0 || ry <= 0) return null;

        var ellipseObj = ShapeObject.CreateEllipse(
            new Point((int)(cx * 1000), (int)(cy * 1000)),
            (int)(rx * 1000),
            (int)(ry * 1000),
            32,
            ellipse.Attribute("id")?.Value ?? "ellipse"
        );

        ApplyStyle(ellipse, ellipseObj);
        return ellipseObj;
    }

    private EmbroideryObject? ParsePath(XElement path)
    {
        var d = path.Attribute("d")?.Value;
        if (string.IsNullOrEmpty(d)) return null;

        var pathObj = new PathObject
        {
            Name = path.Attribute("id")?.Value ?? "path"
        };

        var segments = ParsePathData(d);
        pathObj.Segments = segments;
        
        // Determine if closed
        pathObj.IsClosed = d.TrimEnd().EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
                          d.TrimEnd().EndsWith("z", StringComparison.OrdinalIgnoreCase);

        ApplyStyle(path, pathObj);
        pathObj.RecalculateBounds();
        return pathObj;
    }

    private ShapeObject? ParsePolygon(XElement polygon)
    {
        var pointsStr = polygon.Attribute("points")?.Value;
        if (string.IsNullOrEmpty(pointsStr)) return null;

        var points = ParsePoints(pointsStr);
        if (points.Count < 3) return null;

        var polyObj = new ShapeObject
        {
            Name = polygon.Attribute("id")?.Value ?? "polygon",
            Vertices = points,
            IsClosed = true
        };

        ApplyStyle(polygon, polyObj);
        polyObj.RecalculateBounds();
        return polyObj;
    }

    private PathObject? ParsePolyline(XElement polyline)
    {
        var pointsStr = polyline.Attribute("points")?.Value;
        if (string.IsNullOrEmpty(pointsStr)) return null;

        var points = ParsePoints(pointsStr);
        if (points.Count < 2) return null;

        var pathObj = new PathObject
        {
            Name = polyline.Attribute("id")?.Value ?? "polyline",
            IsClosed = false
        };

        // Create line segments
        var segments = new List<PathSegment>();
        for (int i = 0; i < points.Count - 1; i++)
        {
            segments.Add(new LineSegment { Start = points[i], End = points[i + 1] });
        }
        pathObj.Segments = segments;

        ApplyStyle(polyline, pathObj);
        pathObj.RecalculateBounds();
        return pathObj;
    }

    private PathObject? ParseLine(XElement line)
    {
        double x1 = ParseDouble(line.Attribute("x1")?.Value) ?? 0;
        double y1 = ParseDouble(line.Attribute("y1")?.Value) ?? 0;
        double x2 = ParseDouble(line.Attribute("x2")?.Value) ?? 0;
        double y2 = ParseDouble(line.Attribute("y2")?.Value) ?? 0;

        var pathObj = new PathObject
        {
            Name = line.Attribute("id")?.Value ?? "line",
            IsClosed = false
        };

        pathObj.Segments = new List<PathSegment>
        {
            new LineSegment
            {
                Start = new Point((int)(x1 * 1000), (int)(y1 * 1000)),
                End = new Point((int)(x2 * 1000), (int)(y2 * 1000))
            }
        };

        ApplyStyle(line, pathObj);
        pathObj.RecalculateBounds();
        return pathObj;
    }

    private List<Point> ParsePoints(string pointsStr)
    {
        var points = new List<Point>();
        var parts = pointsStr.Split(new[] { ' ', ',', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        
        for (int i = 0; i < parts.Length - 1; i += 2)
        {
            if (double.TryParse(parts[i], out double x) && double.TryParse(parts[i + 1], out double y))
            {
                points.Add(new Point((int)(x * 1000), (int)(y * 1000)));
            }
        }
        return points;
    }

    private List<PathSegment> ParsePathData(string d)
    {
        var segments = new List<PathSegment>();
        var tokens = TokenizePathData(d);
        
        Point? currentPoint = null;
        Point? startPoint = null;
        int i = 0;

        while (i < tokens.Count)
        {
            var token = tokens[i++];
            char cmd = char.ToUpper(token[0]);
            bool relative = char.IsLower(token[0]);

            Point GetPoint(int count)
            {
                var coords = new double[count];
                for (int j = 0; j < count; j++)
                {
                    coords[j] = double.Parse(tokens[i++]);
                }
                
                double x = coords[0];
                double y = coords.Length > 1 ? coords[1] : 0;
                
                if (relative && currentPoint.HasValue)
                {
                    x += currentPoint.Value.X / 1000.0;
                    y += currentPoint.Value.Y / 1000.0;
                }
                
                return new Point((int)(x * 1000), (int)(y * 1000));
            }

            switch (cmd)
            {
                case 'M': // Move to
                    var p = GetPoint(2);
                    currentPoint = p;
                    if (!startPoint.HasValue) startPoint = p;
                    break;

                case 'L': // Line to
                    var endPt = GetPoint(2);
                    if (currentPoint.HasValue)
                    {
                        segments.Add(new LineSegment { Start = currentPoint.Value, End = endPt });
                        currentPoint = endPt;
                    }
                    break;

                case 'H': // Horizontal line to
                    double hx = double.Parse(tokens[i++]);
                    if (relative && currentPoint.HasValue) hx += currentPoint.Value.X / 1000.0;
                    var hEnd = new Point((int)(hx * 1000), currentPoint?.Y ?? 0);
                    if (currentPoint.HasValue)
                    {
                        segments.Add(new LineSegment { Start = currentPoint.Value, End = hEnd });
                        currentPoint = hEnd;
                    }
                    break;

                case 'V': // Vertical line to
                    double vy = double.Parse(tokens[i++]);
                    if (relative && currentPoint.HasValue) vy += currentPoint.Value.Y / 1000.0;
                    var vEnd = new Point(currentPoint?.X ?? 0, (int)(vy * 1000));
                    if (currentPoint.HasValue)
                    {
                        segments.Add(new LineSegment { Start = currentPoint.Value, End = vEnd });
                        currentPoint = vEnd;
                    }
                    break;

                case 'C': // Cubic Bezier
                    var cp1 = GetPoint(2);
                    var cp2 = GetPoint(2);
                    var cEnd = GetPoint(2);
                    if (currentPoint.HasValue)
                    {
                        segments.Add(new CubicBezierSegment { Start = currentPoint.Value, Control1 = cp1, Control2 = cp2, End = cEnd });
                        currentPoint = cEnd;
                    }
                    break;

                case 'S': // Smooth cubic Bezier
                    var scp2 = GetPoint(2);
                    var sEnd = GetPoint(2);
                    if (currentPoint.HasValue)
                    {
                        // Simplified - just use as cubic
                        segments.Add(new CubicBezierSegment { Start = currentPoint.Value, Control1 = currentPoint.Value, Control2 = scp2, End = sEnd });
                        currentPoint = sEnd;
                    }
                    break;

                case 'Q': // Quadratic Bezier
                    var qcp = GetPoint(2);
                    var qEnd = GetPoint(2);
                    if (currentPoint.HasValue)
                    {
                        segments.Add(new QuadraticBezierSegment { Start = currentPoint.Value, Control = qcp, End = qEnd });
                        currentPoint = qEnd;
                    }
                    break;

                case 'T': // Smooth quadratic Bezier
                    var tEnd = GetPoint(2);
                    if (currentPoint.HasValue)
                    {
                        segments.Add(new QuadraticBezierSegment { Start = currentPoint.Value, Control = currentPoint.Value, End = tEnd });
                        currentPoint = tEnd;
                    }
                    break;

                case 'A': // Elliptical Arc (simplified - convert to cubic)
                    // rx, ry, x-axis-rotation, large-arc-flag, sweep-flag, x, y
                    double rx = double.Parse(tokens[i++]);
                    double ry = double.Parse(tokens[i++]);
                    double xrot = double.Parse(tokens[i++]);
                    int largeArc = int.Parse(tokens[i++]);
                    int sweep = int.Parse(tokens[i++]);
                    var aEnd = GetPoint(2);
                    if (currentPoint.HasValue)
                    {
                        // Approximate arc with cubic bezier
                        segments.Add(new CubicBezierSegment { Start = currentPoint.Value, Control1 = currentPoint.Value, Control2 = aEnd, End = aEnd });
                        currentPoint = aEnd;
                    }
                    break;

                case 'Z': // Close path
                case 'z':
                    if (currentPoint.HasValue && startPoint.HasValue && !currentPoint.Value.Equals(startPoint.Value))
                    {
                        segments.Add(new LineSegment { Start = currentPoint.Value, End = startPoint.Value });
                        currentPoint = startPoint.Value;
                    }
                    break;
            }
        }

        return segments;
    }

    private List<string> TokenizePathData(string d)
    {
        var tokens = new List<string>();
        var current = "";
        
        for (int i = 0; i < d.Length; i++)
        {
            char c = d[i];
            
            if (char.IsLetter(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current);
                    current = "";
                }
                tokens.Add(c.ToString());
            }
            else if (c == '-' || c == '+' || c == '.' || char.IsDigit(c))
            {
                current += c;
            }
            else if (c == ',' || c == ' ' || c == '\t' || c == '\n' || c == '\r')
            {
                if (current.Length > 0)
                {
                    tokens.Add(current);
                    current = "";
                }
            }
        }
        
        if (current.Length > 0)
            tokens.Add(current);
        
        return tokens;
    }

    private double? ParseDouble(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return double.TryParse(value, out double result) ? result : null;
    }

    private void ApplyStyle(XElement element, EmbroideryObject obj)
    {
        // Parse fill
        var fill = element.Attribute("fill")?.Value;
        if (!string.IsNullOrEmpty(fill) && fill != "none")
        {
            var color = ParseSvgColor(fill);
            if (color.HasValue)
            {
                obj.StitchParams = StitchParams.DefaultFor(StitchType.Tatami);
                obj.StitchParams.ColorIndex = 0; // Will be mapped later
                // Store color in metadata for palette building
                obj.Metadata["svg_fill"] = fill;
            }
        }
        else
        {
            // No fill - use stroke as outline
            var stroke = element.Attribute("stroke")?.Value;
            if (!string.IsNullOrEmpty(stroke) && stroke != "none")
            {
                obj.StitchParams = StitchParams.DefaultFor(StitchType.Running);
                obj.Metadata["svg_stroke"] = stroke;
            }
        }

        // Parse stroke-width
        var strokeWidth = element.Attribute("stroke-width")?.Value;
        if (!string.IsNullOrEmpty(strokeWidth))
        {
            double width = ParseDouble(strokeWidth) ?? 1;
            obj.Metadata["svg_stroke_width"] = width;
        }
    }

    private ThreadColor? ParseSvgColor(string colorStr)
    {
        colorStr = colorStr.Trim();
        
        if (colorStr.StartsWith("#"))
        {
            return ThreadColor.FromHex(colorStr);
        }
        
        if (colorStr.StartsWith("rgb("))
        {
            var parts = colorStr.Substring(4, colorStr.Length - 5).Split(',');
            if (parts.Length == 3 &&
                byte.TryParse(parts[0].Trim(), out byte r) &&
                byte.TryParse(parts[1].Trim(), out byte g) &&
                byte.TryParse(parts[2].Trim(), out byte b))
            {
                return new ThreadColor(r, g, b);
            }
        }
        
        // Named colors - basic support
        var namedColors = new Dictionary<string, ThreadColor>(StringComparer.OrdinalIgnoreCase)
        {
            ["red"] = ThreadColor.Red,
            ["green"] = ThreadColor.Green,
            ["blue"] = ThreadColor.Blue,
            ["black"] = ThreadColor.Black,
            ["white"] = ThreadColor.White,
            ["yellow"] = ThreadColor.Yellow,
            ["magenta"] = ThreadColor.Magenta,
            ["cyan"] = ThreadColor.Cyan,
        };
        
        if (namedColors.TryGetValue(colorStr, out var named))
            return named;
        
        return null;
    }

    private void BuildThreadPalette(AtlasProject project)
    {
        var colors = new Dictionary<string, ThreadColor>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var obj in project.Objects)
        {
            if (obj.Metadata.TryGetValue("svg_fill", out var fill) && fill is string fillStr)
            {
                var color = ParseSvgColor(fillStr);
                if (color.HasValue && !colors.ContainsKey(fillStr))
                {
                    colors[fillStr] = color.Value;
                }
            }
            
            if (obj.Metadata.TryGetValue("svg_stroke", out var stroke) && stroke is string strokeStr)
            {
                var color = ParseSvgColor(strokeStr);
                if (color.HasValue && !colors.ContainsKey(strokeStr))
                {
                    colors[strokeStr] = color.Value;
                }
            }
        }

        project.ThreadPalette = colors.Values.ToList();
        
        for (int i = 0; i < project.ThreadPalette.Count; i++)
        {
            project.ColorToNeedleMap[i] = (i % 15) + 1;
        }
    }
}