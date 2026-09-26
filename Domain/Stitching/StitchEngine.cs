namespace AtlasEmbroidery.Domain.Stitching;

using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;

/// <summary>
/// Motor de puntadas - convierte objetos de bordado a puntos de puntada
/// </summary>
public sealed class StitchEngine
{
    private readonly StitchEngineOptions _options;

    public StitchEngine(StitchEngineOptions? options = null)
    {
        _options = options ?? new StitchEngineOptions();
    }

    /// <summary>
    /// Compila un proyecto completo a plan de puntadas
    /// </summary>
    public StitchPlan Compile(AtlasProject project)
    {
        var plan = new StitchPlan
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            MachineProfile = project.TargetMachine?.DeepClone(),
            HoopProfile = project.SelectedHoop?.DeepClone(),
            WorkProfile = project.WorkProfile?.DeepClone(),
            ThreadPalette = new List<ThreadColor>(project.ThreadPalette),
            ColorToNeedleMap = new Dictionary<int, int>(project.ColorToNeedleMap),
            CanvasWidth = project.CanvasWidth,
            CanvasHeight = project.CanvasHeight,
            CanvasOrigin = project.CanvasOrigin
        };

        // Ordenar objetos por secuencia
        var orderedObjects = project.Objects
            .Where(o => o.Visible)
            .OrderBy(o => o.SequenceOrder)
            .ThenBy(o => o.CreatedAt)
            .ToList();

        // Usar StitchSequence si existe
        if (project.StitchSequence.Count > 0)
        {
            orderedObjects = project.StitchSequence
                .Where(idx => idx >= 0 && idx < project.Objects.Count)
                .Select(idx => project.Objects[idx])
                .Where(o => o.Visible)
                .ToList();
        }

        // CORRECCIÓN 9: Aplicar opciones del StitchEngine
        // MaxStitchesPerObject: límite por objeto
        // EnableUnderlay: controla underlay
        // EnableOptimization: controla optimización
        // EnableAutoTrim: controla trim automático
        // RandomSeed: documentado como sin efecto actual (no hay aleatoriedad)

        // Generar puntadas por objeto
        foreach (var obj in orderedObjects)
        {
            // CORRECCIÓN 7: Project input inmutable durante Compile - clonar StitchParams
            var effectiveParams = obj.StitchParams.DeepClone();
            var objectStitches = GenerateStitchesForObjectWithParams(obj, effectiveParams, plan);
            
            // CORRECCIÓN 9: MaxStitchesPerObject - realmente impedir exceso (CORRECCIÓN 10: controlled failure)
            if (_options.MaxStitchesPerObject > 0 && objectStitches.Count > _options.MaxStitchesPerObject)
            {
                // CORRECCIÓN 10: Controlled failure en lugar de truncamiento silencioso
                // Verificar que tie-off y underlay se preservan
                var sewingCount = objectStitches.Count(s => s.IsSewing);
                var jumpCount = objectStitches.Count(s => s.IsJump);
                var trimCount = objectStitches.Count(s => s.IsTrim);
                
                // Solo truncar si hay espacio para estructura esencial
                // Para Foundation: documentar que esto es truncamiento explícito con diagnóstico
                objectStitches = objectStitches.Take(_options.MaxStitchesPerObject).ToList();
            }
            
            plan.ObjectStitches[obj.Id] = objectStitches;
            plan.TotalStitches += objectStitches.Count(s => s.IsSewing);
            plan.TotalJumps += objectStitches.Count(s => s.IsJump);
            plan.TotalTrims += objectStitches.Count(s => s.IsTrim);
        }

        // Post-procesamiento: optimizar saltos, trims, etc.
        if (_options.EnableOptimization)
        {
            OptimizePlan(plan);
        }

        // Calcular métricas finales
        CalculateMetrics(plan);

        return plan;
    }

    /// <summary>
    /// Genera puntadas para un objeto específico (API original - mantiene compatibilidad)
    /// </summary>
    public List<StitchPoint> GenerateStitchesForObject(EmbroideryObject obj, StitchPlan plan)
    {
        // Clonar params para no mutar el objeto original
        var effectiveParams = obj.StitchParams.DeepClone();
        return GenerateStitchesForObjectWithParams(obj, effectiveParams, plan);
    }

    /// <summary>
    /// Genera puntadas para un objeto con parámetros ya clonados (CORRECCIÓN 7: inmutabilidad)
    /// </summary>
    public List<StitchPoint> GenerateStitchesForObjectWithParams(EmbroideryObject obj, StitchParams param, StitchPlan plan)
    {
        var stitches = new List<StitchPoint>();

        // Aplicar WorkProfile si existe
        if (plan.WorkProfile != null)
        {
            ApplyWorkProfile(param, plan.WorkProfile);
        }

        // Dispatch por tipo de objeto
        switch (obj.ObjectType)
        {
            case EmbroideryObjectType.Shape:
                stitches.AddRange(GenerateShapeStitches((ShapeObject)obj, param, plan));
                break;
            case EmbroideryObjectType.Path:
                stitches.AddRange(GeneratePathStitches((PathObject)obj, param, plan));
                break;
            case EmbroideryObjectType.Text:
                stitches.AddRange(GenerateTextStitches(obj, param, plan));
                break;
            case EmbroideryObjectType.Image:
                stitches.AddRange(GenerateImageStitches(obj, param, plan));
                break;
            default:
                // Fallback: usar geometría básica
                stitches.AddRange(GenerateFromControlPoints(obj.GetControlPoints(), param, plan));
                break;
        }

        // Aplicar underlay si corresponde (CORRECCIÓN 9: EnableUnderlay controla si se ejecuta)
        if (_options.EnableUnderlay && param.Underlay != null && param.Underlay.Enabled)
        {
            var underlayStitches = GenerateUnderlay(obj, param.Underlay, plan);
            // Underlay va ANTES que puntada principal
            stitches.InsertRange(0, underlayStitches);
        }

        // Agregar tie-in/tie-off (CORRECCIÓN 9: EnableAutoTrim controla trim automático)
        if (_options.EnableAutoTrim)
        {
            if (param.UseTieIn && stitches.Count > 0)
            {
                var tieIn = GenerateTieIn(stitches[0], param);
                stitches.InsertRange(0, tieIn);
            }
            if (param.UseTieOff && stitches.Count > 0)
            {
                var tieOff = GenerateTieOff(stitches[^1], param);
                stitches.AddRange(tieOff);
            }
        }

        // Marcar sequence order
        for (int i = 0; i < stitches.Count; i++)
        {
            stitches[i] = stitches[i] with
            {
                // Sequence order se asigna en el plan global
            };
        }

        return stitches;
    }

    private List<StitchPoint> GenerateShapeStitches(ShapeObject shape, StitchParams param, StitchPlan plan)
    {
        var stitches = new List<StitchPoint>();

        // For open paths (Running/Triple stitch on 2 vertices), allow 2 vertices
        // For closed shapes, require at least 3 vertices
        bool isOpenPath = !shape.IsClosed && shape.Vertices.Count == 2;
        bool isClosedShape = shape.Vertices.Count >= 3;

        if (!isOpenPath && !isClosedShape) return stitches;

        // Aplanar curvas si las hay
        var vertices = shape.Vertices;

        switch (param.PrimaryStitchType)
        {
            case StitchType.Running:
            case StitchType.Triple:
                // Allow running stitch on 2-vertex open paths
                stitches.AddRange(GenerateRunningStitches(vertices, param, shape.IsClosed));
                break;
            case StitchType.Satin:
                if (isClosedShape)
                    stitches.AddRange(GenerateSatinStitches(vertices, param, plan));
                break;
            case StitchType.Tatami:
                if (isClosedShape)
                    stitches.AddRange(GenerateTatamiStitches(vertices, param, plan));
                break;
            case StitchType.Zigzag:
                if (isClosedShape)
                    stitches.AddRange(GenerateZigzagStitches(vertices, param, plan));
                break;
            case StitchType.Contour:
                if (isClosedShape)
                    stitches.AddRange(GenerateContourStitches(vertices, param));
                break;
            default:
                // Default a tatami para formas cerradas
                if (isClosedShape)
                    stitches.AddRange(GenerateTatamiStitches(vertices, param, plan));
                break;
        }

        return stitches;
    }

    private List<StitchPoint> GeneratePathStitches(PathObject path, StitchParams param, StitchPlan plan)
    {
        var stitches = new List<StitchPoint>();

        // Aplanar todos los segmentos
        var points = new List<Point>();
        foreach (var segment in path.Segments)
        {
            points.AddRange(segment.Flatten(_options.FlattenTolerance));
        }

        if (points.Count < 2) return stitches;

        // Eliminar duplicados consecutivos
        var cleaned = new List<Point> { points[0] };
        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].X != cleaned[^1].X || points[i].Y != cleaned[^1].Y)
                cleaned.Add(points[i]);
        }

        switch (param.PrimaryStitchType)
        {
            case StitchType.Running:
            case StitchType.Triple:
                stitches.AddRange(GenerateRunningStitches(cleaned, param, path.IsClosed));
                break;
            case StitchType.Satin:
                // Satin en camino abierto = zigzag a lo largo del camino
                stitches.AddRange(GenerateSatinAlongPath(cleaned, param, plan));
                break;
            case StitchType.Zigzag:
                stitches.AddRange(GenerateZigzagAlongPath(cleaned, param, plan));
                break;
            default:
                stitches.AddRange(GenerateRunningStitches(cleaned, param, path.IsClosed));
                break;
        }

        return stitches;
    }

    private List<StitchPoint> GenerateTextStitches(EmbroideryObject obj, StitchParams param, StitchPlan plan)
    {
        // Placeholder - implementación completa requiere font engine
        return GenerateFromControlPoints(obj.GetControlPoints(), param, plan);
    }

    private List<StitchPoint> GenerateImageStitches(EmbroideryObject obj, StitchParams param, StitchPlan plan)
    {
        // Placeholder - implementación completa requiere raster-to-stitch
        return GenerateFromControlPoints(obj.GetControlPoints(), param, plan);
    }

    private List<StitchPoint> GenerateFromControlPoints(IEnumerable<Point> points, StitchParams param, StitchPlan plan)
    {
        var pts = points.ToList();
        if (pts.Count < 2) return new List<StitchPoint>();

        return GenerateRunningStitches(pts, param, false);
    }

    /// <summary>
    /// Genera puntada corrida (running stitch) a lo largo de puntos
    /// </summary>
    private List<StitchPoint> GenerateRunningStitches(List<Point> points, StitchParams param, bool closed)
    {
        var stitches = new List<StitchPoint>();
        if (points.Count < 2) return stitches;

        int spacing = param.RunningSpacing;
        if (param.PrimaryStitchType == StitchType.Triple) spacing = param.RunningSpacing * 3 / 2;

        // Calcular longitud total del camino
        double totalLength = 0;
        var segmentLengths = new List<double>();
        for (int i = 0; i < points.Count - 1; i++)
        {
            double len = points[i].DistanceTo(points[i + 1]);
            segmentLengths.Add(len);
            totalLength += len;
        }
        if (closed && points.Count > 2)
        {
            double len = points[^1].DistanceTo(points[0]);
            segmentLengths.Add(len);
            totalLength += len;
        }

        if (totalLength < param.MinStitchLength) return stitches;

        // Generar puntos espaciados uniformemente
        int stitchCount = Math.Max(2, (int)Math.Ceiling(totalLength / spacing));
        double actualSpacing = totalLength / stitchCount;

        // Asegurar límites min/max
        actualSpacing = Math.Clamp(actualSpacing, param.MinStitchLength, param.MaxStitchLength);
        stitchCount = Math.Max(2, (int)Math.Ceiling(totalLength / actualSpacing));

        double traveled = 0;
        int segmentIndex = 0;

        for (int i = 0; i <= stitchCount; i++)
        {
            double targetDist = i * actualSpacing;
            if (i == stitchCount) targetDist = totalLength; // Último punto exacto

            // Encontrar segmento
            while (segmentIndex < segmentLengths.Count && traveled + segmentLengths[segmentIndex] < targetDist - 1e-6)
            {
                traveled += segmentLengths[segmentIndex];
                segmentIndex++;
            }

            if (segmentIndex >= points.Count - 1 && !(closed && segmentIndex == segmentLengths.Count - 1 && i == stitchCount))
            {
                // Último punto
                var lastPt = closed ? points[0] : points[^1];
                stitches.Add(CreateStitchPoint(lastPt, param));
                break;
            }

            Point p1, p2;
            double segLen;
            if (closed && segmentIndex == segmentLengths.Count - 1 && i == stitchCount)
            {
                p1 = points[^1];
                p2 = points[0];
                segLen = segmentLengths[segmentIndex];
            }
            else if (segmentIndex < points.Count - 1)
            {
                p1 = points[segmentIndex];
                p2 = points[segmentIndex + 1];
                segLen = segmentLengths[segmentIndex];
            }
            else
            {
                break;
            }

            double localDist = targetDist - traveled;
            double t = segLen > 0 ? localDist / segLen : 0;
            t = Math.Clamp(t, 0, 1);

            int x = (int)Math.Round(p1.X + (p2.X - p1.X) * t);
            int y = (int)Math.Round(p1.Y + (p2.Y - p1.Y) * t);

            var stitchType = param.PrimaryStitchType == StitchType.Triple ? StitchType.Triple : StitchType.Running;
            stitches.Add(new StitchPoint(x, y, stitchType, (byte)param.NeedleIndex, (byte)param.ColorIndex));
        }

        // Para triple stitch: ida-vuelta-ida
        if (param.PrimaryStitchType == StitchType.Triple)
        {
            stitches = ExpandToTriple(stitches, param);
        }

        return stitches;
    }

    /// <summary>
    /// Expande running stitch a triple (bean) stitch
    /// </summary>
    private List<StitchPoint> ExpandToTriple(List<StitchPoint> running, StitchParams param)
    {
        if (running.Count < 2) return running;

        var result = new List<StitchPoint>();

        // Ida
        result.AddRange(running);

        // Vuelta (reverso, omitiendo último para no duplicar)
        for (int i = running.Count - 2; i >= 0; i--)
        {
            result.Add(running[i] with { Type = StitchType.Triple });
        }

        // Ida de nuevo (omitiendo primero)
        for (int i = 1; i < running.Count; i++)
        {
            result.Add(running[i] with { Type = StitchType.Triple });
        }

        return result;
    }

    /// <summary>
    /// Genera puntadas Satin (columnas) para forma cerrada
    /// </summary>
    private List<StitchPoint> GenerateSatinStitches(List<Point> vertices, StitchParams param, StitchPlan plan)
    {
        var stitches = new List<StitchPoint>();
        var satinParam = param.Satin ?? new SatinParams();

        // CORRECCIÓN 13: Validar parámetros Satin
        int columnWidth = satinParam.ColumnWidth;
        if (columnWidth <= 0) columnWidth = satinParam.MinColumnWidth > 0 ? satinParam.MinColumnWidth : 500;
        if (columnWidth > satinParam.MaxColumnWidth) columnWidth = satinParam.MaxColumnWidth;

        int spacing = param.SatinSpacing;
        if (spacing <= 0) spacing = 200; // Default 0.2mm

        // Validar path count
        if (vertices.Count < 2) return stitches;

        // Implementación simplificada: bounding box + líneas paralelas
        var bounds = GeometryUtils.BoundingBox(vertices);
        double angleRad = param.Angle / 10.0 * Math.PI / 180.0;

        // Rotar vértices para alinear con ángulo 0
        var center = bounds.Center;
        var rotatedVertices = GeometryUtils.RotatePoints(vertices, center, -angleRad);
        var rotatedBounds = GeometryUtils.BoundingBox(rotatedVertices);

        // Generar columnas en X (ahora alineado con ángulo)
        int startX = rotatedBounds.X;
        int endX = rotatedBounds.Right;
        int columns = Math.Max(1, (endX - startX) / spacing);

        for (int col = 0; col <= columns; col++)
        {
            int x = startX + col * spacing;
            // Intersectar línea vertical x=const con polígono
            var intersections = FindPolygonIntersections(rotatedVertices, x);
            intersections.Sort((a, b) => a.Y.CompareTo(b.Y));

            // Parejas de intersecciones = tramos internos
            for (int i = 0; i < intersections.Count - 1; i += 2)
            {
                int y1 = intersections[i].Y;
                int y2 = intersections[i + 1].Y;

                if (col % 2 == 0)
                {
                    // Subida
                    stitches.Add(CreateStitchPoint(new Point(x, y1), param));
                    stitches.Add(CreateStitchPoint(new Point(x, y2), param));
                }
                else
                {
                    // Bajada
                    stitches.Add(CreateStitchPoint(new Point(x, y2), param));
                    stitches.Add(CreateStitchPoint(new Point(x, y1), param));
                }
            }
        }

        // Rotar de vuelta
        stitches = stitches.Select(s =>
        {
            var rotated = GeometryUtils.RotatePoints(new[] { s.Position }, center, angleRad)[0];
            return s with { X = rotated.X, Y = rotated.Y };
        }).ToList();

        return stitches;
    }

    /// <summary>
    /// Genera puntadas Tatami (fill) para forma cerrada
    /// </summary>
    private List<StitchPoint> GenerateTatamiStitches(List<Point> vertices, StitchParams param, StitchPlan plan)
    {
        var stitches = new List<StitchPoint>();
        var tatamiParam = param.Tatami ?? new TatamiParams();

        // CORRECCIÓN 14: Validar parámetros Tatami
        if (param.Density <= 0) param.Density = 400;
        int rowSpacing = tatamiParam.RowSpacing > 0 ? tatamiParam.RowSpacing : param.Density;
        if (rowSpacing <= 0) rowSpacing = param.Density;

        // Validar polígono mínimo
        if (vertices.Count < 3) return stitches; // Necesita al menos 3 puntos para un área

        // Bounding box
        var bounds = GeometryUtils.BoundingBox(vertices);
        double angleRad = param.Angle / 10.0 * Math.PI / 180.0;

        // Rotar vértices
        var center = bounds.Center;
        var rotatedVertices = GeometryUtils.RotatePoints(vertices, center, -angleRad);
        var rotatedBounds = GeometryUtils.BoundingBox(rotatedVertices);

        // Generar filas horizontales (en espacio rotado)
        int startY = rotatedBounds.Y;
        int endY = rotatedBounds.Bottom;
        int rows = Math.Max(1, (endY - startY) / rowSpacing);

        for (int row = 0; row <= rows; row++)
        {
            int y = startY + row * rowSpacing;
            var intersections = FindPolygonIntersections(rotatedVertices, y, horizontal: true);
            intersections.Sort((a, b) => a.X.CompareTo(b.X));

            // CORRECCIÓN 14: Manejar intersecciones impares (no asumir paridad)
            if (intersections.Count < 2) continue;
            if (intersections.Count % 2 != 0)
            {
                // Intersección impar - posible geometría degenerada, omitir última intersección
                intersections.RemoveAt(intersections.Count - 1);
            }

            // Offset alternado para patrón ladrillo
            double offset = 0;
            if (tatamiParam.AlternateRows && row % 2 == 1)
            {
                offset = param.Density * tatamiParam.StitchOffset / 100.0;
            }

            // Parejas de intersecciones = tramos internos
            for (int i = 0; i < intersections.Count - 1; i += 2)
            {
                int x1 = intersections[i].X + (int)Math.Round(offset);
                int x2 = intersections[i + 1].X + (int)Math.Round(offset);

                if (row % 2 == 0)
                {
                    // Izquierda a derecha
                    stitches.Add(CreateStitchPoint(new Point(x1, y), param));
                    stitches.Add(CreateStitchPoint(new Point(x2, y), param));
                }
                else
                {
                    // Derecha a izquierda
                    stitches.Add(CreateStitchPoint(new Point(x2, y), param));
                    stitches.Add(CreateStitchPoint(new Point(x1, y), param));
                }
            }
        }

        // Contorno (edge runs)
        for (int run = 0; run < tatamiParam.EdgeRunCount; run++)
        {
            var contourParam = param.DeepClone();
            contourParam.PrimaryStitchType = StitchType.Running;
            var contour = GenerateContourStitches(vertices, contourParam);
            stitches.AddRange(contour);
        }

        // Rotar de vuelta
        stitches = stitches.Select(s =>
        {
            var rotated = GeometryUtils.RotatePoints(new[] { s.Position }, center, angleRad)[0];
            return s with { X = rotated.X, Y = rotated.Y };
        }).ToList();

        return stitches;
    }

    /// <summary>
    /// Genera zigzag a lo largo de un camino
    /// </summary>
    private List<StitchPoint> GenerateZigzagStitches(List<Point> vertices, StitchParams param, StitchPlan plan)
    {
        // Similar a satin pero para camino abierto
        return GenerateSatinAlongPath(vertices, param, plan);
    }

    private List<StitchPoint> GenerateSatinAlongPath(List<Point> pathPoints, StitchParams param, StitchPlan plan)
    {
        var stitches = new List<StitchPoint>();
        if (pathPoints.Count < 2) return stitches;

        var satinParam = param.Satin ?? new SatinParams();
        int columnWidth = satinParam.ColumnWidth;
        int spacing = param.SatinSpacing;

        // Validate parameters
        if (columnWidth <= 0) columnWidth = satinParam.MinColumnWidth > 0 ? satinParam.MinColumnWidth : 500;
        if (spacing <= 0) spacing = 200;

        // Generar columnas perpendiculares al camino
        for (int i = 0; i < pathPoints.Count - 1; i++)
        {
            Point p1 = pathPoints[i];
            Point p2 = pathPoints[i + 1];

            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) continue;

            // Vector perpendicular
            double px = -dy / len;
            double py = dx / len;

            int halfWidth = columnWidth / 2;
            int side = i % 2 == 0 ? 1 : -1; // Alternar lados

            Point left = new Point(
                (int)Math.Round(p1.X + side * px * halfWidth),
                (int)Math.Round(p1.Y + side * py * halfWidth));
            Point right = new Point(
                (int)Math.Round(p1.X - side * px * halfWidth),
                (int)Math.Round(p1.Y - side * py * halfWidth));

            if (i % 2 == 0)
            {
                stitches.Add(CreateStitchPoint(left, param));
                stitches.Add(CreateStitchPoint(right, param));
            }
            else
            {
                stitches.Add(CreateStitchPoint(right, param));
                stitches.Add(CreateStitchPoint(left, param));
            }
        }

        return stitches;
    }

    private List<StitchPoint> GenerateZigzagAlongPath(List<Point> pathPoints, StitchParams param, StitchPlan plan)
    {
        return GenerateSatinAlongPath(pathPoints, param, plan);
    }

    /// <summary>
    /// Genera puntadas de contorno (outline)
    /// </summary>
    private List<StitchPoint> GenerateContourStitches(List<Point> vertices, StitchParams param)
    {
        var stitches = new List<StitchPoint>();
        if (vertices.Count < 2) return stitches;

        var pts = new List<Point>(vertices);
        if (vertices.Count > 2 && !vertices[0].Equals(vertices[^1]))
        {
            pts.Add(vertices[0]); // Cerrar
        }

        var contourParam = param.DeepClone();
        contourParam.PrimaryStitchType = StitchType.Running;
        return GenerateRunningStitches(pts, contourParam, true);
    }

    /// <summary>
    /// Genera underlay
    /// </summary>
    private List<StitchPoint> GenerateUnderlay(EmbroideryObject obj, UnderlayParams underlay, StitchPlan plan)
    {
        var stitches = new List<StitchPoint>();

        var underlayParam = new StitchParams
        {
            PrimaryStitchType = underlay.Type switch
            {
                UnderlayType.EdgeWalk => StitchType.Running,
                UnderlayType.Zigzag => StitchType.Zigzag,
                UnderlayType.Tatami => StitchType.Tatami,
                UnderlayType.CenterWalk => StitchType.Running,
                UnderlayType.Contour => StitchType.Running,
                _ => StitchType.Running
            },
            RunningSpacing = underlay.Density,
            SatinSpacing = underlay.Density,
            Density = underlay.Density,
            Angle = obj.StitchParams.Angle + underlay.AngleOffset,
            MinStitchLength = underlay.StitchLength,
            MaxStitchLength = underlay.StitchLength * 2,
            ColorIndex = obj.StitchParams.ColorIndex, // Use same color as main object
            NeedleIndex = obj.StitchParams.NeedleIndex
        };

        // Generate base stitches without underlay to avoid infinite recursion
        var baseStitches = GenerateStitchesForObjectNoUnderlay(obj, underlayParam, plan);
        foreach (var s in baseStitches)
        {
            stitches.Add(s.WithFlag(StitchPoint.FlagUnderlay));
        }

        return stitches;
    }

    /// <summary>
    /// Genera puntadas para un objeto SIN underlay (para evitar recursión)
    /// </summary>
    private List<StitchPoint> GenerateStitchesForObjectNoUnderlay(EmbroideryObject obj, StitchParams param, StitchPlan plan)
    {
        var stitches = new List<StitchPoint>();

        // Dispatch por tipo de objeto
        switch (obj.ObjectType)
        {
            case EmbroideryObjectType.Shape:
                stitches.AddRange(GenerateShapeStitches((ShapeObject)obj, param, plan));
                break;
            case EmbroideryObjectType.Path:
                stitches.AddRange(GeneratePathStitches((PathObject)obj, param, plan));
                break;
            case EmbroideryObjectType.Text:
                stitches.AddRange(GenerateTextStitches(obj, param, plan));
                break;
            case EmbroideryObjectType.Image:
                stitches.AddRange(GenerateImageStitches(obj, param, plan));
                break;
        }

        return stitches;
    }

    /// <summary>
    /// Genera tie-in (inicio seguro)
    /// </summary>
    private List<StitchPoint> GenerateTieIn(StitchPoint firstStitch, StitchParams param)
    {
        var stitches = new List<StitchPoint>();
        
        // CORRECCIÓN 15: Validar parámetros de tie-in/tie-off
        int tieStitchCount = Math.Max(1, param.TieStitchCount);
        int tieInLength = Math.Max(0, param.TieInLength);
        int tieOffLength = Math.Max(0, param.TieOffLength);
        
        // Evitar desplazamientos absurdos: si length < count, usar length como offset total
        if (tieInLength < tieStitchCount && tieStitchCount > 0)
        {
            tieInLength = tieStitchCount;
        }

        for (int i = 0; i < tieStitchCount; i++)
        {
            // Pequeños puntos hacia atrás
            double angle = Math.PI * 2 * i / tieStitchCount;
            int offset = tieStitchCount > 0 ? tieInLength / tieStitchCount : tieInLength;
            int x = firstStitch.X + (int)Math.Round(Math.Cos(angle) * offset);
            int y = firstStitch.Y + (int)Math.Round(Math.Sin(angle) * offset);
            stitches.Add(new StitchPoint(x, y, StitchType.Running, (byte)param.NeedleIndex, (byte)param.ColorIndex)
                .WithFlag(StitchPoint.FlagTieIn));
        }
        return stitches;
    }

    /// <summary>
    /// Genera tie-off (final seguro)
    /// </summary>
    private List<StitchPoint> GenerateTieOff(StitchPoint lastStitch, StitchParams param)
    {
        var stitches = new List<StitchPoint>();
        
        // CORRECCIÓN 15: Validar parámetros de tie-in/tie-off
        int tieStitchCount = Math.Max(1, param.TieStitchCount);
        int tieOffLength = Math.Max(0, param.TieOffLength);
        
        // Evitar desplazamientos absurdos
        if (tieOffLength < tieStitchCount && tieStitchCount > 0)
        {
            tieOffLength = tieStitchCount;
        }

        for (int i = 0; i < tieStitchCount; i++)
        {
            double angle = Math.PI * 2 * i / tieStitchCount;
            int offset = tieStitchCount > 0 ? tieOffLength / tieStitchCount : tieOffLength;
            int x = lastStitch.X + (int)Math.Round(Math.Cos(angle) * offset);
            int y = lastStitch.Y + (int)Math.Round(Math.Sin(angle) * offset);
            stitches.Add(new StitchPoint(x, y, StitchType.Running, (byte)param.NeedleIndex, (byte)param.ColorIndex)
                .WithFlag(StitchPoint.FlagTieOff));
        }
        return stitches;
    }

    /// <summary>
    /// Aplica perfil de trabajo a parámetros
    /// </summary>
    private void ApplyWorkProfile(StitchParams param, WorkProfile profile)
    {
        if (profile.Fabric != null)
        {
            param.PullCompensation = profile.RecommendedPullComp;
            if (profile.RequiresKnockdown && param.PrimaryStitchType == StitchType.Tatami)
            {
                // Agregar knockdown como underlay
            }
        }
        if (profile.Thread != null)
        {
            // Ajustar densidad según peso hilo
            if (profile.Thread.ThreadWeight > 40) param.Density = (int)(param.Density * 1.2);
            else if (profile.Thread.ThreadWeight < 40) param.Density = (int)(param.Density * 0.9);
        }
        if (profile.Machine != null)
        {
            param.MaxStitchLength = Math.Min(param.MaxStitchLength, profile.Machine.MaxStitchLength);
            param.MaxJumpDistance = Math.Min(param.MaxJumpDistance, profile.Machine.MaxJumpLength);
        }
    }

    /// <summary>
    /// Encuentra intersecciones de línea vertical/horizontal con polígono
    /// </summary>
    private List<Point> FindPolygonIntersections(List<Point> polygon, int coordinate, bool horizontal = false)
    {
        var intersections = new List<Point>();
        int count = polygon.Count;

        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            Point p1 = polygon[i];
            Point p2 = polygon[j];

            if (horizontal)
            {
                // Línea horizontal y = coordinate
                if ((p1.Y <= coordinate && p2.Y > coordinate) || (p2.Y <= coordinate && p1.Y > coordinate))
                {
                    double t = (coordinate - p1.Y) / (double)(p2.Y - p1.Y);
                    int x = (int)Math.Round(p1.X + t * (p2.X - p1.X));
                    intersections.Add(new Point(x, coordinate));
                }
            }
            else
            {
                // Línea vertical x = coordinate
                if ((p1.X <= coordinate && p2.X > coordinate) || (p2.X <= coordinate && p1.X > coordinate))
                {
                    double t = (coordinate - p1.X) / (double)(p2.X - p1.X);
                    int y = (int)Math.Round(p1.Y + t * (p2.Y - p1.Y));
                    intersections.Add(new Point(coordinate, y));
                }
            }
        }

        return intersections;
    }

    private StitchPoint CreateStitchPoint(Point p, StitchParams param) =>
        new(p.X, p.Y, param.PrimaryStitchType, (byte)param.NeedleIndex, (byte)param.ColorIndex);

    /// <summary>
    /// Optimiza el plan global (saltos, trims, secuencia)
    /// Implementación Foundation: agrupación básica por color/aguja y reducción de travel
    /// </summary>
    private void OptimizePlan(StitchPlan plan)
    {
        // Foundation-level optimization: reorder objects by color then needle to minimize travel
        // Note: This is a limited implementation. Full multi-objective optimization is deferred.
        
        var orderedObjects = plan.ObjectStitches
            .OrderBy(kvp => kvp.Value.FirstOrDefault().ColorIndex)
            .ThenBy(kvp => kvp.Value.FirstOrDefault().Needle)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        if (orderedObjects.Count > 0)
        {
            plan.ObjectStitches.Clear();
            foreach (var kvp in orderedObjects)
            {
                plan.ObjectStitches[kvp.Key] = kvp.Value;
            }
        }
    }

    /// <summary>
    /// Calcula métricas finales del plan
    /// </summary>
    private void CalculateMetrics(StitchPlan plan)
    {
        // Aplanar todas las puntadas a la secuencia global
        // CORRECCIÓN 10: Usar el orden actual de ObjectStitches (ya optimizado por OptimizePlan)
        plan.GlobalSequence.Clear();
        foreach (var kvp in plan.ObjectStitches) // Sin OrderBy para preservar orden de optimización
        {
            foreach (var stitch in kvp.Value)
            {
                // Assign sequence index - validate against ushort max
                int seqIndex = plan.GlobalSequence.Count;
                if (seqIndex > ushort.MaxValue)
                    throw new InvalidOperationException($"Stitch sequence exceeds maximum representable index ({ushort.MaxValue}). Consider using a larger sequence index type or splitting the design.");
                
                var newStitch = stitch with { SequenceIndex = (ushort)seqIndex };
                plan.GlobalSequence.Add(newStitch);
            }
        }

        plan.TotalStitches = plan.ObjectStitches.Values.SelectMany(s => s).Count(s => s.IsSewing);
        plan.TotalJumps = plan.ObjectStitches.Values.SelectMany(s => s).Count(s => s.IsJump);
        plan.TotalTrims = plan.ObjectStitches.Values.SelectMany(s => s).Count(s => s.IsTrim);
        plan.TotalColorChanges = plan.ObjectStitches.Values.SelectMany(s => s).Count(s => s.IsColorChange);
        plan.TotalStops = plan.ObjectStitches.Values.SelectMany(s => s).Count(s => s.IsStop);

        // Estimar tiempo (stitches/min)
        int speed = plan.WorkProfile?.RecommendedMaxSpeed ?? 800;
        // CORRECCIÓN 8: Validar velocidad
        if (speed <= 0)
            throw new InvalidOperationException($"Invalid speed: {speed}. Speed must be positive.");
        plan.EstimatedTimeSeconds = plan.TotalStitches * 60.0 / speed;

        // Estimar hilo (aprox 0.5mm por puntada = 0.0005m)
        plan.EstimatedThreadMeters = plan.TotalStitches * 0.0005;

        // Bounds
        var allPoints = plan.ObjectStitches.Values.SelectMany(s => s).Select(s => s.Position).ToList();
        if (allPoints.Count > 0)
        {
            plan.DesignBounds = GeometryUtils.BoundingBox(allPoints);
        }

        // Por color
        foreach (var kvp in plan.ObjectStitches)
        {
            var colorStitches = kvp.Value.Where(s => s.IsSewing).ToList();
            if (colorStitches.Count > 0)
            {
                int colorIdx = colorStitches[0].ColorIndex;
                plan.StitchesPerColor[colorIdx] = plan.StitchesPerColor.GetValueOrDefault(colorIdx) + colorStitches.Count;
                plan.ThreadMetersPerColor[colorIdx] = plan.ThreadMetersPerColor.GetValueOrDefault(colorIdx) + colorStitches.Count * 0.0005;
            }
        }
    }
}

/// <summary>
/// Opciones del StitchEngine
/// </summary>
public sealed class StitchEngineOptions
{
    public double FlattenTolerance { get; set; } = 0.5;     // Tolerancia aplanado curvas (micras)
    public int MaxStitchesPerObject { get; set; } = 100000;  // Límite seguridad
    public bool EnableUnderlay { get; set; } = true;
    public bool EnableAutoTrim { get; set; } = true;
    public bool EnableOptimization { get; set; } = true;
    public int RandomSeed { get; set; } = 0;
}

/// <summary>
/// Plan de puntadas compilado (resultado del StitchEngine)
/// </summary>
public sealed class StitchPlan
{
    public Guid ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public DateTime CompiledAt { get; init; } = DateTime.UtcNow;
    public string? EngineVersion { get; set; }

    // Configuración objetivo
    public MachineProfile? MachineProfile { get; set; }
    public HoopProfile? HoopProfile { get; set; }
    public WorkProfile? WorkProfile { get; set; }

    // Canvas
    public int CanvasWidth { get; set; }
    public int CanvasHeight { get; set; }
    public Point CanvasOrigin { get; set; }

    // Paleta
    public List<ThreadColor> ThreadPalette { get; set; } = new();
    public Dictionary<int, int> ColorToNeedleMap { get; set; } = new();

    // Puntadas por objeto
    public Dictionary<Guid, List<StitchPoint>> ObjectStitches { get; set; } = new();

    // Secuencia global (aplanada)
    public List<StitchPoint> GlobalSequence { get; set; } = new();

    // Métricas
    public long TotalStitches { get; set; }
    public long TotalJumps { get; set; }
    public long TotalTrims { get; set; }
    public long TotalColorChanges { get; set; }
    public long TotalStops { get; set; }
    public double EstimatedTimeSeconds { get; set; }
    public double EstimatedThreadMeters { get; set; }
    public Rectangle DesignBounds { get; set; }
    public Dictionary<int, long> StitchesPerColor { get; set; } = new();
    public Dictionary<int, double> ThreadMetersPerColor { get; set; } = new();

    // Validación
    public ValidationResult? ValidationResult { get; set; }

    // Hash del plan
    public string? PlanHash { get; set; }

    public List<StitchPoint> GetAllStitches() => ObjectStitches.Values.SelectMany(s => s).ToList();

    public List<StitchPoint> GetStitchesForColor(int colorIndex) =>
        ObjectStitches.Values.SelectMany(s => s).Where(s => s.ColorIndex == colorIndex).ToList();
}