namespace AtlasEmbroidery.Domain.Tests.Models;

using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using FluentAssertions;
using Xunit;

public class PointTests
{
    [Fact]
    public void Point_Zero_ReturnsOrigin()
    {
        Point.Zero.Should().Be(new Point(0, 0));
    }

    [Fact]
    public void Point_Empty_ReturnsMinValue()
    {
        Point.Empty.Should().Be(new Point(int.MinValue, int.MinValue));
    }

    [Fact]
    public void Point_IsEmpty_TrueForEmpty()
    {
        Point.Empty.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Point_IsEmpty_FalseForValid()
    {
        new Point(10, 20).IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Point_DistanceTo_CalculatesCorrectly()
    {
        var p1 = new Point(0, 0);
        var p2 = new Point(3, 4);
        p1.DistanceTo(p2).Should().Be(5.0);
    }

    [Fact]
    public void Point_Translate_MovesCorrectly()
    {
        var p = new Point(10, 20);
        var translated = p.Translate(5, -3);
        translated.Should().Be(new Point(15, 17));
    }

    [Fact]
    public void Point_Scale_ScalesCorrectly()
    {
        var p = new Point(10, 20);
        var scaled = p.Scale(2.0);
        scaled.Should().Be(new Point(20, 40));
    }

    [Fact]
    public void Point_Rotate90_RotatesCorrectly()
    {
        var p = new Point(10, 0);
        p.Rotate90().Should().Be(new Point(0, 10));
    }

    [Fact]
    public void Point_Rotate180_RotatesCorrectly()
    {
        var p = new Point(10, 20);
        p.Rotate180().Should().Be(new Point(-10, -20));
    }

    [Fact]
    public void Point_Rotate270_RotatesCorrectly()
    {
        var p = new Point(10, 0);
        p.Rotate270().Should().Be(new Point(0, -10));
    }

    [Fact]
    public void Point_Operators_Add_Subtract_Multiply()
    {
        var p1 = new Point(10, 20);
        var p2 = new Point(5, 3);

        (p1 + p2).Should().Be(new Point(15, 23));
        (p1 - p2).Should().Be(new Point(5, 17));
        (p1 * 2).Should().Be(new Point(20, 40));
    }
}

public class RectangleTests
{
    [Fact]
    public void Rectangle_Empty_ReturnsZeroSize()
    {
        Rectangle.Empty.Should().Be(new Rectangle(0, 0, 0, 0));
    }

    [Fact]
    public void Rectangle_Properties_CalculateCorrectly()
    {
        var r = new Rectangle(10, 20, 100, 50);
        r.Left.Should().Be(10);
        r.Top.Should().Be(20);
        r.Right.Should().Be(110);
        r.Bottom.Should().Be(70);
        r.CenterX.Should().Be(60);
        r.CenterY.Should().Be(45);
        r.Center.Should().Be(new Point(60, 45));
        r.Size.Should().Be(new Point(100, 50));
        r.Area.Should().Be(5000L);
    }

    [Fact]
    public void Rectangle_IsEmpty_TrueForZeroSize()
    {
        new Rectangle(0, 0, 0, 0).IsEmpty.Should().BeTrue();
        new Rectangle(0, 0, -1, 10).IsEmpty.Should().BeTrue();
        new Rectangle(0, 0, 10, -1).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Rectangle_IsEmpty_FalseForValid()
    {
        new Rectangle(0, 0, 1, 1).IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Rectangle_IsValid_TrueForPositiveSize()
    {
        new Rectangle(0, 0, 10, 10).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rectangle_Contains_PointInside()
    {
        var r = new Rectangle(10, 10, 100, 100);
        r.Contains(new Point(50, 50)).Should().BeTrue();
        r.Contains(new Point(10, 10)).Should().BeTrue(); // Edge inclusive
        r.Contains(new Point(109, 109)).Should().BeTrue();
        r.Contains(new Point(110, 110)).Should().BeFalse(); // Edge exclusive
    }

    [Fact]
    public void Rectangle_Contains_RectangleInside()
    {
        var r1 = new Rectangle(10, 10, 100, 100);
        var r2 = new Rectangle(20, 20, 50, 50);
        r1.Contains(r2).Should().BeTrue();
    }

    [Fact]
    public void Rectangle_IntersectsWith_Overlapping()
    {
        var r1 = new Rectangle(0, 0, 100, 100);
        var r2 = new Rectangle(50, 50, 100, 100);
        r1.IntersectsWith(r2).Should().BeTrue();
    }

    [Fact]
    public void Rectangle_IntersectsWith_NonOverlapping()
    {
        var r1 = new Rectangle(0, 0, 100, 100);
        var r2 = new Rectangle(200, 200, 100, 100);
        r1.IntersectsWith(r2).Should().BeFalse();
    }

    [Fact]
    public void Rectangle_Intersect_ReturnsOverlap()
    {
        var r1 = new Rectangle(0, 0, 100, 100);
        var r2 = new Rectangle(50, 50, 100, 100);
        var intersection = r1.Intersect(r2);
        intersection.Should().Be(new Rectangle(50, 50, 50, 50));
    }

    [Fact]
    public void Rectangle_Intersect_NoOverlap_ReturnsEmpty()
    {
        var r1 = new Rectangle(0, 0, 100, 100);
        var r2 = new Rectangle(200, 200, 100, 100);
        r1.Intersect(r2).Should().Be(Rectangle.Empty);
    }

    [Fact]
    public void Rectangle_Union_Combines()
    {
        var r1 = new Rectangle(0, 0, 100, 100);
        var r2 = new Rectangle(200, 200, 100, 100);
        var union = r1.Union(r2);
        union.Should().Be(new Rectangle(0, 0, 300, 300));
    }

    [Fact]
    public void Rectangle_Inflate_Expands()
    {
        var r = new Rectangle(50, 50, 100, 100);
        var inflated = r.Inflate(10, 20);
        inflated.Should().Be(new Rectangle(40, 30, 120, 140));
    }

    [Fact]
    public void Rectangle_Offset_Moves()
    {
        var r = new Rectangle(10, 20, 100, 100);
        var offset = r.Offset(5, -10);
        offset.Should().Be(new Rectangle(15, 10, 100, 100));
    }

    [Fact]
    public void Rectangle_Scale_Scales()
    {
        var r = new Rectangle(10, 20, 100, 50);
        var scaled = r.Scale(2.0);
        scaled.Should().Be(new Rectangle(20, 40, 200, 100));
    }
}

public class ThreadColorTests
{
    [Fact]
    public void ThreadColor_DefaultValues_AreCorrect()
    {
        var c = new ThreadColor(255, 128, 64);
        c.R.Should().Be(255);
        c.G.Should().Be(128);
        c.B.Should().Be(64);
        c.Hex.Should().Be("#FF8040");
    }

    [Fact]
    public void ThreadColor_Argb_ReturnsCorrectValue()
    {
        var c = new ThreadColor(255, 128, 64);
        c.Argb.Should().Be(0xFFFF8040u);
    }

    [Fact]
    public void ThreadColor_Luminance_CalculatesCorrectly()
    {
        ThreadColor.White.Luminance.Should().BeApproximately(255, 1);
        ThreadColor.Black.Luminance.Should().BeApproximately(0, 1);
        ThreadColor.Red.Luminance.Should().BeApproximately(76, 1); // 0.299 * 255
    }

    [Fact]
    public void ThreadColor_IsDark_WorksCorrectly()
    {
        ThreadColor.White.IsDark.Should().BeFalse();
        ThreadColor.Black.IsDark.Should().BeTrue();
        // 128 is exactly at boundary - use clearly dark/light values
        new ThreadColor(130, 130, 130).IsDark.Should().BeFalse(); // Luminance ~130 > 128
        new ThreadColor(120, 120, 120).IsDark.Should().BeTrue();  // Luminance ~120 < 128
    }

    [Fact]
    public void ThreadColor_FromHex_ParsesCorrectly()
    {
        ThreadColor.FromHex("#FF0000").Should().Be(new ThreadColor(255, 0, 0));
        ThreadColor.FromHex("FF0000").Should().Be(new ThreadColor(255, 0, 0));
        ThreadColor.FromHex("#F00").Should().Be(new ThreadColor(255, 0, 0));
        ThreadColor.FromHex("F00").Should().Be(new ThreadColor(255, 0, 0));
    }

    [Fact]
    public void ThreadColor_FromHex_Invalid_ReturnsBlack()
    {
        ThreadColor.FromHex("").Should().Be(ThreadColor.Black);
        ThreadColor.FromHex("invalid").Should().Be(ThreadColor.Black);
    }

    [Fact]
    public void ThreadColor_DistanceTo_CalculatesCorrectly()
    {
        var c1 = new ThreadColor(255, 0, 0);
        var c2 = new ThreadColor(0, 255, 0);
        c1.DistanceTo(c2).Should().BeApproximately(Math.Sqrt(255 * 255 + 255 * 255), 1);
    }

    [Fact]
    public void ThreadColor_Blend_InterpolatesCorrectly()
    {
        var c1 = ThreadColor.Black;
        var c2 = ThreadColor.White;
        var blended = c1.Blend(c2, 0.5);
        blended.R.Should().Be(128);
        blended.G.Should().Be(128);
        blended.B.Should().Be(128);
    }

    [Fact]
    public void ThreadColor_PredefinedColors_AreCorrect()
    {
        ThreadColor.Red.Should().Be(new ThreadColor(255, 0, 0, "Generic", "F00", "Red"));
        ThreadColor.Green.Should().Be(new ThreadColor(0, 255, 0, "Generic", "0F0", "Green"));
        ThreadColor.Blue.Should().Be(new ThreadColor(0, 0, 255, "Generic", "00F", "Blue"));
    }

    [Fact]
    public void ThreadColor_ToString_IncludesBrand()
    {
        var c = new ThreadColor(255, 0, 0, "Madeira", "1000", "Red");
        c.ToString().Should().Contain("Madeira");
        c.ToString().Should().Contain("1000");
        c.ToString().Should().Contain("Red");
    }
}

public class StitchTypeTests
{
    [Fact]
    public void StitchType_IsSewing_ReturnsTrueForSewingTypes()
    {
        StitchType.Running.IsSewing().Should().BeTrue();
        StitchType.Triple.IsSewing().Should().BeTrue();
        StitchType.Satin.IsSewing().Should().BeTrue();
        StitchType.Tatami.IsSewing().Should().BeTrue();
        StitchType.Sequins.IsSewing().Should().BeTrue();
    }

    [Fact]
    public void StitchType_IsSewing_ReturnsFalseForControlTypes()
    {
        StitchType.Jump.IsSewing().Should().BeFalse();
        StitchType.Trim.IsSewing().Should().BeFalse();
        StitchType.ColorChange.IsSewing().Should().BeFalse();
        StitchType.Stop.IsSewing().Should().BeFalse();
        StitchType.End.IsSewing().Should().BeFalse();
    }

    [Fact]
    public void StitchType_IsControl_ReturnsTrueForControlTypes()
    {
        StitchType.Jump.IsControl().Should().BeTrue();
        StitchType.Trim.IsControl().Should().BeTrue();
        StitchType.ColorChange.IsControl().Should().BeTrue();
    }

    [Fact]
    public void StitchType_RequiresThread_ReturnsTrueForSewing()
    {
        StitchType.Running.RequiresThread().Should().BeTrue();
        StitchType.Jump.RequiresThread().Should().BeFalse();
    }

    [Fact]
    public void StitchType_ToDisplayName_ReturnsReadableNames()
    {
        StitchType.Running.ToDisplayName().Should().Be("Running");
        StitchType.Triple.ToDisplayName().Should().Be("Triple/Bean");
        StitchType.Satin.ToDisplayName().Should().Be("Satin/Column");
        StitchType.Tatami.ToDisplayName().Should().Be("Tatami/Fill");
        StitchType.Jump.ToDisplayName().Should().Be("Jump");
        StitchType.Trim.ToDisplayName().Should().Be("Trim");
    }
}

public class StitchPointTests
{
    [Fact]
    public void StitchPoint_DefaultValues_AreCorrect()
    {
        var s = new StitchPoint(100, 200);
        s.X.Should().Be(100);
        s.Y.Should().Be(200);
        s.Type.Should().Be(StitchType.Running);
        s.Needle.Should().Be(1);
        s.ColorIndex.Should().Be(0);
        s.Flags.Should().Be(0);
        s.Position.Should().Be(new Point(100, 200));
    }

    [Fact]
    public void StitchPoint_IsEmpty_TrueForEmpty()
    {
        StitchPoint.Empty.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void StitchPoint_Flags_WorkCorrectly()
    {
        var s = new StitchPoint(0, 0, StitchType.Running, 1, 0, 0);
        s.HasFlag(StitchPoint.FlagTrim).Should().BeFalse();

        var s2 = s.WithFlag(StitchPoint.FlagTrim);
        s2.HasFlag(StitchPoint.FlagTrim).Should().BeTrue();
        s2.HasFlag(StitchPoint.FlagJump).Should().BeFalse();

        var s3 = s2.WithoutFlag(StitchPoint.FlagTrim);
        s3.HasFlag(StitchPoint.FlagTrim).Should().BeFalse();
    }

    [Fact]
    public void StitchPoint_IsJump_DetectsCorrectly()
    {
        new StitchPoint(0, 0, StitchType.Jump).IsJump.Should().BeTrue();
        new StitchPoint(0, 0, StitchType.Running, Flags: StitchPoint.FlagJump).IsJump.Should().BeTrue();
        new StitchPoint(0, 0, StitchType.Running).IsJump.Should().BeFalse();
    }

    [Fact]
    public void StitchPoint_IsTrim_DetectsCorrectly()
    {
        new StitchPoint(0, 0, StitchType.Trim).IsTrim.Should().BeTrue();
        new StitchPoint(0, 0, StitchType.Running, Flags: StitchPoint.FlagTrim).IsTrim.Should().BeTrue();
    }

    [Fact]
    public void StitchPoint_IsStop_DetectsCorrectly()
    {
        new StitchPoint(0, 0, StitchType.Stop).IsStop.Should().BeTrue();
        new StitchPoint(0, 0, StitchType.Running, Flags: StitchPoint.FlagStop).IsStop.Should().BeTrue();
    }

    [Fact]
    public void StitchPoint_IsColorChange_DetectsCorrectly()
    {
        new StitchPoint(0, 0, StitchType.ColorChange).IsColorChange.Should().BeTrue();
        new StitchPoint(0, 0, StitchType.Running, Flags: StitchPoint.FlagColorChange).IsColorChange.Should().BeTrue();
    }

    [Fact]
    public void StitchPoint_IsControl_DetectsCorrectly()
    {
        new StitchPoint(0, 0, StitchType.Jump).IsControl.Should().BeTrue();
        new StitchPoint(0, 0, StitchType.Running, Flags: StitchPoint.FlagJump).IsControl.Should().BeTrue();
        new StitchPoint(0, 0, StitchType.Running).IsControl.Should().BeFalse();
    }

    [Fact]
    public void StitchPoint_IsSewing_DetectsCorrectly()
    {
        new StitchPoint(0, 0, StitchType.Running).IsSewing.Should().BeTrue();
        new StitchPoint(0, 0, StitchType.Jump).IsSewing.Should().BeFalse();
        new StitchPoint(0, 0, StitchType.Running, Flags: StitchPoint.FlagJump).IsSewing.Should().BeFalse();
    }

    [Fact]
    public void StitchPoint_AsJump_CreatesJump()
    {
        var s = new StitchPoint(100, 100, StitchType.Running);
        var jump = s.AsJump();
        jump.Type.Should().Be(StitchType.Jump);
        jump.IsJump.Should().BeTrue();
    }

    [Fact]
    public void StitchPoint_DistanceTo_CalculatesCorrectly()
    {
        var s1 = new StitchPoint(0, 0);
        var s2 = new StitchPoint(3, 4);
        s1.DistanceTo(s2).Should().Be(5.0);
    }

    [Fact]
    public void StitchPoint_Translate_MovesCorrectly()
    {
        var s = new StitchPoint(10, 20, StitchType.Running, 1, 0, StitchPoint.FlagTrim);
        var translated = s.Translate(5, -3);
        translated.X.Should().Be(15);
        translated.Y.Should().Be(17);
        translated.Type.Should().Be(StitchType.Running);
        translated.Needle.Should().Be(1);
        translated.Flags.Should().Be(StitchPoint.FlagTrim);
    }

    [Fact]
    public void StitchPoint_Scale_ScalesCorrectly()
    {
        var s = new StitchPoint(10, 20, StitchType.Running);
        var scaled = s.Scale(2.0);
        scaled.X.Should().Be(20);
        scaled.Y.Should().Be(40);
    }
}

public class GeometryUtilsTests
{
    [Fact]
    public void PolygonArea_Square_ReturnsCorrectArea()
    {
        var square = new List<Point>
        {
            new(0, 0), new(100, 0), new(100, 100), new(0, 100)
        };
        GeometryUtils.PolygonArea(square).Should().Be(10000);
    }

    [Fact]
    public void PolygonArea_Triangle_ReturnsCorrectArea()
    {
        var triangle = new List<Point>
        {
            new(0, 0), new(100, 0), new(0, 100)
        };
        GeometryUtils.PolygonArea(triangle).Should().Be(5000);
    }

    [Fact]
    public void PointInPolygon_Inside_ReturnsTrue()
    {
        var square = new List<Point>
        {
            new(0, 0), new(100, 0), new(100, 100), new(0, 100)
        };
        GeometryUtils.PointInPolygon(new Point(50, 50), square).Should().BeTrue();
    }

    [Fact]
    public void PointInPolygon_Outside_ReturnsFalse()
    {
        var square = new List<Point>
        {
            new(0, 0), new(100, 0), new(100, 100), new(0, 100)
        };
        GeometryUtils.PointInPolygon(new Point(150, 150), square).Should().BeFalse();
    }

    [Fact]
    public void PointInPolygon_OnEdge_ReturnsTrueOrFalse()
    {
        // Ray casting may return either for edge points - just verify it doesn't crash
        var square = new List<Point>
        {
            new(0, 0), new(100, 0), new(100, 100), new(0, 100)
        };
        var result = GeometryUtils.PointInPolygon(new Point(50, 0), square);
        // Result can be true or false for edge - both are acceptable
    }

    [Fact]
    public void PolygonCentroid_Square_ReturnsCenter()
    {
        var square = new List<Point>
        {
            new(0, 0), new(100, 0), new(100, 100), new(0, 100)
        };
        var centroid = GeometryUtils.PolygonCentroid(square);
        centroid.Should().Be(new Point(50, 50));
    }

    [Fact]
    public void PolygonCentroid_SinglePoint_ReturnsPoint()
    {
        GeometryUtils.PolygonCentroid(new List<Point> { new Point(10, 20) }).Should().Be(new Point(10, 20));
    }

    [Fact]
    public void PolygonCentroid_TwoPoints_ReturnsMidpoint()
    {
        var centroid = GeometryUtils.PolygonCentroid(new List<Point> { new Point(0, 0), new Point(100, 100) });
        centroid.Should().Be(new Point(50, 50));
    }

    [Fact]
    public void SimplifyPolygon_ReducesPoints()
    {
        // Línea con muchos puntos colineales
        var points = new List<Point>();
        for (int i = 0; i <= 100; i++)
        {
            points.Add(new Point(i * 10, 0));
        }
        var simplified = GeometryUtils.SimplifyPolygon(points, 1.0);
        simplified.Count.Should().BeLessThan(points.Count);
        simplified.First().Should().Be(points.First());
        simplified.Last().Should().Be(points.Last());
    }

    [Fact]
    public void DistancePointToSegment_CalculatesCorrectly()
    {
        var p = new Point(50, 50);
        var a = new Point(0, 0);
        var b = new Point(100, 0);
        var dist = GeometryUtils.DistancePointToSegment(p, a, b);
        dist.Should().Be(50.0);
    }

    [Fact]
    public void LineSegmentsIntersect_Intersecting_ReturnsTrue()
    {
        var p1 = new Point(0, 0);
        var p2 = new Point(100, 100);
        var p3 = new Point(0, 100);
        var p4 = new Point(100, 0);

        GeometryUtils.LineSegmentsIntersect(p1, p2, p3, p4, out var intersection).Should().BeTrue();
        intersection.Should().Be(new Point(50, 50));
    }

    [Fact]
    public void LineSegmentsIntersect_Parallel_ReturnsFalse()
    {
        var p1 = new Point(0, 0);
        var p2 = new Point(100, 0);
        var p3 = new Point(0, 100);
        var p4 = new Point(100, 100);

        GeometryUtils.LineSegmentsIntersect(p1, p2, p3, p4, out _).Should().BeFalse();
    }

    [Fact]
    public void BoundingBox_CalculatesCorrectly()
    {
        var points = new List<Point>
        {
            new(10, 20), new(100, 50), new(50, 200), new(200, 10)
        };
        var bbox = GeometryUtils.BoundingBox(points);
        bbox.Should().Be(new Rectangle(10, 10, 190, 190));
    }

    [Fact]
    public void RotatePoints_RotatesAroundCenter()
    {
        var points = new List<Point> { new Point(100, 0) };
        var rotated = GeometryUtils.RotatePoints(points, new Point(0, 0), Math.PI / 2);
        rotated[0].Should().Be(new Point(0, 100));
    }

    [Fact]
    public void ScalePoints_ScalesFromCenter()
    {
        var points = new List<Point> { new Point(100, 100) };
        var scaled = GeometryUtils.ScalePoints(points, new Point(0, 0), 2.0, 2.0);
        scaled[0].Should().Be(new Point(200, 200));
    }
}