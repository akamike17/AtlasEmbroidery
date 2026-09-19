namespace AtlasEmbroidery.Domain.Models;

/// <summary>
/// Perfil de máquina (capacidades, límites, formatos soportados)
/// </summary>
public sealed class MachineProfile : ICloneable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Model { get; set; } = "";
    public string ControllerFirmware { get; set; } = "";
    public string? Description { get; set; }

    // Área de bordado (micras)
    public int MaxWidth { get; set; } = 400000;   // 400mm
    public int MaxHeight { get; set; } = 400000;  // 400mm

    // Hoops disponibles
    public List<HoopProfile> Hoops { get; set; } = new();

    // Agujas y colores
    public int NeedleCount { get; set; } = 15;
    public int MaxColorChanges { get; set; } = 250;

    // Límites de puntada
    public int MaxStitchLength { get; set; } = 12700;  // 12.7mm (formato DST)
    public int MinStitchLength { get; set; } = 100;    // 0.1mm
    public int MaxJumpLength { get; set; } = 12700;    // 12.7mm
    public int MaxStitchesPerColor { get; set; } = 65535;
    public int MaxTotalStitches { get; set; } = 2000000;

    // Comandos soportados
    public bool SupportsTrim { get; set; } = true;
    public bool SupportsJump { get; set; } = true;
    public bool SupportsColorChange { get; set; } = true;
    public bool SupportsNeedleChange { get; set; } = true;
    public bool SupportsStop { get; set; } = true;
    public bool SupportsSequins { get; set; } = false;
    public bool SupportsPuff3D { get; set; } = false;

    // Formatos nativos
    public List<string> NativeFormats { get; set; } = new() { "DST" };

    // Velocidad (stitches/min)
    public int MaxSpeed { get; set; } = 1000;
    public int MinSpeed { get; set; } = 200;

    // Metadatos
    public bool IsBuiltIn { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    public object Clone()
    {
        var clone = (MachineProfile)MemberwiseClone();
        clone.Id = Guid.NewGuid();
        clone.Hoops = Hoops.Select(h => h.DeepClone()).ToList();
        clone.CreatedAt = DateTime.UtcNow;
        clone.ModifiedAt = DateTime.UtcNow;
        return clone;
    }

    public MachineProfile DeepClone() => (MachineProfile)Clone();

    public Rectangle GetMaxEmbroideryArea() => new(0, 0, MaxWidth, MaxHeight);

    public HoopProfile? GetHoopByName(string name) =>
        Hoops.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    // Propiedades de compatibilidad para simulación (derivadas)
    public int MaxJumpSpeedMmMin => MaxSpeed * 2;           // Velocidad salto ~2x costura
    public int MaxStitchSpeedRpm => MaxSpeed;               // RPM máxima
    public int MaxStitchSpeedMmMin => MaxSpeed;             // mm/min
    public int MaxFieldWidthMm => MaxWidth / 1000;          // mm
    public int MaxFieldHeightMm => MaxHeight / 1000;        // mm

    public static MachineProfile Default() => CreateTajimaDefault();

    public static MachineProfile CreateTajimaDefault() => new()
    {
        Name = "Tajima Default",
        Brand = "Tajima",
        Model = "TFMX",
        NeedleCount = 15,
        NativeFormats = new() { "DST", "DSB" },
        Hoops = new()
        {
            HoopProfile.CreateStandard("Standard 400x400", 400000, 400000),
            HoopProfile.CreateStandard("Cap 360x200", 360000, 200000),
            HoopProfile.CreateStandard("Sleeve 100x400", 100000, 400000)
        }
    };

    public static MachineProfile CreateBrotherDefault() => new()
    {
        Name = "Brother Default",
        Brand = "Brother",
        Model = "PR1050X",
        NeedleCount = 10,
        NativeFormats = new() { "PES", "PEC", "PHC" },
        Hoops = new()
        {
            HoopProfile.CreateStandard("Standard 300x200", 300000, 200000),
            HoopProfile.CreateStandard("Large 300x300", 300000, 300000),
            HoopProfile.CreateStandard("Cap 180x130", 180000, 130000)
        }
    };
}

/// <summary>
/// Perfil de bastidor (hoop)
/// </summary>
public sealed class HoopProfile : ICloneable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public int Width { get; set; }      // micras
    public int Height { get; set; }     // micras
    public int UsableWidth { get; set; }   // Área útil real
    public int UsableHeight { get; set; }  // Área útil real
    public HoopType Type { get; set; } = HoopType.Flat;
    public string? MachineBrand { get; set; }
    public string? MachineModel { get; set; }
    public Point CenterOffset { get; set; } = Point.Zero;  // Offset del centro respecto al origen máquina
    public bool IsDefault { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    public object Clone()
    {
        var clone = (HoopProfile)MemberwiseClone();
        clone.Id = Guid.NewGuid();
        return clone;
    }

    public HoopProfile DeepClone() => (HoopProfile)Clone();

    public Rectangle GetUsableArea() => new(
        CenterOffset.X - UsableWidth / 2,
        CenterOffset.Y - UsableHeight / 2,
        UsableWidth, UsableHeight);

    public Rectangle GetFullArea() => new(
        CenterOffset.X - Width / 2,
        CenterOffset.Y - Height / 2,
        Width, Height);

    public bool FitsDesign(Rectangle designBounds) =>
        designBounds.Width <= UsableWidth && designBounds.Height <= UsableHeight;

    public static HoopProfile CreateStandard(string name, int width, int height, int? usableWidth = null, int? usableHeight = null) =>
        new()
        {
            Name = name,
            Width = width,
            Height = height,
            UsableWidth = usableWidth ?? (int)(width * 0.9),
            UsableHeight = usableHeight ?? (int)(height * 0.9),
            Type = HoopType.Flat
        };

    public static HoopProfile CreateCap(string name, int width, int height, int? usableWidth = null, int? usableHeight = null) =>
        new()
        {
            Name = name,
            Width = width,
            Height = height,
            UsableWidth = usableWidth ?? (int)(width * 0.85),
            UsableHeight = usableHeight ?? (int)(height * 0.85),
            Type = HoopType.Cap
        };

    // Propiedades de compatibilidad (mm)
    public int WidthMm => Width / 1000;
    public int HeightMm => Height / 1000;

    public static HoopProfile Default() => CreateStandard("Default", 400000, 400000);
}

public enum HoopType : byte
{
    Flat = 0,
    Cap = 1,
    Tubular = 2,
    Sleeve = 3,
    Custom = 4
}

/// <summary>
/// Perfil de material (tela, hilo, aguja, estabilizador)
/// </summary>
public sealed class MaterialProfile : ICloneable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";  // "Tela", "Hilo", "Aguja", "Estabilizador", "Backing", "Topping"
    public string? Brand { get; set; }
    public string? Code { get; set; }
    public string? Description { get; set; }

    // Propiedades tela
    public FabricType? FabricType { get; set; }
    public int WeightGsm { get; set; } = 0;           // Gramaje g/m²
    public int ThicknessMicrons { get; set; } = 0;    // Grosor micras
    public double Elasticity { get; set; } = 0;       // 0-1
    public bool HasNap { get; set; } = false;
    public NapDirection NapDirection { get; set; } = NapDirection.None;

    // Propiedades hilo
    public ThreadMaterial? ThreadMaterial { get; set; }
    public int ThreadWeight { get; set; } = 40;       // wt (40 estándar)
    public ThreadColor? ThreadColor { get; set; }

    // Propiedades aguja
    public NeedleSystem? NeedleSystem { get; set; }
    public NeedlePoint? NeedlePoint { get; set; }
    public double NeedleSize { get; set; } = 75;      // Nm (75 = 75/11)

    // Propiedades estabilizador
    public StabilizerType? StabilizerType { get; set; }
    public int StabilizerLayers { get; set; } = 1;
    public int StabilizerWeightGsm { get; set; } = 0;

    // Densidad máxima recomendada para este material (stitches/mm²)
    public double MaxDensityStitchesPerMm2 { get; set; } = 10.0;

    // Confianza (sección 7 del spec)
    public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Custom;
    public string? EvidenceSource { get; set; }
    public DateTime? ValidatedAt { get; set; }
    public string? ValidatedBy { get; set; }

    public bool IsSystemTemplate { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    public object Clone()
    {
        var clone = (MaterialProfile)MemberwiseClone();
        clone.Id = Guid.NewGuid();
        clone.CreatedAt = DateTime.UtcNow;
        clone.ModifiedAt = DateTime.UtcNow;
        return clone;
    }

    public MaterialProfile DeepClone() => (MaterialProfile)Clone();
}

public enum FabricType : byte
{
    Unknown = 0,
    Cotton = 1,
    Polyester = 2,
    CottonPoly = 3,
    Jersey = 4,
    Pique = 5,
    Denim = 6,
    Canvas = 7,
    Towel = 8,
    Fleece = 9,
    Silk = 10,
    Wool = 11,
    Leather = 12,
    Synthetic = 13,
    Blend = 14,
    Technical = 15
}

public enum ThreadMaterial : byte
{
    Unknown = 0,
    Polyester = 1,
    Rayon = 2,
    Cotton = 3,
    Metallic = 4,
    Silk = 5,
    Wool = 6,
    FireRetardant = 7,
    GlowInDark = 8
}

public enum NeedleSystem : byte
{
    Unknown = 0,
    DBx1 = 1,       // 130/705H estándar doméstica
    DBxK5 = 2,      // Industrial
    ELx705 = 3,     // Overlock
    SY1906 = 4,     // Especial
    Custom = 5
}

public enum NeedlePoint : byte
{
    Unknown = 0,
    Sharp = 1,       // Punta aguda (tejidos tejidos)
    BallPoint = 2,   // Punta bola (tejidos de punto)
    Universal = 3,   // Universal
    Leather = 4,     // Cuero
    Metallic = 5,    // Metálico
    Topstitch = 6    // Topstitch
}

public enum StabilizerType : byte
{
    Unknown = 0,
    TearAway = 1,
    CutAway = 2,
    WashAway = 3,
    HeatAway = 4,
    Fusible = 5,
    NoShow = 6,
    CapBacking = 7,
    Topping = 8
}

public enum NapDirection : byte
{
    None = 0,
    Up = 1,
    Down = 2,
    Left = 3,
    Right = 4
}

public enum ConfidenceLevel : byte
{
    Custom = 0,           // ⚪ Personalizada
    Experimental = 1,     // 🟡 Experimental
    Verified = 2,         // 🔵 Verificada (documentación técnica)
    Validated = 3         // 🟢 Validada (doc + pruebas + evidencia física)
}