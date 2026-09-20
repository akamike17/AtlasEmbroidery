namespace AtlasEmbroidery.Domain.Formats;

using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Stitching;

/// <summary>
/// Common format adapter interface (legacy - use specific interfaces)
/// </summary>
public interface IFormatAdapter
{
    string FormatName { get; }
    string FileExtension { get; }
    string MimeType { get; }
    FormatCapabilities Capabilities { get; }
    
    Task<AtlasProject> ReadAsync(Stream stream, CancellationToken ct = default);
    Task WriteAsync(AtlasProject project, Stream stream, CancellationToken ct = default);
    AtlasProject Normalize(AtlasProject project);
    Task<RoundTripResult> RoundTripTestAsync(Stream originalStream, CancellationToken ct = default);
    List<SemanticDifference> SemanticDiff(AtlasProject a, AtlasProject b);
    byte[] GenerateFuzzInput(int seed = 0);
}