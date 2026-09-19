namespace AtlasEmbroidery.Domain.Models;

/// <summary>
/// Tipos de puntada soportados (extensible).
/// Valores numéricos estables para serialización y compatibilidad con formatos.
/// </summary>
public enum StitchType : byte
{
    /// <summary>Puntada corrida / running stitch</summary>
    Running = 1,

    /// <summary>Triple / bean stitch (ida-vuelta-ida)</summary>
    Triple = 2,

    /// <summary>Satin / column stitch (zigzag denso)</summary>
    Satin = 3,

    /// <summary>Tatami / fill stitch (relleno)</summary>
    Tatami = 4,

    /// <summary>Zigzag decorativo</summary>
    Zigzag = 5,

    /// <summary>Motif / patrón repetido</summary>
    Motif = 6,

    /// <summary>Contorno / outline</summary>
    Contour = 7,

    /// <summary>Cross stitch (punto de cruz)</summary>
    CrossStitch = 8,

    /// <summary>Photo / thread art (foto realista)</summary>
    Photo = 9,

    /// <summary>Sketch / boceto</summary>
    Sketch = 10,

    /// <summary>Appliqué (tela aplicada)</summary>
    Applique = 11,

    /// <summary>Knockdown (base para telas con nap)</summary>
    Knockdown = 12,

    /// <summary>3D / Puff (relleno con foam)</summary>
    Puff3D = 13,

    /// <summary>Sequins (lentejuelas)</summary>
    Sequins = 14,

    /// <summary>Salto (jump) - movimiento sin coser</summary>
    Jump = 100,

    /// <summary>Corte de hilo (trim)</summary>
    Trim = 101,

    /// <summary>Cambio de color</summary>
    ColorChange = 102,

    /// <summary>Cambio de aguja</summary>
    NeedleChange = 103,

    /// <summary>Parada (stop)</summary>
    Stop = 104,

    /// <summary>Fin de diseño</summary>
    End = 255
}

/// <summary>
/// Extensiones para StitchType
/// </summary>
public static class StitchTypeExtensions
{
    public static bool IsSewing(this StitchType type) =>
        type is >= StitchType.Running and <= StitchType.Sequins;

    public static bool IsControl(this StitchType type) =>
        type >= StitchType.Jump;

    public static bool RequiresThread(this StitchType type) =>
        type.IsSewing();

    public static string ToDisplayName(this StitchType type) => type switch
    {
        StitchType.Running => "Running",
        StitchType.Triple => "Triple/Bean",
        StitchType.Satin => "Satin/Column",
        StitchType.Tatami => "Tatami/Fill",
        StitchType.Zigzag => "Zigzag",
        StitchType.Motif => "Motif",
        StitchType.Contour => "Contour",
        StitchType.CrossStitch => "Cross Stitch",
        StitchType.Photo => "Photo/Thread Art",
        StitchType.Sketch => "Sketch",
        StitchType.Applique => "Appliqué",
        StitchType.Knockdown => "Knockdown",
        StitchType.Puff3D => "3D/Puff",
        StitchType.Sequins => "Sequins",
        StitchType.Jump => "Jump",
        StitchType.Trim => "Trim",
        StitchType.ColorChange => "Color Change",
        StitchType.NeedleChange => "Needle Change",
        StitchType.Stop => "Stop",
        StitchType.End => "End",
        _ => type.ToString()
    };
}