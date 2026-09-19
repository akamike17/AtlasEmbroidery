namespace AtlasEmbroidery.Domain.Models;

/// <summary>
/// Un punto de puntada individual con tipo y metadatos.
/// Inmutable, ligero, optimizado para arrays grandes.
/// </summary>
public readonly record struct StitchPoint(
    int X,
    int Y,
    StitchType Type = StitchType.Running,
    byte Needle = 1,
    byte ColorIndex = 0,
    ushort Flags = 0,
    ushort SequenceIndex = 0)
{
    public static readonly StitchPoint Empty = new(int.MinValue, int.MinValue, StitchType.End);

    public bool IsEmpty => X == int.MinValue && Y == int.MinValue;
    public Point Position => new(X, Y);

    // Flags bit a bit para extensibilidad sin romper serialización
    public const ushort FlagTrim = 1 << 0;           // Trim después de este punto
    public const ushort FlagJump = 1 << 1;           // Es un salto (move sin coser)
    public const ushort FlagStop = 1 << 2;           // Parada máquina
    public const ushort FlagColorChange = 1 << 3;    // Cambio de color
    public const ushort FlagNeedleChange = 1 << 4;   // Cambio de aguja
    public const ushort FlagTieIn = 1 << 5;          // Tie-in (inicio seguro)
    public const ushort FlagTieOff = 1 << 6;         // Tie-off (final seguro)
    public const ushort FlagUnderlay = 1 << 7;       // Punto de underlay
    public const ushort FlagAuto = 1 << 8;           // Generado automáticamente
    public const ushort FlagManual = 1 << 9;         // Editado manualmente
    public const ushort FlagSequin = 1 << 10;        // Punto de lentejuela

    public bool HasFlag(ushort flag) => (Flags & flag) != 0;
    public StitchPoint WithFlag(ushort flag) => this with { Flags = (ushort)(Flags | flag) };
    public StitchPoint WithoutFlag(ushort flag) => this with { Flags = (ushort)(Flags & ~flag) };

    public bool IsJump => Type == StitchType.Jump || HasFlag(FlagJump);
    public bool IsTrim => Type == StitchType.Trim || HasFlag(FlagTrim);
    public bool IsStop => Type == StitchType.Stop || HasFlag(FlagStop);
    public bool IsColorChange => Type == StitchType.ColorChange || HasFlag(FlagColorChange);
    public bool IsNeedleChange => Type == StitchType.NeedleChange || HasFlag(FlagNeedleChange);
    public bool IsControl => Type.IsControl() || IsJump || IsTrim || IsStop || IsColorChange || IsNeedleChange;
    public bool IsSewing => Type.IsSewing() && !IsControl;
    public bool IsUnderlay => HasFlag(FlagUnderlay);

    public StitchPoint AsJump() => this with { Type = StitchType.Jump, Flags = (ushort)(Flags | FlagJump) };
    public StitchPoint AsTrim() => this with { Type = StitchType.Trim, Flags = (ushort)(Flags | FlagTrim) };
    public StitchPoint AsStop() => this with { Type = StitchType.Stop, Flags = (ushort)(Flags | FlagStop) };

    public double DistanceTo(StitchPoint other) =>
        Math.Sqrt(Math.Pow(other.X - X, 2) + Math.Pow(other.Y - Y, 2));

    public StitchPoint Translate(int dx, int dy) => new(X + dx, Y + dy, Type, Needle, ColorIndex, Flags);
    public StitchPoint Scale(double factor) =>
        new((int)Math.Round(X * factor), (int)Math.Round(Y * factor), Type, Needle, ColorIndex, Flags);

    public override string ToString() =>
        IsEmpty ? "StitchPoint.Empty" :
        $"{Type.ToDisplayName()}[{X},{Y}] N{Needle} C{ColorIndex} F{Flags:X4}";
}