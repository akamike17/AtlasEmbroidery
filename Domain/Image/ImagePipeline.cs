namespace AtlasEmbroidery.Domain.Image;

using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using System.Drawing;
using System.Drawing.Imaging;
using SDPoint = System.Drawing.Point;
using SDBitmap = System.Drawing.Bitmap;
using SDColor = System.Drawing.Color;
using SDRectangle = System.Drawing.Rectangle;
using SDGraphics = System.Drawing.Graphics;
using SDImageFormat = System.Drawing.Imaging.ImageFormat;
using SDPixelFormat = System.Drawing.Imaging.PixelFormat;
using SDRectangleF = System.Drawing.RectangleF;
using AtlasPoint = AtlasEmbroidery.Domain.Models.Point;

#pragma warning disable CA1416 // Platform compatibility - System.Drawing.Common is Windows-only by design

/// <summary>
/// Pipeline de procesamiento de imagen para digitalización de bordado
/// Flujo: preservar → limpiar → segmentar → reducir colores → detectar bordes → simplificar → vectorizar/reconstruir → proponer bordado → generar objetos → puntadas → validar → comparar
/// </summary>
public sealed class ImagePipeline
{
    private readonly ImagePipelineOptions _options;

    public ImagePipeline(ImagePipelineOptions? options = null)
    {
        _options = options ?? new ImagePipelineOptions();
    }

    /// <summary>
    /// Procesa una imagen completa y genera un AtlasProject con objetos de bordado propuestos
    /// </summary>
    public async Task<AtlasProject> ProcessAsync(Stream imageStream, string fileName, CancellationToken ct = default)
    {
        // 1. Cargar imagen original
        using var originalBitmap = new Bitmap(imageStream);
        var originalRaster = CreateRasterReference(originalBitmap, fileName);

        // 2. Preservar original
        var workingBitmap = new Bitmap(originalBitmap);

        // 3. Limpiar imagen (ruido, fondo, etc.)
        var cleanedBitmap = await CleanImageAsync(workingBitmap, ct);

        // 4. Segmentar / reducir colores
        var (segmentedBitmap, colorPalette) = await SegmentAndReduceColorsAsync(cleanedBitmap, ct);

        // 5. Detectar bordes
        var edgesBitmap = await DetectEdgesAsync(segmentedBitmap, ct);

        // 6. Simplificar / vectorizar
        var vectorObjects = await VectorizeAsync(edgesBitmap, colorPalette, ct);

        // 7. Proponer objetos de bordado
        var embroideryObjects = await ProposeEmbroideryObjectsAsync(vectorObjects, colorPalette, ct);

        /// 8. Construir proyecto
                var project = new AtlasProject
                {
                    Name = Path.GetFileNameWithoutExtension(_options.SourceFileName ?? "Imported"),
                    SourceFilePath = _options.SourceFileName,
                    OriginalRaster = originalRaster,
                    Objects = embroideryObjects,
                    ThreadPalette = colorPalette.Select((c, i) => c with { Code = i.ToString("D3") }).ToList()
                };

        // Mapear colores a agujas
        for (int i = 0; i < project.ThreadPalette.Count; i++)
        {
            project.ColorToNeedleMap[i] = (i % 15) + 1;
        }

        project.RecalculateBounds();
        project.Touch();

        return project;
    }

    /// <summary>
    /// Crea referencia raster original (sin almacenar píxeles, solo metadatos + hash)
    /// </summary>
    private RasterReference CreateRasterReference(SDBitmap bitmap, string fileName)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, SDImageFormat.Png);
        var bytes = ms.ToArray();

        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();

        return new RasterReference
        {
            FileName = fileName,
            FileHash = hash,
            Width = bitmap.Width,
            Height = bitmap.Height,
            DpiX = (int)bitmap.HorizontalResolution,
            DpiY = (int)bitmap.VerticalResolution,
            PixelFormat = AtlasEmbroidery.Domain.Models.PixelFormat.Rgba32,
            FileSize = bytes.Length
        };
    }

    /// <summary>
    /// Limpia imagen: remueve ruido, fondo uniforme, artefactos
    /// </summary>
    private async Task<SDBitmap> CleanImageAsync(SDBitmap source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var result = new SDBitmap(source.Width, source.Height, SDPixelFormat.Format32bppArgb);
        
        // 1. Remover fondo si es uniforme (esquina superior-izquierda)
        var backgroundColor = source.GetPixel(0, 0);
        var tolerance = _options.BackgroundTolerance;

        // 2. Aplicar filtro de mediana para reducir ruido
        var denoised = ApplyMedianFilter(source, _options.MedianFilterRadius);

        // 3. Recortar bordes transparentes/fondo
        var cropped = CropToContent(denoised, backgroundColor, tolerance);

        return cropped;
    }

    private SDBitmap ApplyMedianFilter(SDBitmap source, int radius)
    {
        if (radius <= 0) return new SDBitmap(source);

        var result = new SDBitmap(source.Width, source.Height, SDPixelFormat.Format32bppArgb);
        var rect = new SDRectangle(0, 0, source.Width, source.Height);
        
        var srcData = source.LockBits(rect, ImageLockMode.ReadOnly, SDPixelFormat.Format32bppArgb);
        var dstData = result.LockBits(rect, ImageLockMode.WriteOnly, SDPixelFormat.Format32bppArgb);

        try
        {
            int stride = srcData.Stride;
            int width = source.Width;
            int height = source.Height;

            var srcBytes = new byte[stride * height];
            var dstBytes = new byte[stride * height];

            System.Runtime.InteropServices.Marshal.Copy(srcData.Scan0, srcBytes, 0, srcBytes.Length);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var neighbors = new List<int>();
                    
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;
                            
                            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                            {
                                int idx = ny * stride + nx * 4;
                                int pixel = (srcBytes[idx + 3] << 24) | (srcBytes[idx + 2] << 16) | (srcBytes[idx + 1] << 8) | srcBytes[idx];
                                neighbors.Add(pixel);
                            }
                        }
                    }

                    neighbors.Sort();
                    int median = neighbors[neighbors.Count / 2];
                    
                    int dstIdx = y * stride + x * 4;
                    dstBytes[dstIdx] = (byte)(median & 0xFF);
                    dstBytes[dstIdx + 1] = (byte)((median >> 8) & 0xFF);
                    dstBytes[dstIdx + 2] = (byte)((median >> 16) & 0xFF);
                    dstBytes[dstIdx + 3] = (byte)((median >> 24) & 0xFF);
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(dstBytes, 0, dstData.Scan0, dstBytes.Length);
        }
        finally
        {
            source.UnlockBits(srcData);
            result.UnlockBits(dstData);
        }

        return result;
    }

    private SDBitmap CropToContent(SDBitmap source, SDColor backgroundColor, int tolerance)
    {
        int left = source.Width, top = source.Height, right = 0, bottom = 0;
        bool foundContent = false;

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                var pixel = source.GetPixel(x, y);
                int diff = Math.Abs(pixel.R - backgroundColor.R) + 
                          Math.Abs(pixel.G - backgroundColor.G) + 
                          Math.Abs(pixel.B - backgroundColor.B);
                
                if (diff > tolerance || pixel.A < 255)
                {
                    foundContent = true;
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        if (!foundContent) return new SDBitmap(source);

        // Add margin
        int margin = 5;
        left = Math.Max(0, left - margin);
        top = Math.Max(0, top - margin);
        right = Math.Min(source.Width - 1, right + margin);
        bottom = Math.Min(source.Height - 1, bottom + margin);

        int cropWidth = right - left + 1;
        int cropHeight = bottom - top + 1;

        if (cropWidth <= 0 || cropHeight <= 0) return new SDBitmap(source);

        var cropped = new SDBitmap(cropWidth, cropHeight, SDPixelFormat.Format32bppArgb);
        using (var g = SDGraphics.FromImage(cropped))
        {
            g.DrawImage(source, 0, 0, new SDRectangle(left, top, cropWidth, cropHeight), GraphicsUnit.Pixel);
        }

        return cropped;
    }

    /// <summary>
    /// Segmenta imagen y reduce colores usando K-means o cuantización
    /// </summary>
    private async Task<(SDBitmap segmented, List<ThreadColor> palette)> SegmentAndReduceColorsAsync(SDBitmap source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Cuantizar colores usando K-means simplificado
        var colors = ExtractColors(source);
        var palette = QuantizeColors(colors, _options.MaxColors);
        
        // Mapear cada píxel al color más cercano de la paleta
        var result = new SDBitmap(source.Width, source.Height, SDPixelFormat.Format32bppArgb);
        
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                var pixel = source.GetPixel(x, y);
                if (pixel.A == 0)
                {
                    result.SetPixel(x, y, SDColor.Transparent);
                }
                else
                {
                    var nearest = FindNearestColor(pixel, palette);
                    result.SetPixel(x, y, SDColor.FromArgb(pixel.A, nearest.R, nearest.G, nearest.B));
                }
            }
        }

        return (result, palette);
    }

    private List<SDColor> ExtractColors(SDBitmap bitmap)
    {
        var colors = new HashSet<int>();
        
        for (int y = 0; y < bitmap.Height; y += 2) // Sample every 2 pixels for speed
        {
            for (int x = 0; x < bitmap.Width; x += 2)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A > 0)
                {
                    colors.Add(pixel.ToArgb());
                }
            }
        }

        return colors.Select(c => SDColor.FromArgb(c)).ToList();
    }

    private List<ThreadColor> QuantizeColors(List<SDColor> colors, int maxColors)
    {
        if (colors.Count <= maxColors)
        {
            return colors.Select(c => new ThreadColor(c.R, c.G, c.B)).ToList();
        }

        // K-means simplificado
        var centroids = colors.Take(maxColors).ToList();
        var clusters = new List<SDColor>[maxColors];
        
        for (int i = 0; i < maxColors; i++) clusters[i] = new List<SDColor>();

        bool changed = true;
        int iterations = 0;

        while (changed && iterations < 20)
        {
            changed = false;
            iterations++;

            // Clear clusters
            foreach (var cluster in clusters) cluster.Clear();

            // Assign colors to nearest centroid
            foreach (var color in colors)
            {
                int bestIdx = 0;
                double bestDist = double.MaxValue;

                for (int i = 0; i < centroids.Count; i++)
                {
                    double dist = ColorDistance(color, centroids[i]);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestIdx = i;
                    }
                }

                clusters[bestIdx].Add(color);
            }

            // Update centroids
            for (int i = 0; i < centroids.Count; i++)
            {
                if (clusters[i].Count > 0)
                {
                    int r = (int)clusters[i].Average(c => c.R);
                    int g = (int)clusters[i].Average(c => c.G);
                    int b = (int)clusters[i].Average(c => c.B);
                    
                    var newCentroid = SDColor.FromArgb(r, g, b);
                    if (ColorDistance(newCentroid, centroids[i]) > 1)
                    {
                        centroids[i] = newCentroid;
                        changed = true;
                    }
                }
            }
        }

        return centroids.Where(c => c.A > 0).Select(c => new ThreadColor(c.R, c.G, c.B)).ToList();
    }

    private double ColorDistance(SDColor c1, SDColor c2)
    {
        int dr = c1.R - c2.R;
        int dg = c1.G - c2.G;
        int db = c1.B - c2.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    private ThreadColor FindNearestColor(SDColor pixel, List<ThreadColor> palette)
    {
        ThreadColor nearest = palette[0];
        double bestDist = double.MaxValue;

        foreach (var color in palette)
        {
            int dr = pixel.R - color.R;
            int dg = pixel.G - color.G;
            int db = pixel.B - color.B;
            double dist = dr * dr + dg * dg + db * db;

            if (dist < bestDist)
            {
                bestDist = dist;
                nearest = color;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Detecta bordes usando Sobel o Canny simplificado
    /// </summary>
    private async Task<SDBitmap> DetectEdgesAsync(SDBitmap source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var result = new SDBitmap(source.Width, source.Height, SDPixelFormat.Format32bppArgb);
        
        // Sobel operator
        int[,] gx = { { -1, 0, 1 }, { -2, 0, 2 }, { -1, 0, 1 } };
        int[,] gy = { { -1, -2, -1 }, { 0, 0, 0 }, { 1, 2, 1 } };

        for (int y = 1; y < source.Height - 1; y++)
        {
            for (int x = 1; x < source.Width - 1; x++)
            {
                double gxSum = 0, gySum = 0;

                for (int ky = -1; ky <= 1; ky++)
                {
                    for (int kx = -1; kx <= 1; kx++)
                    {
                        var pixel = source.GetPixel(x + kx, y + ky);
                        int gray = (pixel.R + pixel.G + pixel.B) / 3;
                        
                        gxSum += gray * gx[ky + 1, kx + 1];
                        gySum += gray * gy[ky + 1, kx + 1];
                    }
                }

                double magnitude = Math.Sqrt(gxSum * gxSum + gySum * gySum);
                int edgeValue = Math.Min(255, (int)magnitude);

                result.SetPixel(x, y, SDColor.FromArgb(255, edgeValue, edgeValue, edgeValue));
            }
        }

        return result;
    }

    /// <summary>
    /// Vectoriza bordes detectados en curvas/paths
    /// </summary>
    private async Task<List<VectorObject>> VectorizeAsync(SDBitmap edges, List<ThreadColor> palette, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var vectors = new List<VectorObject>();
        var visited = new bool[edges.Width, edges.Height];

        // Trace contours using Moore neighborhood tracing
        for (int y = 0; y < edges.Height; y++)
        {
            for (int x = 0; x < edges.Width; x++)
            {
                var pixel = edges.GetPixel(x, y);
                if (pixel.R > _options.EdgeThreshold && !visited[x, y])
                {
                    var contour = TraceContour(edges, x, y, visited);
                    if (contour.Count > _options.MinContourLength)
                    {
                        var simplified = SimplifyContour(contour);
                        var vectorObj = new VectorObject
                        {
                            Points = simplified,
                            Color = FindDominantColor(contour, palette)
                        };
                        vectors.Add(vectorObj);
                    }
                }
            }
        }

        return vectors;
    }

    private List<AtlasPoint> TraceContour(SDBitmap edges, int startX, int startY, bool[,] visited)
    {
        var contour = new List<AtlasPoint>();
        int x = startX;
        int y = startY;
        int dir = 0; // 0=right, 1=down-right, 2=down, etc.

        // Moore neighborhood directions
        int[] dx = { 1, 1, 0, -1, -1, -1, 0, 1 };
        int[] dy = { 0, 1, 1, 1, 0, -1, -1, -1 };

        int finalStartX = x;
        int finalStartY = y;

        do
        {
            visited[x, y] = true;
            contour.Add(new AtlasPoint(x, y));

            // Find next edge pixel
            bool found = false;
            for (int i = 0; i < 8; i++)
            {
                int nextDir = (dir + 6 + i) % 8; // Start from previous direction - 1
                int nx = x + dx[nextDir];
                int ny = y + dy[nextDir];

                if (nx >= 0 && nx < edges.Width && ny >= 0 && ny < edges.Height)
                {
                    var pixel = edges.GetPixel(nx, ny);
                    if (pixel.R > _options.EdgeThreshold && !visited[nx, ny])
                    {
                        x = nx;
                        y = ny;
                        dir = nextDir;
                        found = true;
                        break;
                    }
                }
            }

            if (!found) break;

        } while (x != finalStartX || y != finalStartY);

        return contour;
    }

    private List<AtlasPoint> SimplifyContour(List<AtlasPoint> contour)
    {
        // Douglas-Peucker simplification
        return GeometryUtils.SimplifyPolygon(contour, _options.SimplificationTolerance);
    }

    private ThreadColor FindDominantColor(List<AtlasPoint> contour, List<ThreadColor> palette)
    {
        // For now, return first palette color - in reality would sample source image
        return palette.Count > 0 ? palette[0] : ThreadColor.Black;
    }

    /// <summary>
    /// Propone objetos de bordado a partir de vectores
    /// </summary>
    private async Task<List<EmbroideryObject>> ProposeEmbroideryObjectsAsync(
        List<VectorObject> vectors, 
        List<ThreadColor> palette, 
        CancellationToken ct)
    {
        var objects = new List<EmbroideryObject>();

        for (int i = 0; i < vectors.Count; i++)
        {
            var vector = vectors[i];
            ct.ThrowIfCancellationRequested();

            // Decide stitch type based on shape characteristics
            var stitchType = DetermineStitchType(vector);
            var colorIndex = palette.IndexOf(vector.Color);
            if (colorIndex < 0) colorIndex = 0;

            EmbroideryObject? obj = null;

            if (vector.Points.Count >= 3 && IsClosed(vector.Points))
            {
                // Closed shape -> tatami fill
                obj = new ShapeObject
                {
                    Name = $"AutoShape_{i}",
                    Vertices = vector.Points,
                    IsClosed = true,
                    StitchParams = StitchParams.DefaultFor(stitchType)
                };
                obj.StitchParams.ColorIndex = Math.Max(0, colorIndex);
            }
            else if (vector.Points.Count >= 2)
            {
                // Open path -> running stitch
                var pathObj = new PathObject
                {
                    Name = $"AutoPath_{i}",
                    IsClosed = false
                };

                var segments = new List<PathSegment>();
                for (int j = 0; j < vector.Points.Count - 1; j++)
                {
                    segments.Add(new LineSegment
                    {
                        Start = vector.Points[j],
                        End = vector.Points[j + 1]
                    });
                }
                pathObj.Segments = segments;
                pathObj.StitchParams = StitchParams.DefaultFor(StitchType.Running);
                pathObj.StitchParams.ColorIndex = Math.Max(0, colorIndex);
                obj = pathObj;
            }

            if (obj != null)
            {
                obj.SequenceOrder = i;
                objects.Add(obj);
            }
        }

        return objects;
    }

    private StitchType DetermineStitchType(VectorObject vector)
    {
        if (vector.Points.Count < 3) return StitchType.Running;

        // Calculate area and perimeter
        double area = Math.Abs(GeometryUtils.PolygonArea(vector.Points));
        double perimeter = 0;
        for (int i = 0; i < vector.Points.Count; i++)
        {
            int j = (i + 1) % vector.Points.Count;
            perimeter += vector.Points[i].DistanceTo(vector.Points[j]);
        }

        // Compactness: 4*PI*Area / Perimeter^2
        // Circle = 1, line = 0
        double compactness = perimeter > 0 ? (4 * Math.PI * area) / (perimeter * perimeter) : 0;

        if (compactness > 0.7 && vector.Points.Count > 10)
            return StitchType.Tatami; // Fill for large compact shapes
        else if (compactness > 0.3)
            return StitchType.Satin; // Satin for elongated shapes
        else
            return StitchType.Running; // Running for thin lines
    }

    private bool IsClosed(List<AtlasPoint> points)
    {
        if (points.Count < 3) return false;
        return points[0].Equals(points[^1]) || points[0].DistanceTo(points[^1]) < 5;
    }
}

/// <summary>
/// Objeto vectorial intermedio
/// </summary>
public sealed class VectorObject
{
    public List<AtlasPoint> Points { get; set; } = new();
    public ThreadColor Color { get; set; } = ThreadColor.Black;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Opciones del pipeline de imagen
/// </summary>
public sealed class ImagePipelineOptions
{
    public string? SourceFileName { get; set; }
    public int BackgroundTolerance { get; set; } = 30;
    public int MedianFilterRadius { get; set; } = 2;
    public int MaxColors { get; set; } = 16;
    public int EdgeThreshold { get; set; } = 50;
    public int MinContourLength { get; set; } = 10;
    public double SimplificationTolerance { get; set; } = 2.0;
}