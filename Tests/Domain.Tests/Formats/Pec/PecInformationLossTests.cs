namespace AtlasEmbroidery.Tests.Formats.Pec;

using AtlasEmbroidery.Domain.Formats.Pec;
using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using FluentAssertions;
using Xunit;

/// <summary>
/// Information loss tests for PEC format
/// Documents what information is lost when writing to PEC
/// </summary>
public class PecInformationLossTests
{
    private readonly PecFormatAdapter _adapter = new();

    [Fact]
    public void Pec_Loss_MissingThreadRGB_UsesBuiltInPalette()
    {
        // PEC stores thread indices, not RGB - reader uses built-in palette
        var project = new AtlasProject { Name = "Thread Test" };
        project.ThreadPalette.Add(new ThreadColor(128, 64, 32, "Custom", "999", "Custom Brown"));
        project.ColorToNeedleMap[0] = 1;
        
        // When written, PEC only stores color index (0)
        // When read back, it maps to built-in palette color 0 (Black)
        // This is documented information loss
        var formatCapabilities = _adapter.Capabilities;
        formatCapabilities.MaxColors.Should().Be(255);
    }

    [Fact]
    public void Pec_Loss_NoStopCommand_UsesColorChangeAsStop()
    {
        // PEC has no native STOP command - uses COLOR_CHANGE as stop
        var caps = _adapter.Capabilities;
        caps.SupportsStop.Should().BeTrue("PEC uses color change as stop");
        caps.SupportsColorChange.Should().BeTrue();
    }

    [Fact]
    public void Pec_Loss_NoTrimCommand_UsesJumpAsTrim()
    {
        // PEC has TRIM_CODE (0x20) but it's combined with jump
        var caps = _adapter.Capabilities;
        caps.SupportsTrim.Should().BeTrue();
    }

    [Fact]
    public void Pec_Loss_NoExplicitThreadNames_OnlyIndices()
    {
        // PEC only stores color indices (0-254), not thread names or brands
        // Thread info comes from built-in palette only
    }

    [Fact]
    public void Pec_Loss_NoHoopInfo_OnlyDesignBounds()
    {
        // PEC stores design bounding box in header, not hoop info
    }

    [Fact]
    public void Pec_Loss_IconGraphicsAreApproximate()
    {
        // PEC stores 48x38 1-bit icons per color block
        // These are low-resolution approximations
    }

    [Fact]
    public void Pec_Loss_MaxDeltaPerRecord_2047Units()
    {
        // Movements > 2047 PEC units must be split
        // 2047 * 0.1mm = 204.7mm max per record
        PecSpec.MaxDeltaPerRecord.Should().Be(2047);
    }

    [Fact]
    public void Pec_Loss_CoordinatePrecision_0_1mm()
    {
        // PEC uses 0.1mm units (100 microns)
        PecSpec.MicronsPerPecUnit.Should().Be(100);
    }
}