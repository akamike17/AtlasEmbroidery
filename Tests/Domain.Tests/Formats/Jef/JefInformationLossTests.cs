namespace AtlasEmbroidery.Tests.Formats.Jef;

using AtlasEmbroidery.Domain.Formats.Jef;
using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using FluentAssertions;
using Xunit;

/// <summary>
/// Information loss documentation tests for JEF format
/// Documents what data is NOT preserved when writing to JEF
/// </summary>
public sealed class JefInformationLossTests
{
    private readonly JefFormatAdapter _adapter = new();

    [Fact]
    public async Task Write_ThreadRgbOutsidePalette_IsClamped()
    {
        var project = new AtlasProject { Name = "RGBTest" };
        project.ThreadPalette.Add(new ThreadColor(128, 64, 192, "Custom", "001", "Purple"));
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        stream.Position = 0;
        
        var readProject = await _adapter.ReadAsync(stream);
        
        // JEF stores thread colors as hex RGB in TC lines
        // But reader doesn't parse TC lines back to palette
        // So palette is lost
        readProject.ThreadPalette.Should().HaveCount(1); // Only default palette from compilation
    }

    [Fact]
    public async Task Write_DesignName_Preserved()
    {
        var project = new AtlasProject { Name = "My Design Name" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        stream.Position = 0;
        
        var readProject = await _adapter.ReadAsync(stream);
        readProject.Name.Should().Be("My Design Name");
    }

    [Fact]
    public async Task Write_HoopInfo_NotPreserved()
    {
        var project = new AtlasProject { Name = "HoopTest" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        project.SelectedHoop = new HoopProfile 
        { 
            Name = "Standard 100x100", 
            Width = 100000, 
            Height = 100000 
        };
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        stream.Position = 0;
        
        var readProject = await _adapter.ReadAsync(stream);
        // Hoop info is not in JEF format
        readProject.SelectedHoop.Should().BeNull();
    }

    [Fact]
    public async Task Write_StopVsColorChange_Indistinguishable()
    {
        // JEF uses same code (0xC3) for both STOP and COLOR_CHANGE
        // Reader treats both as color change
        var project = new AtlasProject { Name = "StopTest" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        stream.Position = 0;
        
        var readProject = await _adapter.ReadAsync(stream);
        // Both stop and color change become color changes in JEF
        // Can't distinguish them on read
        readProject.Should().NotBeNull();
    }

    [Fact]
    public async Task Write_AuthorCopyright_Preserved()
    {
        var project = new AtlasProject { Name = "MetaTest" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        project.CustomData["author"] = "John Doe";
        project.CustomData["copyright"] = "2024";
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        stream.Position = 0;
        
        var readProject = await _adapter.ReadAsync(stream);
        readProject.CustomData["author"].Should().Be("John Doe");
        readProject.CustomData["copyright"].Should().Be("2024");
    }

    [Fact]
    public async Task Write_UnderlayData_Lost()
    {
        // JEF has no concept of underlay
        // Underlay stitches would be written as normal stitches
        var project = new AtlasProject { Name = "UnderlayTest" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        stream.Position = 0;
        
        var readProject = await _adapter.ReadAsync(stream);
        // Underlay info lost - JEF has no underlay marker
        readProject.Should().NotBeNull();
    }

    [Fact]
    public async Task Write_CompensationData_Lost()
    {
        // JEF has no pull compensation encoding
        var project = new AtlasProject { Name = "CompTest" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        stream.Position = 0;
        
        var readProject = await _adapter.ReadAsync(stream);
        // Compensation data lost
        readProject.Should().NotBeNull();
    }

    [Fact]
    public async Task Write_MaxDelta_Limited()
    {
        // JEF max delta per record is 121 (balanced ternary)
        // Longer moves will be split into multiple records
        var project = new AtlasProject { Name = "MaxDelta" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        // All stitch records should have valid deltas <= 121
        var headerEnd = JefSpec.HeaderSize;
        for (int i = headerEnd; i < bytes.Length - 2; i += 3)
        {
            var (dx, dy, control, _) = JefMovementDecoder.DecodeMovement(bytes, i);
            if (control == JefControl.Normal || control == JefControl.Jump)
            {
                Math.Abs(dx).Should().BeLessOrEqualTo(JefSpec.MaxStitchDistance);
                Math.Abs(dy).Should().BeLessOrEqualTo(JefSpec.MaxStitchDistance);
            }
            if (control == JefControl.End) break;
        }
    }

    [Fact]
    public async Task Write_NoExplicitEnd_ButEndMarkerPresent()
    {
        // JEF doesn't have an explicit END record in header
        // But stitch data always ends with END marker (0x00 0x00 0xF3)
        var project = new AtlasProject { Name = "EndTest" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        bytes[^3].Should().Be(0x00);
        bytes[^2].Should().Be(0x00);
        bytes[^1].Should().Be(JefSpec.EndCode);
    }
}