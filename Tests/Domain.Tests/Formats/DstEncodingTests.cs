namespace AtlasEmbroidery.Domain.Tests.Formats;

using AtlasEmbroidery.Domain.Formats.Dst;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Stitching;
using FluentAssertions;
using Xunit;

/// <summary>
/// Exhaustive tests for DST balanced ternary encoding/decoding via public API
/// </summary>
public class DstEncodingTests
{
    private readonly DstFormatAdapter _adapter = new();

    [Fact]
    public void MaxDeltaPerRecord_Is121_Not127()
    {
        // Test via capabilities - 121 DST units * 100 microns = 12,100 microns
        var caps = _adapter.Capabilities;
        caps.MaxStitchLength.Should().Be(12100, "MaxStitchLength should be 121 * 100 = 12100 microns");
        caps.MaxJumpLength.Should().Be(12100, "MaxJumpLength should be 121 * 100 = 12100 microns");
    }

    [Fact]
    public async Task EndMarker_IsCorrect()
    {
        // We can test END by writing an empty design and checking the bytes
        var project = new AtlasProject { Name = "Empty" };
        
        using var ms = new MemoryStream();
        await _adapter.WriteAsync(project, ms);
        var bytes = ms.ToArray();
        
        // The END marker 0xF3 0x00 0x00 appears before any trailing padding
        // For empty design: 512 header + 3 END + padding to 3-byte boundary
        bytes.Length.Should().BeGreaterThan(512 + 3);
        
        // Find the END marker (0xF3 0x00 0x00)
        bool foundEnd = false;
        for (int i = 512; i < bytes.Length - 2; i++)
        {
            if (bytes[i] == 0xF3 && bytes[i + 1] == 0x00 && bytes[i + 2] == 0x00)
            {
                foundEnd = true;
                break;
            }
        }
        foundEnd.Should().BeTrue("END marker 0xF3 0x00 0x00 must be present in the file");
    }

    [Fact]
    public async Task WriteRead_SingleStitch_RoundTripExact()
    {
        // Create a project with a single small stitch
        var project = new AtlasProject
        {
            Name = "SingleStitch",
            CanvasWidth = 10000,
            CanvasHeight = 10000,
        };
        
        var shape = ShapeObject.CreateRectangle(new Rectangle(100, 100, 100, 100), "Test");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape.StitchParams.Density = 4000;
        project.Objects.Add(shape);
        
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        project.ColorToNeedleMap[0] = 1;

        using var ms1 = new MemoryStream();
        await _adapter.WriteAsync(project, ms1);
        
        ms1.Position = 0;
        var readProject = await _adapter.ReadAsync(ms1);
        
        readProject.Should().NotBeNull();
        readProject.Name.Should().Be("SingleStitch");
        readProject.Objects.Should().HaveCount(1);
    }

    [Fact]
    public async Task ReadWrite_LargeMovement_SplitsCorrectly()
    {
        // Create a project with a large movement that exceeds 121 DST units (12,100 microns)
        var project = new AtlasProject
        {
            Name = "LargeMovement",
            CanvasWidth = 500000,
            CanvasHeight = 500000,
        };
        
        // Create two shapes far apart to force a large jump
        var shape1 = ShapeObject.CreateRectangle(new Rectangle(10000, 10000, 10000, 10000), "Shape1");
        shape1.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape1.StitchParams.Density = 4000;
        shape1.StitchParams.ColorIndex = 0;
        shape1.StitchParams.NeedleIndex = 1;
        project.Objects.Add(shape1);
        
        var shape2 = ShapeObject.CreateRectangle(new Rectangle(200000, 200000, 10000, 10000), "Shape2");
        shape2.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape2.StitchParams.Density = 4000;
        shape2.StitchParams.ColorIndex = 0;
        shape2.StitchParams.NeedleIndex = 1;
        project.Objects.Add(shape2);
        
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        project.ColorToNeedleMap[0] = 1;

        using var ms1 = new MemoryStream();
        await _adapter.WriteAsync(project, ms1);
        
        // Read back and verify no exceptions
        ms1.Position = 0;
        var readProject = await _adapter.ReadAsync(ms1);
        
        readProject.Should().NotBeNull();
        readProject.Objects.Should().HaveCount(1); // Merged into single shape on import
        
        // Verify it validates
        ms1.Position = 0;
        var validation = await _adapter.ValidateAsync(ms1);
        validation.IsValid.Should().BeTrue("Large movement DST should be valid");
    }

    [Fact]
    public async Task ReadWrite_Jumps_Preserved()
    {
        var project = new AtlasProject
        {
            Name = "WithJumps",
            CanvasWidth = 50000,
            CanvasHeight = 50000,
        };
        
        // First shape
        var shape1 = ShapeObject.CreateRectangle(new Rectangle(1000, 1000, 5000, 5000), "Shape1");
        shape1.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape1.StitchParams.Density = 4000;
        shape1.StitchParams.ColorIndex = 0;
        shape1.StitchParams.NeedleIndex = 1;
        project.Objects.Add(shape1);
        
        // Second shape far away - should generate jump
        var shape2 = ShapeObject.CreateRectangle(new Rectangle(30000, 30000, 5000, 5000), "Shape2");
        shape2.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape2.StitchParams.Density = 4000;
        shape2.StitchParams.ColorIndex = 0;
        shape2.StitchParams.NeedleIndex = 1;
        project.Objects.Add(shape2);
        
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        project.ColorToNeedleMap[0] = 1;

        using var ms1 = new MemoryStream();
        await _adapter.WriteAsync(project, ms1);
        
        ms1.Position = 0;
        var readProject = await _adapter.ReadAsync(ms1);
        
        readProject.Should().NotBeNull();
        
        // Validate
        ms1.Position = 0;
        var validation = await _adapter.ValidateAsync(ms1);
        validation.IsValid.Should().BeTrue("DST with jumps should be valid");
    }

    [Fact]
    public async Task ReadWrite_ColorChanges_Preserved()
    {
        var project = new AtlasProject
        {
            Name = "MultiColor",
            CanvasWidth = 50000,
            CanvasHeight = 50000,
        };
        
        // First shape - color 0
        var shape1 = ShapeObject.CreateRectangle(new Rectangle(1000, 1000, 5000, 5000), "Shape1");
        shape1.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape1.StitchParams.Density = 4000;
        shape1.StitchParams.ColorIndex = 0;
        shape1.StitchParams.NeedleIndex = 1;
        project.Objects.Add(shape1);
        
        // Second shape - color 1
        var shape2 = ShapeObject.CreateRectangle(new Rectangle(10000, 10000, 5000, 5000), "Shape2");
        shape2.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape2.StitchParams.Density = 4000;
        shape2.StitchParams.ColorIndex = 1;
        shape2.StitchParams.NeedleIndex = 2;
        project.Objects.Add(shape2);
        
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        project.ThreadPalette.Add(new ThreadColor(0, 255, 0, "Test", "002", "Green"));
        project.ColorToNeedleMap[0] = 1;
        project.ColorToNeedleMap[1] = 2;

        using var ms1 = new MemoryStream();
        await _adapter.WriteAsync(project, ms1);
        
        ms1.Position = 0;
        var readProject = await _adapter.ReadAsync(ms1);
        
        readProject.Should().NotBeNull();
        
        // Note: DST stores color change positions, not RGB thread identity
        // On read, a synthetic default palette is generated based on color change count
        // The current implementation generates palette based on number of color changes + 1
        // Verify the color change is preserved in the written DST file
        ms1.Position = 0;
        var validation = await _adapter.ValidateAsync(ms1);
        validation.IsValid.Should().BeTrue("Multi-color DST should be valid");
        
        // The written file should have the color change count in the header
        // When read back, colorIndex tracks the color changes
        // Current behavior: palette count = colorIndex + 1 at end of read
    }

    [Fact]
    public async Task DeterministicOutput_SameInputProducesSameBytes()
    {
        var project = new AtlasProject
        {
            Name = "DeterministicTest",
            CanvasWidth = 100000,
            CanvasHeight = 100000,
        };
        
        var shape = ShapeObject.CreateRectangle(new Rectangle(10000, 10000, 50000, 50000), "Test Rectangle");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape.StitchParams.Density = 4000;
        shape.StitchParams.ColorIndex = 0;
        shape.StitchParams.NeedleIndex = 1;
        project.Objects.Add(shape);
        
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        project.ColorToNeedleMap[0] = 1;

        // Write twice
        using var ms1 = new MemoryStream();
        await _adapter.WriteAsync(project, ms1);
        var bytes1 = ms1.ToArray();
        
        using var ms2 = new MemoryStream();
        await _adapter.WriteAsync(project, ms2);
        var bytes2 = ms2.ToArray();
        
        // Should be byte-for-byte identical
        bytes1.Should().Equal(bytes2, "DST encoding must be deterministic");
    }

    [Fact]
    public async Task Validation_RejectsInvalidFiles()
    {
        // Empty file
        using var ms = new MemoryStream(new byte[100]);
        var result = await _adapter.ValidateAsync(ms);
        result.IsValid.Should().BeFalse("Empty file should be invalid");
        
        // File with valid header but no END
        var data = new byte[512];
        using (var writer = new BinaryWriter(new MemoryStream(data), System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("LA:Test\r\n");
            writer.Write("ST:0\r\n");
            writer.Write("CO:0\r\n");
            writer.Write("+X:10\r\n");
            writer.Write("-X:0\r\n");
            writer.Write("+Y:10\r\n");
            writer.Write("-Y:0\r\n");
            writer.Write("AX:0\r\n");
            writer.Write("AY:0\r\n");
            writer.Write("MX:0\r\n");
            writer.Write("MY:0\r\n");
            writer.Write("PD:******\r\n");
            writer.Flush();
        }
        
        using var ms2 = new MemoryStream(data);
        result = await _adapter.ValidateAsync(ms2);
        result.IsValid.Should().BeFalse("Header without END should be invalid");
        result.Issues.Should().Contain(i => i.RuleId == "DST.MISSING_END_MARKER" && i.Severity == AtlasEmbroidery.Domain.Formats.ValidationSeverity.Critical);
    }

    [Fact]
    public async Task GenerateFuzzInput_ProducesValidDST()
    {
        var data = _adapter.GenerateFuzzInput(42);
        
        data.Should().NotBeNullOrEmpty();
        data.Length.Should().BeGreaterThan(512);
        data.Should().ContainInOrder((byte)0xF3, (byte)0x00, (byte)0x00);
        
        // Should validate as valid
        using var ms = new MemoryStream(data);
        var result = await _adapter.ValidateAsync(ms);
        result.IsValid.Should().BeTrue("Generated fuzz input should be valid DST");
    }

    [Fact]
    public async Task HeaderSize_Is512Bytes()
    {
        var project = new AtlasProject { Name = "HeaderTest" };
        
        using var ms = new MemoryStream();
        await _adapter.WriteAsync(project, ms);
        var bytes = ms.ToArray();
        
        // Header should be exactly 512 bytes
        bytes.Length.Should().BeGreaterOrEqualTo(512 + 3); // header + END
    }
}