namespace AtlasEmbroidery.Domain.Models;

/// <summary>
/// Formato nativo ATB (AtlasBordado) - Documento principal
/// Contiene todo: geometría, raster original, vectores, texto, objetos de bordado,
/// tipos/parámetros de puntada, underlay, compensación, ángulos, conectores,
/// secuencia, hilos, agujas, trims, jumps, stops, cambios de color/aguja,
/// bastidor, material, máquina, versiones, procedencia, confianza, resultados Validator, hashes.
/// </summary>
public sealed class AtlasProject : ICloneable
{
    // Identidad y versión
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled";
    public string? Description { get; set; }
    public int FormatVersion { get; set; } = 1;        // Versión formato ATB
    public int AppVersionMajor { get; set; } = 1;      // Versión app que creó
    public int AppVersionMinor { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSavedAt { get; set; }
    public Guid? ParentProjectId { get; set; }         // Para versionado/derivados
    public string? SourceFilePath { get; set; }        // Archivo origen (DST, PES, SVG, etc.)
    public string? SourceFileHash { get; set; }        // SHA256 del archivo origen

    // Canvas / área de trabajo (micras)
    public int CanvasWidth { get; set; } = 400000;     // 400mm
    public int CanvasHeight { get; set; } = 400000;    // 400mm
    public Point CanvasOrigin { get; set; } = Point.Zero;

    // Imagen raster original (referencia)
    public RasterReference? OriginalRaster { get; set; }

    // Objetos de bordado (ordenados por SequenceOrder)
    public List<EmbroideryObject> Objects { get; set; } = new();

    // Paleta de hilos (orden = índice de color)
    public List<ThreadColor> ThreadPalette { get; set; } = new();

    // Mapeo color -> aguja (para máquinas multi-aguja)
    public Dictionary<int, int> ColorToNeedleMap { get; set; } = new();

    // Perfil de máquina objetivo
    public MachineProfile? TargetMachine { get; set; }

    // Bastidor seleccionado
    public HoopProfile? SelectedHoop { get; set; }

    // Perfil de material/trabajo (AutoSetup)
    public WorkProfile? WorkProfile { get; set; }

    // Secuencia de ejecución (índices de Objects)
    public List<int> StitchSequence { get; set; } = new();

    // Resultados del Validator
    public ValidationResult? ValidationResult { get; set; }

    // Simulación / preview
    public SimulationResult? SimulationResult { get; set; }

    // Metadatos extensibles
    public Dictionary<string, object> CustomData { get; set; } = new();

    // Hashes de integridad
    public string? ContentHash { get; set; }           // SHA256 del contenido serializable
    public string? StitchPlanHash { get; set; }        // Hash del plan de puntadas compilado

    public object Clone()
    {
        var clone = (AtlasProject)MemberwiseClone();
        clone.Id = Guid.NewGuid();
        clone.Objects = Objects.Select(o => o.DeepClone()).ToList();
        clone.ThreadPalette = new List<ThreadColor>(ThreadPalette);
        clone.ColorToNeedleMap = new Dictionary<int, int>(ColorToNeedleMap);
        clone.TargetMachine = TargetMachine?.DeepClone();
        clone.SelectedHoop = SelectedHoop?.DeepClone();
        clone.WorkProfile = WorkProfile?.DeepClone();
        clone.StitchSequence = new List<int>(StitchSequence);
        clone.ValidationResult = ValidationResult?.DeepClone();
        clone.SimulationResult = SimulationResult?.DeepClone();
        clone.CustomData = new Dictionary<string, object>(CustomData);
        clone.OriginalRaster = OriginalRaster?.DeepClone();
        clone.CreatedAt = DateTime.UtcNow;
        clone.ModifiedAt = DateTime.UtcNow;
        return clone;
    }

    public AtlasProject DeepClone() => (AtlasProject)Clone();

    public void Touch() => ModifiedAt = DateTime.UtcNow;

    public void RecalculateBounds()
    {
        if (Objects.Count == 0)
        {
            CanvasWidth = 400000;
            CanvasHeight = 400000;
            return;
        }

        var allBounds = Objects.Where(o => o.Visible).Select(o => o.Bounds).Where(b => b.IsValid).ToList();
        if (allBounds.Count == 0) return;

        int minX = allBounds.Min(b => b.X);
        int minY = allBounds.Min(b => b.Y);
        int maxX = allBounds.Max(b => b.Right);
        int maxY = allBounds.Max(b => b.Bottom);

        // Agregar margen 10%
        int width = maxX - minX;
        int height = maxY - minY;
        int marginX = width / 10;
        int marginY = height / 10;

        CanvasOrigin = new Point(minX - marginX, minY - marginY);
        CanvasWidth = width + 2 * marginX;
        CanvasHeight = height + 2 * marginY;
    }

    public Rectangle GetDesignBounds()
    {
        var visibleObjects = Objects.Where(o => o.Visible).ToList();
        if (visibleObjects.Count == 0) return Rectangle.Empty;

        int minX = visibleObjects.Min(o => o.Bounds.X);
        int minY = visibleObjects.Min(o => o.Bounds.Y);
        int maxX = visibleObjects.Max(o => o.Bounds.Right);
        int maxY = visibleObjects.Max(o => o.Bounds.Bottom);

        return new Rectangle(minX, minY, maxX - minX, maxY - minY);
    }

    public long GetTotalStitchCount()
    {
        // Se calcula después de compilar a StitchPlan
        return CustomData.TryGetValue("TotalStitches", out var val) && val is long l ? l : 0;
    }

    public int GetColorCount() => ThreadPalette.Count;

    public ThreadColor? GetThreadColor(int index) =>
        index >= 0 && index < ThreadPalette.Count ? ThreadPalette[index] : null;

    public int GetNeedleForColor(int colorIndex) =>
        ColorToNeedleMap.TryGetValue(colorIndex, out var needle) ? needle : 1;

    public void EnsureThreadPaletteCapacity(int neededColors)
    {
        while (ThreadPalette.Count < neededColors)
        {
            ThreadPalette.Add(ThreadColor.Black with { Code = ThreadPalette.Count.ToString("D3") });
        }
    }
}

/// <summary>
/// Referencia a imagen raster original (no almacena pixels, solo metadatos + hash)
/// </summary>
public sealed class RasterReference : ICloneable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";
    public string? FilePath { get; set; }
    public string FileHash { get; set; } = "";         // SHA256
    public int Width { get; set; }
    public int Height { get; set; }
    public int DpiX { get; set; } = 96;
    public int DpiY { get; set; } = 96;
    public PixelFormat PixelFormat { get; set; } = PixelFormat.Rgb24;
    public long FileSize { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public Rectangle? CropRect { get; set; }           // Recorte aplicado
    public double? ScaleFactor { get; set; }           // Factor de escala aplicado
    public Dictionary<string, object> Metadata { get; set; } = new();

    public object Clone()
    {
        var clone = (RasterReference)MemberwiseClone();
        clone.Id = Guid.NewGuid();
        clone.Metadata = new Dictionary<string, object>(Metadata);
        return clone;
    }

    public RasterReference DeepClone() => (RasterReference)Clone();
}

public enum PixelFormat : byte
{
    Unknown = 0,
    Rgb24 = 1,
    Rgba32 = 2,
    Gray8 = 3,
    Indexed8 = 4,
    Bgr24 = 5,
    Bgra32 = 6
}

/// <summary>
/// Perfil de trabajo combinado (Material + Tipo trabajo + Máquina + Hilo/Aguja + Proceso/Bastidor)
/// Sección 7.2 del spec
/// </summary>
public sealed class WorkProfile : ICloneable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    // Componentes del perfil combinado
    public MaterialProfile? Fabric { get; set; }
    public MaterialProfile? Thread { get; set; }
    public MaterialProfile? Needle { get; set; }
    public MaterialProfile? Stabilizer { get; set; }
    public MaterialProfile? Backing { get; set; }
    public MaterialProfile? Topping { get; set; }
    public MachineProfile? Machine { get; set; }
    public HoopProfile? Hoop { get; set; }

    // Parámetros derivados/calculados
    public int RecommendedDensity { get; set; } = 400;
    public int RecommendedUnderlayDensity { get; set; } = 800;
    public int RecommendedPullComp { get; set; } = 200;
    public int RecommendedMaxSpeed { get; set; } = 800;
    public bool RequiresKnockdown { get; set; } = false;
    public bool RequiresTopping { get; set; } = false;
    public bool RequiresCapProfile { get; set; } = false;

    // Confianza global (mínima de componentes)
    public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Custom;

    public bool IsSystemTemplate { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    public object Clone()
    {
        var clone = (WorkProfile)MemberwiseClone();
        clone.Id = Guid.NewGuid();
        clone.Fabric = Fabric?.DeepClone();
        clone.Thread = Thread?.DeepClone();
        clone.Needle = Needle?.DeepClone();
        clone.Stabilizer = Stabilizer?.DeepClone();
        clone.Backing = Backing?.DeepClone();
        clone.Topping = Topping?.DeepClone();
        clone.Machine = Machine?.DeepClone();
        clone.Hoop = Hoop?.DeepClone();
        clone.CreatedAt = DateTime.UtcNow;
        clone.ModifiedAt = DateTime.UtcNow;
        return clone;
    }

    public WorkProfile DeepClone() => (WorkProfile)Clone();

    public static WorkProfile CreateDefault(string fabricCategory = "Cotton") => new()
    {
        Name = $"{fabricCategory} Default",
        Fabric = new MaterialProfile
        {
            Name = fabricCategory,
            Category = "Fabric",
            FabricType = Enum.TryParse<FabricType>(fabricCategory, true, out var ft) ? ft : FabricType.Cotton,
            WeightGsm = 180,
            Confidence = ConfidenceLevel.Verified
        },
        Thread = new MaterialProfile
        {
            Name = "Polyester 40wt",
            Category = "Thread",
            ThreadMaterial = ThreadMaterial.Polyester,
            ThreadWeight = 40,
            Confidence = ConfidenceLevel.Validated
        },
        Needle = new MaterialProfile
        {
            Name = "75/11 Sharp",
            Category = "Needle",
            NeedleSystem = NeedleSystem.DBx1,
            NeedlePoint = NeedlePoint.Sharp,
            NeedleSize = 75,
            Confidence = ConfidenceLevel.Validated
        },
        Stabilizer = new MaterialProfile
        {
            Name = "Tear Away Medium",
            Category = "Stabilizer",
            StabilizerType = StabilizerType.TearAway,
            StabilizerWeightGsm = 50,
            StabilizerLayers = 1,
            Confidence = ConfidenceLevel.Verified
        },
        Confidence = ConfidenceLevel.Verified
    };
}

/// <summary>
/// Resultado de validación (Atlas Validator)
/// </summary>
public sealed class ValidationResult : ICloneable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;
    public ValidationStatus OverallStatus { get; set; } = ValidationStatus.Unknown;
    public List<ValidationIssue> Issues { get; set; } = new();
    public Dictionary<string, object> Metrics { get; set; } = new();
    public string? ValidatorVersion { get; set; }
    public TimeSpan ValidationTime { get; set; }

    public object Clone()
    {
        var clone = (ValidationResult)MemberwiseClone();
        clone.Id = Guid.NewGuid();
        clone.Issues = Issues.Select(i => i.DeepClone()).ToList();
        clone.Metrics = new Dictionary<string, object>(Metrics);
        return clone;
    }

    public ValidationResult DeepClone() => (ValidationResult)Clone();

    public int ErrorCount => Issues.Count(i => i.Severity == ValidationSeverity.Critical);
    public int WarningCount => Issues.Count(i => i.Severity == ValidationSeverity.Warning);
    public int InfoCount => Issues.Count(i => i.Severity == ValidationSeverity.Info);
    public int UnknownCount => Issues.Count(i => i.Severity == ValidationSeverity.Unknown);

    public bool HasCriticalErrors => ErrorCount > 0;
    public bool HasWarnings => WarningCount > 0;

    public void AddIssue(ValidationIssue issue) => Issues.Add(issue);

    public void AddIssue(ValidationSeverity severity, string code, string message,
        string? objectId = null, string? ruleId = null, Dictionary<string, object>? data = null,
        Rectangle? affectedArea = null, List<Point>? affectedPoints = null)
    {
        Issues.Add(new ValidationIssue
        {
            Severity = severity,
            Code = code,
            Message = message,
            ObjectId = objectId,
            RuleId = ruleId,
            Data = data ?? new(),
            AffectedArea = affectedArea,
            AffectedPoints = affectedPoints
        });
    }
}

public enum ValidationStatus : byte
{
    Unknown = 0,      // ⚪ No validado
    Pass = 1,         // 🟢 Apto
    Warning = 2,      // 🟡 Advertencia
    Fail = 3          // 🔴 Crítico
}

public enum ValidationSeverity : byte
{
    Info = 0,         // Informativo
    Warning = 1,      // 🟡 Advertencia - riesgo calculado / recomendación
    Critical = 2,     // 🔴 Crítico - determinista / inviable
    Unknown = 3       // ⚪ Desconocido - hipótesis
}

/// <summary>
/// Hallazgo individual de validación
/// </summary>
public sealed class ValidationIssue : ICloneable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ValidationSeverity Severity { get; set; }
    public string Code { get; set; } = "";           // Código de regla (ej: "STITCH_LEN_MIN")
    public string Message { get; set; } = "";        // Mensaje legible
    public string? ObjectId { get; set; }            // Objeto afectado
    public string? RuleId { get; set; }              // ID de regla técnica
    public Dictionary<string, object> Data { get; set; } = new(); // Evidencia, métricas, etc.
    public string? EvidenceSource { get; set; }      // Fuente de la regla
    public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Custom;
    public Rectangle? AffectedArea { get; set; }     // Área afectada (micras)
    public List<Point>? AffectedPoints { get; set; } // Puntos específicos

    public object Clone()
    {
        var clone = (ValidationIssue)MemberwiseClone();
        clone.Id = Guid.NewGuid();
        clone.Data = new Dictionary<string, object>(Data);
        clone.AffectedPoints = AffectedPoints?.ToList();
        return clone;
    }

    public ValidationIssue DeepClone() => (ValidationIssue)Clone();
}

/// <summary>
/// Resultado de simulación
/// </summary>
public sealed class SimulationResult : ICloneable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime SimulatedAt { get; set; } = DateTime.UtcNow;
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
    public List<SimulationLayer> Layers { get; set; } = new();
    public List<SimulationRisk> Risks { get; set; } = new();
    public bool FitsInHoop { get; set; } = true;
    public bool ExceedsMachineLimits { get; set; } = false;
    public string? SimulatorVersion { get; set; }
    public double GlobalDensityStitchesPerMm2 { get; set; }

    public object Clone()
    {
        var clone = (SimulationResult)MemberwiseClone();
        clone.Id = Guid.NewGuid();
        clone.StitchesPerColor = new Dictionary<int, long>(StitchesPerColor);
        clone.ThreadMetersPerColor = new Dictionary<int, double>(ThreadMetersPerColor);
        clone.Layers = Layers.Select(l => l.DeepClone()).ToList();
        clone.Risks = Risks.Select(r => r.DeepClone()).ToList();
        return clone;
    }

    public SimulationResult DeepClone() => (SimulationResult)Clone();
}

public sealed class SimulationLayer : ICloneable
{
    public int ColorIndex { get; set; }
    public ThreadColor? Color { get; set; }
    public long StitchCount { get; set; }
    public double ThreadMeters { get; set; }
    public Rectangle Bounds { get; set; }
    public int MinStitchLength { get; set; }
    public int MaxStitchLength { get; set; }
    public double AvgDensity { get; set; }
    public bool HasOverlaps { get; set; }
    public bool HasGaps { get; set; }
    public double StartTimeSeconds { get; set; }
    public double EndTimeSeconds { get; set; }
    public double ThreadUsedMeters { get; set; }

    public object Clone() => MemberwiseClone();
    public SimulationLayer DeepClone() => (SimulationLayer)Clone();
}

public sealed class SimulationRisk : ICloneable
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public ValidationSeverity Severity { get; set; }
    public Rectangle? Area { get; set; }
    public double Probability { get; set; } = 0.5;   // 0-1
    public string? Mitigation { get; set; }

    public object Clone() => MemberwiseClone();
    public SimulationRisk DeepClone() => (SimulationRisk)Clone();
}