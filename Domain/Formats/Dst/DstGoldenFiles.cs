namespace AtlasEmbroidery.Domain.Formats.Dst;

using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using AtlasEmbroidery.Domain.Formats;
using System.Text;

/// <summary>
/// DST Golden Test Files - Embedded valid DST files for round-trip testing
/// Updated for correct Tajima DST specification
/// </summary>
public static class DstGoldenFiles
{
    /// <summary>
    /// Minimal valid DST: single running stitch line
    /// </summary>
    public static byte[] SimpleLine => BuildValidDst(
        name: "Simple Line",
        stitches: new[] { (0, 0, DstControl.Normal), (1, 0, DstControl.Normal), (2, 0, DstControl.Normal) },
        colorChanges: 0
    );

    /// <summary>
    /// Square with 4 sides (running stitch)
    /// </summary>
    public static byte[] Square => BuildValidDst(
        name: "Square",
        stitches: new[]
        {
            (0, 0, DstControl.Normal), (10, 0, DstControl.Normal),
            (0, 10, DstControl.Normal), (-10, 0, DstControl.Normal),
            (0, -10, DstControl.Normal)
        },
        colorChanges: 0
    );

    /// <summary>
    /// Multi-color design: two squares with color change
    /// </summary>
    public static byte[] MultiColor => BuildValidDst(
        name: "Multi Color",
        stitches: new[]
        {
            // Color 0: Square
            (0, 0, DstControl.Normal), (10, 0, DstControl.Normal),
            (0, 10, DstControl.Normal), (-10, 0, DstControl.Normal),
            (0, -10, DstControl.Normal),
            // Color change
            (20, 20, DstControl.ColorChange),
            // Color 1: Square
            (0, 0, DstControl.Normal), (10, 0, DstControl.Normal),
            (0, 10, DstControl.Normal), (-10, 0, DstControl.Normal),
            (0, -10, DstControl.Normal)
        },
        colorChanges: 1
    );

    /// <summary>
    /// Design with jumps
    /// </summary>
    public static byte[] WithJumpsAndTrims => BuildValidDst(
        name: "Jumps and Trims",
        stitches: new[]
        {
            // First object
            (0, 0, DstControl.Normal), (5, 0, DstControl.Normal), (0, 5, DstControl.Normal), (-5, 0, DstControl.Normal), (0, -5, DstControl.Normal),
            // Jump to second object
            (20, 20, DstControl.Jump),
            (0, 0, DstControl.Normal), (5, 0, DstControl.Normal), (0, 5, DstControl.Normal), (-5, 0, DstControl.Normal), (0, -5, DstControl.Normal),
        },
        colorChanges: 0
    );

    /// <summary>
    /// Large design near format limits
    /// </summary>
    public static byte[] LargeDesign => BuildValidDst(
        name: "Large Design",
        stitches: GenerateSpiral(centerX: 500, centerY: 500, radius: 400, turns: 3, pointsPerTurn: 50),
        colorChanges: 0
    );

    /// <summary>
    /// Design with many color changes
    /// </summary>
    public static byte[] ManyColors => BuildValidDst(
        name: "Many Colors",
        stitches: GenerateColorBands(),
        colorChanges: 9
    );

    /// <summary>
    /// Satin-like column (dense parallel lines)
    /// </summary>
    public static byte[] SatinColumn => BuildValidDst(
        name: "Satin Column",
        stitches: GenerateSatinColumn(x: 100, y: 100, width: 20, height: 500, spacing: 5),
        colorChanges: 0
    );

    /// <summary>
    /// Tatami-like fill (dense grid)
    /// </summary>
    public static byte[] TatamiFill => BuildValidDst(
        name: "Tatami Fill",
        stitches: GenerateTatamiFill(x: 0, y: 0, width: 500, height: 500, spacing: 20),
        colorChanges: 0
    );

    /// <summary>
    /// Design with stops (color changes without trims)
    /// </summary>
    public static byte[] WithStops => BuildValidDst(
        name: "With Stops",
        stitches: new[]
        {
            (0, 0, DstControl.Normal), (10, 0, DstControl.Normal),
            (0, 0, DstControl.ColorChange),
            (0, 10, DstControl.Normal), (-10, 0, DstControl.Normal),
            (0, 0, DstControl.Normal)
        },
        colorChanges: 1
    );

    /// <summary>
    /// Empty design (header only, no stitches)
    /// </summary>
    public static byte[] EmptyDesign => BuildValidDst(
        name: "Empty",
        stitches: Array.Empty<(int dx, int dy, DstControl control)>(),
        colorChanges: 0
    );

    private static byte[] BuildValidDst(string name, (int dx, int dy, DstControl control)[] stitches, int colorChanges)
    {
        var ms = new MemoryStream();
        var writer = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        // Calculate bounds
        int x = 0, y = 0;
        int minX = 0, maxX = 0, minY = 0, maxY = 0;
        
        foreach (var (dx, dy, _) in stitches)
        {
            x += dx;
            y += dy;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }
        
        // Header
        var header = new DstHeader
        {
            Label = name,
            StitchCount = stitches.Length,
            ColorChanges = colorChanges,
            MinX = minX, MaxX = maxX,
            MinY = minY, MaxY = maxY,
            StartX = 0, StartY = 0,
            EndX = x, EndY = y
        }.ToBytes();
        
        writer.Write(header);

        // Stitch data
        foreach (var (dx, dy, control) in stitches)
        {
            var controlByte = control switch
            {
                DstControl.Normal => DstSpec.StitchNormal,
                DstControl.Jump => DstSpec.StitchJump,
                DstControl.ColorChange => DstSpec.StitchColorChange,
                DstControl.End => DstSpec.StitchEnd,
                _ => DstSpec.StitchNormal
            };
            var (b1, b2, b3) = DstMovementEncoder.EncodeMovement(dx, dy, controlByte);
            writer.Write(b1);
            writer.Write(b2);
            writer.Write(b3);
        }

        // End marker (Tajima spec: 0xF3 0x00 0x00)
        writer.Write(0xF3);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);

        writer.Flush();
        return ms.ToArray();
    }

    private static (int dx, int dy, DstControl control)[] GenerateSpiral(int centerX, int centerY, int radius, int turns, int pointsPerTurn)
    {
        var stitches = new List<(int, int, DstControl)>();
        int lastX = 0, lastY = 0;
        
        for (int t = 0; t < turns * pointsPerTurn; t++)
        {
            double angle = (t * 2 * Math.PI) / pointsPerTurn;
            double r = radius * (1.0 - (double)t / (turns * pointsPerTurn));
            
            int targetX = centerX + (int)(r * Math.Cos(angle));
            int targetY = centerY + (int)(r * Math.Sin(angle));
            
            int dx = targetX - lastX;
            int dy = targetY - lastY;
            
            // Clamp to max delta
            dx = Math.Clamp(dx, -DstSpec.MaxDeltaPerRecord, DstSpec.MaxDeltaPerRecord);
            dy = Math.Clamp(dy, -DstSpec.MaxDeltaPerRecord, DstSpec.MaxDeltaPerRecord);
            
            stitches.Add((dx, dy, DstControl.Normal));
            lastX = targetX;
            lastY = targetY;
        }
        
        return stitches.ToArray();
    }

    private static (int dx, int dy, DstControl control)[] GenerateColorBands()
    {
        var stitches = new List<(int, int, DstControl)>();
        int lastX = 0, lastY = 0;
        
        for (int color = 0; color < 10; color++)
        {
            if (color > 0)
            {
                stitches.Add((0, 0, DstControl.ColorChange));
            }
            
            for (int i = 0; i < 50; i++)
            {
                int x = color * 100 + i * 10;
                int y = color * 100;
                int dx = Math.Clamp(x - lastX, -DstSpec.MaxDeltaPerRecord, DstSpec.MaxDeltaPerRecord);
                int dy = Math.Clamp(y - lastY, -DstSpec.MaxDeltaPerRecord, DstSpec.MaxDeltaPerRecord);
                stitches.Add((dx, dy, DstControl.Normal));
                lastX = x;
                lastY = y;
            }
        }
        
        return stitches.ToArray();
    }

    private static (int dx, int dy, DstControl control)[] GenerateSatinColumn(int x, int y, int width, int height, int spacing)
    {
        var stitches = new List<(int, int, DstControl)>();
        int lastX = 0, lastY = 0;
        int rows = height / spacing;
        
        for (int row = 0; row <= rows; row++)
        {
            int yPos = y + row * spacing;
            int direction = row % 2 == 0 ? 1 : -1;
            int startX = x + (direction == 1 ? 0 : width);
            int endX = x + (direction == 1 ? width : 0);
            
            // Jump to start of row
            if (row == 0)
            {
                int dx = Math.Clamp(startX - lastX, -DstSpec.MaxDeltaPerRecord, DstSpec.MaxDeltaPerRecord);
                int dy = Math.Clamp(yPos - lastY, -DstSpec.MaxDeltaPerRecord, DstSpec.MaxDeltaPerRecord);
                stitches.Add((dx, dy, DstControl.Normal));
            }
            else
            {
                int dx = Math.Clamp(startX - lastX, -DstSpec.MaxDeltaPerRecord, DstSpec.MaxDeltaPerRecord);
                int dy = Math.Clamp(yPos - lastY, -DstSpec.MaxDeltaPerRecord, DstSpec.MaxDeltaPerRecord);
                stitches.Add((dx, dy, DstControl.Jump));
            }
            
            // Stitch across
            int dx2 = Math.Clamp(endX - startX, -DstSpec.MaxDeltaPerRecord, DstSpec.MaxDeltaPerRecord);
            stitches.Add((dx2, 0, DstControl.Normal));
            
            lastX = endX;
            lastY = yPos;
        }
        
        return stitches.ToArray();
    }

    private static (int dx, int dy, DstControl control)[] GenerateTatamiFill(int x, int y, int width, int height, int spacing)
    {
        var stitches = new List<(int, int, DstControl)>();
        int lastX = 0, lastY = 0;
        int rows = height / spacing;
        
        for (int row = 0; row <= rows; row++)
        {
            int yPos = y + row * spacing;
            int direction = row % 2 == 0 ? 1 : -1;
            int startX = x + (direction == 1 ? 0 : width);
            int endX = x + (direction == 1 ? width : 0);
            
            if (row == 0)
            {
                int dx = Math.Clamp(startX - lastX, -DstSpec.MaxDeltaPerRecord, DstSpec.MaxDeltaPerRecord);
                int dy = Math.Clamp(yPos - lastY, -DstSpec.MaxDeltaPerRecord, DstSpec.MaxDeltaPerRecord);
                stitches.Add((dx, dy, DstControl.Normal));
            }
            else
            {
                int dx = Math.Clamp(startX - lastX, -DstSpec.MaxDeltaPerRecord, DstSpec.MaxDeltaPerRecord);
                int dy = Math.Clamp(yPos - lastY, -DstSpec.MaxDeltaPerRecord, DstSpec.MaxDeltaPerRecord);
                stitches.Add((dx, dy, DstControl.Jump));
            }
            
            int dx2 = Math.Clamp(endX - startX, -DstSpec.MaxDeltaPerRecord, DstSpec.MaxDeltaPerRecord);
            stitches.Add((dx2, 0, DstControl.Normal));
            
            lastX = endX;
            lastY = yPos;
        }
        
        return stitches.ToArray();
    }
}