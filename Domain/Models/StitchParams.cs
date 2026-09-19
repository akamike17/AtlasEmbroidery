namespace AtlasEmbroidery.Domain.Models;

/// <summary>
/// Parámetros de puntada para un objeto (shape, texto, etc.).
/// Configuración completa que el Stitch Engine usa para generar puntadas.
/// </summary>
public sealed class StitchParams : ICloneable
{
    // Tipo principal de puntada
    public StitchType PrimaryStitchType { get; set; } = StitchType.Tatami;

    // Densidad / espaciado (en micras, 0.1mm = 100 micras)
    public int Density { get; set; } = 400;           // Espaciado entre líneas de puntada (tatami)
    public int SatinSpacing { get; set; } = 200;      // Espaciado para satin (columnas)
    public int RunningSpacing { get; set; } = 250;    // Espaciado running stitch

    // Longitud de puntada (micras)
    public int MinStitchLength { get; set; } = 100;   // 1mm mínimo
    public int MaxStitchLength { get; set; } = 4000;  // 4mm máximo
    public int PreferredStitchLength { get; set; } = 3000; // 3mm preferido

    // Ángulo (grados * 10 para precisión 0.1°)
    public int Angle { get; set; } = 450;             // 45° por defecto
    public int AngleVariation { get; set; } = 0;      // Variación aleatoria ±

    // Underlay
    public UnderlayParams? Underlay { get; set; }

    // Compensación de pull (micras)
    public int PullCompensation { get; set; } = 200;  // 0.2mm
    public int PushCompensation { get; set; } = 0;    // Compensación push

    // Overlap / solape (micras)
    public int Overlap { get; set; } = 200;           // Solape entre objetos adyacentes

    // Tie-in / Tie-off
    public bool UseTieIn { get; set; } = true;
    public bool UseTieOff { get; set; } = true;
    public int TieInLength { get; set; } = 1000;      // 1mm
    public int TieOffLength { get; set; } = 1000;     // 1mm
    public int TieStitchCount { get; set; } = 3;

    // Trim policy
    public TrimPolicy TrimPolicy { get; set; } = TrimPolicy.Auto;
    public int MinTrimDistance { get; set; } = 2000;  // 2mm mínimo para trim

    // Travel / desplazamiento
    public TravelPolicy TravelPolicy { get; set; } = TravelPolicy.Optimize;
    public int MaxJumpDistance { get; set; } = 10000; // 10mm máximo salto

    // Color / aguja
    public int ColorIndex { get; set; } = 0;
    public int NeedleIndex { get; set; } = 1;

    // Orden de secuencia (para multi-objeto)
    public int SequenceOrder { get; set; } = 0;

    // Propiedades específicas por tipo
    public SatinParams? Satin { get; set; }
    public TatamiParams? Tatami { get; set; }
    public MotifParams? Motif { get; set; }

    // Metadatos
    public string? Name { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, object> CustomData { get; set; } = new();

    public object Clone() => MemberwiseClone();

    public StitchParams DeepClone()
    {
        var clone = (StitchParams)MemberwiseClone();
        clone.Underlay = Underlay?.DeepClone();
        clone.Satin = Satin?.DeepClone();
        clone.Tatami = Tatami?.DeepClone();
        clone.Motif = Motif?.DeepClone();
        clone.CustomData = new Dictionary<string, object>(CustomData);
        return clone;
    }

    public static StitchParams DefaultFor(StitchType type) => type switch
    {
        StitchType.Running => new() { PrimaryStitchType = StitchType.Running, RunningSpacing = 250 },
        StitchType.Triple => new() { PrimaryStitchType = StitchType.Triple, RunningSpacing = 300 },
        StitchType.Satin => new() { PrimaryStitchType = StitchType.Satin, SatinSpacing = 200, Satin = new() },
        StitchType.Tatami => new() { PrimaryStitchType = StitchType.Tatami, Density = 400, Tatami = new() },
        StitchType.Zigzag => new() { PrimaryStitchType = StitchType.Zigzag, SatinSpacing = 300 },
        _ => new() { PrimaryStitchType = type }
    };
}

/// <summary>
/// Parámetros de underlay (base)
/// </summary>
public sealed class UnderlayParams : ICloneable
{
    public UnderlayType Type { get; set; } = UnderlayType.EdgeWalk;
    public int Density { get; set; } = 800;           // Más espaciado que puntada principal
    public int Inset { get; set; } = 200;             // Inset desde borde (micras)
    public int AngleOffset { get; set; } = 900;       // 90° perpendicular al principal
    public int StitchLength { get; set; } = 3000;     // 3mm
    public bool Enabled { get; set; } = true;

    public object Clone() => MemberwiseClone();
    public UnderlayParams DeepClone() => (UnderlayParams)MemberwiseClone();
}

public enum UnderlayType : byte
{
    None = 0,
    EdgeWalk = 1,       // Contorno
    Zigzag = 2,         // Zigzag ligero
    Tatami = 3,         // Tatami ligero (center walk)
    DoubleZigzag = 4,   // Doble zigzag
    CenterWalk = 5,     // Línea central
    Contour = 6         // Contorno múltiple
}

/// <summary>
/// Parámetros específicos de Satin
/// </summary>
public sealed class SatinParams : ICloneable
{
    public int ColumnWidth { get; set; } = 3000;      // Ancho columna (micras) - 3mm
    public int MinColumnWidth { get; set; } = 500;    // 0.5mm
    public int MaxColumnWidth { get; set; } = 10000;  // 10mm
    public SatinCornerType CornerType { get; set; } = SatinCornerType.Mitered;
    public bool AutoSplit { get; set; } = true;       // Auto-dividir columnas anchas
    public int SplitCount { get; set; } = 0;          // 0 = auto
    public int ShortStitchThreshold { get; set; } = 500; // Umbral puntada corta
    public bool UseRamping { get; set; } = true;      // Ramping en esquinas

    public object Clone() => MemberwiseClone();
    public SatinParams DeepClone() => (SatinParams)MemberwiseClone();
}

public enum SatinCornerType : byte
{
    Mitered = 0,      // Esquina en ángulo
    Capped = 1,       // Con tapa
    Rounded = 2,      // Redondeada
    Auto = 3          // Automático según ángulo
}

/// <summary>
/// Parámetros específicos de Tatami (fill)
/// </summary>
public sealed class TatamiParams : ICloneable
{
    public TatamiPattern Pattern { get; set; } = TatamiPattern.Standard;
    public int StitchOffset { get; set; } = 0;        // Offset entre filas (0-100%)
    public int RowSpacing { get; set; } = 400;        // Espaciado filas (micras)
    public bool AlternateRows { get; set; } = true;   // Filas alternadas
    public bool RandomizeOffset { get; set; } = false; // Offset aleatorio
    public int RandomSeed { get; set; } = 0;
    public bool UseUnderlay { get; set; } = true;
    public int EdgeRunCount { get; set; } = 1;        // Vueltas de contorno

    public object Clone() => MemberwiseClone();
    public TatamiParams DeepClone() => (TatamiParams)MemberwiseClone();
}

public enum TatamiPattern : byte
{
    Standard = 0,       // Estándar
    Brick = 1,          // Ladrillo (offset 50%)
    Satin = 2,          // Tipo satin
    Radial = 3,         // Radial
    Contour = 4,        // Seguir contorno
    Programmable = 5    // Patrón personalizado
}

/// <summary>
/// Parámetros de Motif
/// </summary>
public sealed class MotifParams : ICloneable
{
    public string MotifName { get; set; } = "";
    public int MotifWidth { get; set; } = 1000;
    public int MotifHeight { get; set; } = 1000;
    public int SpacingX { get; set; } = 1000;
    public int SpacingY { get; set; } = 1000;
    public int Angle { get; set; } = 0;
    public bool LockToGrid { get; set; } = true;

    public object Clone() => MemberwiseClone();
    public MotifParams DeepClone() => (MotifParams)MemberwiseClone();
}

/// <summary>
/// Política de trim (corte de hilo)
/// </summary>
public enum TrimPolicy : byte
{
    Never = 0,      // Nunca cortar
    Auto = 1,       // Automático según distancia
    Always = 2,     // Siempre cortar en jumps
    Manual = 3      // Solo donde el usuario indique
}

/// <summary>
/// Política de travel (desplazamiento entre objetos)
/// </summary>
public enum TravelPolicy : byte
{
    Direct = 0,         // Directo (salto)
    Optimize = 1,       // Optimizar ruta
    FollowContour = 2,  // Seguir contornos
    MinimizeJumps = 3,  // Minimizar saltos
    ShortestPath = 4    // Camino más corto
}