namespace AtlasEmbroidery.Domain.Formats;

using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Stitching;

/// <summary>
/// Capabilities of an embroidery format
/// </summary>
public sealed class FormatCapabilities
{
    public bool SupportsReading { get; set; }
    public bool SupportsWriting { get; set; }
    public bool SupportsTrim { get; set; }
    public bool SupportsJump { get; set; }
    public bool SupportsColorChange { get; set; }
    public bool SupportsStop { get; set; }
    public bool SupportsSequins { get; set; }
    public bool SupportsPuff3D { get; set; }
    public int MaxStitchLength { get; set; }
    public int MaxJumpLength { get; set; }
    public int MaxStitchesPerColor { get; set; }
    public int MaxTotalStitches { get; set; }
    public int MaxColors { get; set; }
}

/// <summary>
/// Options for reading embroidery formats
/// </summary>
public sealed class FormatReadOptions
{
    public bool ValidateOnly { get; set; } = false;
    public bool StrictMode { get; set; } = true;
    public int MaxStitches { get; set; } = 2_000_000;
    public int MaxColors { get; set; } = 250;
    public CancellationToken CancellationToken { get; set; } = default;
}

/// <summary>
/// Options for writing embroidery formats
/// </summary>
public sealed class FormatWriteOptions
{
    public bool OptimizeForMachine { get; set; } = true;
    public MachineProfile? TargetMachine { get; set; }
    public bool IncludeMetadata { get; set; } = true;
    public CancellationToken CancellationToken { get; set; } = default;
}

/// <summary>
/// Result of format validation
/// </summary>
public sealed class FormatValidationResult
{
    public bool IsValid { get; set; }
    public string FormatName { get; set; } = "";
    public List<ValidationIssue> Issues { get; set; } = new();
    public FormatCapabilities? DetectedCapabilities { get; set; }
}

/// <summary>
/// Validation issue for format validation
/// </summary>
public sealed class ValidationIssue
{
    public string RuleId { get; set; } = "";
    public ValidationSeverity Severity { get; set; }
    public string Message { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public long? Position { get; set; } // Byte position in stream
}

/// <summary>
/// Severity of validation issue
/// </summary>
public enum ValidationSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2,
    Unknown = 3
}

/// <summary>
/// Round-trip test result
/// </summary>
public sealed class RoundTripResult
{
    public string FormatName { get; set; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<SemanticDifference> Differences { get; set; } = new();
}

/// <summary>
/// Semantic difference between two projects
/// </summary>
public sealed class SemanticDifference
{
    public DifferenceType Type { get; set; }
    public string Description { get; set; } = "";
    public string ValueA { get; set; } = "";
    public string ValueB { get; set; } = "";
    public double Severity { get; set; } = 1.0; // 0-1
}

public enum DifferenceType
{
    StitchCount,
    JumpCount,
    TrimCount,
    ColorChangeCount,
    Bounds,
    ColorPalette,
    StitchType,
    SequenceOrder,
    Metadata
}

/// <summary>
/// Reader interface for embroidery formats
/// </summary>
public interface IEmbroideryFormatReader
{
    string FormatName { get; }
    string[] Extensions { get; }
    string MimeType { get; }
    FormatCapabilities Capabilities { get; }
    
    Task<AtlasProject> ReadAsync(Stream stream, FormatReadOptions? options = null, CancellationToken ct = default);
    FormatValidationResult Validate(Stream stream);
    Task<FormatValidationResult> ValidateAsync(Stream stream);
}

/// <summary>
/// Writer interface for embroidery formats
/// </summary>
public interface IEmbroideryFormatWriter
{
    string FormatName { get; }
    string DefaultExtension { get; }
    string MimeType { get; }
    FormatCapabilities Capabilities { get; }
    
    Task WriteAsync(AtlasProject project, Stream stream, FormatWriteOptions? options = null, CancellationToken ct = default);
}

/// <summary>
/// Normalizer interface for embroidery projects
/// </summary>
public interface IEmbroideryNormalizer
{
    AtlasProject Normalize(AtlasProject project, NormalizationProfile? profile = null);
}

/// <summary>
/// Normalization profile
/// </summary>
public sealed class NormalizationProfile
{
    public bool ClampToMachineLimits { get; set; } = true;
    public bool RemoveDuplicateStitches { get; set; } = true;
    public bool FixInvalidCoordinates { get; set; } = true;
    public bool EnsureValidColorPalette { get; set; } = true;
    public int MaxStitchLengthMicrons { get; set; } = 12_700; // 12.7mm
    public int MaxJumpLengthMicrons { get; set; } = 12_700;
    public int MaxColors { get; set; } = 250;
}

/// <summary>
/// Format validator interface
/// </summary>
public interface IEmbroideryFormatValidator
{
    FormatValidationResult Validate(AtlasProject project, MachineProfile? machine = null, HoopProfile? hoop = null);
    FormatValidationResult Validate(StitchPlan plan, MachineProfile? machine = null, HoopProfile? hoop = null);
}