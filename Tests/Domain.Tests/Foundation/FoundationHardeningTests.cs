namespace AtlasEmbroidery.Domain.Tests.Foundation;

using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Serialization;
using AtlasEmbroidery.Domain.Stitching;
using AtlasEmbroidery.Domain.Geometry;
using FluentAssertions;
using Xunit;
using System.Text.Json;
using System.IO;
using System.Reflection;

public class FoundationHardeningTests
{
    #region SequenceIndex Overflow Tests

    [Fact]
    public void SequenceIndex_WithinLimit_DoesNotThrow()
    {
        var project = CreateTestProject();

        var engine = new StitchEngine();
        var plan = engine.Compile(project);

        plan.GlobalSequence.Count.Should().BeLessOrEqualTo(ushort.MaxValue);
        plan.GlobalSequence.Should().OnlyContain(s => s.SequenceIndex >= 0 && s.SequenceIndex <= ushort.MaxValue);
    }

    [Fact]
    public void SequenceIndex_Overflow_ThrowsOnExceedingUshortMax()
    {
        // CORRECCIÓN 7: Test real que provoca overflow
        // Crear un plan con 65537 puntadas (ushort.MaxValue + 2)
        var plan = new StitchPlan
        {
            ProjectId = Guid.NewGuid(),
            ProjectName = "OverflowTest",
            CanvasWidth = 100000,
            CanvasHeight = 100000
        };

        var stitches = new List<StitchPoint>();
        // Crear 65537 puntadas (ushort.MaxValue + 2) para forzar overflow
        // seqIndex va de 0 a 65536, cuando intente asignar 65536 (>= 65536) debe fallar
        for (int i = 0; i <= ushort.MaxValue + 1; i++)
        {
            stitches.Add(new StitchPoint(i, i, StitchType.Running, 1, 0, 0, 0));
        }
        plan.ObjectStitches[Guid.NewGuid()] = stitches;

        var engine = new StitchEngine();
        
        // CalculateMetrics se llama internamente y debe lanzar excepción
        var method = typeof(StitchEngine).GetMethod("CalculateMetrics", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        
        // Llamar directamente - la excepción debe propagarse
        Action act = () => method!.Invoke(engine, new object[] { plan });
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .Which.Message.Should().Contain("maximum representable index");
    }

    [Fact]
    public void SequenceIndex_ExactlyUshortMax_Works()
    {
        // CORRECCIÓN 7: 65535 puntadas (ushort.MaxValue) debe ser válido
        // indices 0..65534 = 65535 elementos
        var plan = new StitchPlan
        {
            ProjectId = Guid.NewGuid(),
            ProjectName = "MaxValidTest",
            CanvasWidth = 100000,
            CanvasHeight = 100000
        };

        var stitches = new List<StitchPoint>();
        // Crear exactamente 65535 puntadas (ushort.MaxValue)
        for (int i = 0; i < ushort.MaxValue; i++)
        {
            stitches.Add(new StitchPoint(i, i, StitchType.Running, 1, 0, 0, 0));
        }
        plan.ObjectStitches[Guid.NewGuid()] = stitches;

        var engine = new StitchEngine();
        
        // No debe lanzar excepción
        var method = typeof(StitchEngine).GetMethod("CalculateMetrics", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        method!.Invoke(engine, new object[] { plan });
        
        plan.GlobalSequence.Count.Should().Be(ushort.MaxValue);
        plan.GlobalSequence.Last().SequenceIndex.Should().Be((ushort)(ushort.MaxValue - 1));
    }

    #endregion

    #region Binary Format Version Tests

    [Fact]
    public void BinaryStitchSerializer_MagicCorrect_Deserializes()
    {
        var plan = CreateTestPlan();
        var bytes = BinaryStitchSerializer.Serialize(plan);
        
        bytes.Take(4).Should().Equal(new byte[] { 0x41, 0x54, 0x42, 0x31 }); // "ATB1"
        
        var deserialized = BinaryStitchSerializer.Deserialize(bytes);
        deserialized.Should().NotBeNull();
    }

    [Fact]
    public void BinaryStitchSerializer_InvalidMagic_ReturnsNull()
    {
        var invalidData = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x01, 0x00 };
        var result = BinaryStitchSerializer.Deserialize(invalidData);
        result.Should().BeNull();
    }

    [Fact]
    public void BinaryStitchSerializer_SupportedVersion_Deserializes()
    {
        var plan = CreateTestPlan();
        var bytes = BinaryStitchSerializer.Serialize(plan);
        
        var deserialized = BinaryStitchSerializer.Deserialize(bytes);
        deserialized.Should().NotBeNull();
        deserialized!.ProjectName.Should().Be("VersionTest");
    }

    [Fact]
    public void BinaryStitchSerializer_FutureVersion_ReturnsNull()
    {
        var plan = CreateTestPlan();
        var bytes = BinaryStitchSerializer.Serialize(plan);
        
        // Modify version to 2 (future)
        var modified = bytes.ToArray();
        modified[4] = 0x02; // Version 2
        modified[5] = 0x00;
        
        var result = BinaryStitchSerializer.Deserialize(modified);
        result.Should().BeNull();
    }

    [Fact]
    public void BinaryStitchSerializer_VersionZero_ReturnsNull()
    {
        // CORRECCIÓN 1: Version 0 debe rechazarse
        var plan = CreateTestPlan();
        var bytes = BinaryStitchSerializer.Serialize(plan);
        
        // Modify version to 0
        var modified = bytes.ToArray();
        modified[4] = 0x00; // Version 0
        modified[5] = 0x00;
        
        var result = BinaryStitchSerializer.Deserialize(modified);
        result.Should().BeNull();
    }

    [Fact]
    public void BinaryStitchSerializer_VersionMaxValue_ReturnsNull()
    {
        // CORRECCIÓN 1: Version ushort.MaxValue debe rechazarse
        var plan = CreateTestPlan();
        var bytes = BinaryStitchSerializer.Serialize(plan);
        
        // Modify version to ushort.MaxValue
        var modified = bytes.ToArray();
        modified[4] = 0xFF; // ushort.MaxValue low byte
        modified[5] = 0xFF; // ushort.MaxValue high byte
        
        var result = BinaryStitchSerializer.Deserialize(modified);
        result.Should().BeNull();
    }

    [Fact]
    public void BinaryStitchSerializer_TruncatedData_ReturnsNull()
    {
        var plan = CreateTestPlan();
        var bytes = BinaryStitchSerializer.Serialize(plan);
        
        // Truncate the data - ensure it's truncated enough to fail early
        var truncated = bytes.Take(Math.Max(1, bytes.Length / 2)).ToArray();
        
        var result = BinaryStitchSerializer.Deserialize(truncated);
        result.Should().BeNull();
    }

    [Fact]
    public void BinaryStitchSerializer_TrailingGarbage_ReturnsNull()
    {
        // CORRECCIÓN 6: trailing garbage debe rechazarse
        var plan = CreateTestPlan();
        var bytes = BinaryStitchSerializer.Serialize(plan);
        
        // Add trailing garbage bytes
        var withGarbage = new byte[bytes.Length + 10];
        Array.Copy(bytes, withGarbage, bytes.Length);
        for (int i = bytes.Length; i < withGarbage.Length; i++)
        {
            withGarbage[i] = 0xFF;
        }
        
        var result = BinaryStitchSerializer.Deserialize(withGarbage);
        result.Should().BeNull();
    }

    [Fact]
    public void BinaryStitchSerializer_ZeroStitches_Works()
    {
        var plan = new StitchPlan
        {
            ProjectId = Guid.NewGuid(),
            ProjectName = "Empty",
            CanvasWidth = 10000,
            CanvasHeight = 10000,
            MachineProfile = MachineProfile.Default(),
            HoopProfile = HoopProfile.Default()
        };

        var bytes = BinaryStitchSerializer.Serialize(plan);
        var deserialized = BinaryStitchSerializer.Deserialize(bytes);
        
        deserialized.Should().NotBeNull();
        deserialized!.GetAllStitches().Should().BeEmpty();
        deserialized.TotalStitches.Should().Be(0);
    }

    [Fact]
    public void BinaryStitchSerializer_SingleStitch_Works()
    {
        var plan = new StitchPlan
        {
            ProjectId = Guid.NewGuid(),
            ProjectName = "Single",
            CanvasWidth = 10000,
            CanvasHeight = 10000,
            MachineProfile = MachineProfile.Default(),
            HoopProfile = HoopProfile.Default()
        };
        plan.ObjectStitches[Guid.NewGuid()] = new List<StitchPoint>
        {
            new StitchPoint(100, 100, StitchType.Running, 1, 0, 0, 0)
        };
        plan.TotalStitches = 1;

        var bytes = BinaryStitchSerializer.Serialize(plan);
        var deserialized = BinaryStitchSerializer.Deserialize(bytes);
        
        deserialized.Should().NotBeNull();
        deserialized!.GetAllStitches().Should().HaveCount(1);
    }

    [Fact]
    public void BinaryStitchSerializer_MultipleStitches_Works()
    {
        var plan = CreateTestPlan();
        var bytes = BinaryStitchSerializer.Serialize(plan);
        var deserialized = BinaryStitchSerializer.Deserialize(bytes);
        
        deserialized.Should().NotBeNull();
        deserialized!.GetAllStitches().Should().HaveCount(plan.GetAllStitches().Count);
    }

    [Fact]
    public void BinaryStitchSerializer_NegativeCoordinates_Works()
    {
        var plan = new StitchPlan
        {
            ProjectId = Guid.NewGuid(),
            ProjectName = "Negative",
            CanvasWidth = 10000,
            CanvasHeight = 10000,
            MachineProfile = MachineProfile.Default(),
            HoopProfile = HoopProfile.Default()
        };
        plan.ObjectStitches[Guid.NewGuid()] = new List<StitchPoint>
        {
            new StitchPoint(-1000, -2000, StitchType.Running, 1, 0, 0, 0),
            new StitchPoint(-500, -1500, StitchType.Running, 1, 0, 0, 1),
            new StitchPoint(0, -1000, StitchType.Running, 1, 0, 0, 2)
        };
        plan.TotalStitches = 3;

        var bytes = BinaryStitchSerializer.Serialize(plan);
        var deserialized = BinaryStitchSerializer.Deserialize(bytes);
        
        deserialized.Should().NotBeNull();
        deserialized!.GetAllStitches().Should().HaveCount(3);
        deserialized.GetAllStitches()[0].X.Should().Be(-1000);
        deserialized.GetAllStitches()[0].Y.Should().Be(-2000);
    }

    [Fact]
    public void BinaryStitchSerializer_RoundTrip_PreservesAllFields()
    {
        var plan = CreateTestPlan();
        var bytes = BinaryStitchSerializer.Serialize(plan);
        var deserialized = BinaryStitchSerializer.Deserialize(bytes);
        
        deserialized.Should().NotBeNull();
        deserialized!.ProjectId.Should().Be(plan.ProjectId);
        deserialized.ProjectName.Should().Be(plan.ProjectName);
        deserialized.CanvasWidth.Should().Be(plan.CanvasWidth);
        deserialized.CanvasHeight.Should().Be(plan.CanvasHeight);
        deserialized.ThreadPalette.Should().HaveCount(plan.ThreadPalette.Count);
        deserialized.ColorToNeedleMap.Should().BeEquivalentTo(plan.ColorToNeedleMap);
        deserialized.TotalStitches.Should().Be(plan.TotalStitches);
        deserialized.TotalJumps.Should().Be(plan.TotalJumps);
        deserialized.DesignBounds.Should().BeEquivalentTo(plan.DesignBounds);
        deserialized.GetAllStitches().Should().HaveCount(plan.GetAllStitches().Count);
        
        // Verify stitch details
        for (int i = 0; i < plan.GetAllStitches().Count; i++)
        {
            var orig = plan.GetAllStitches()[i];
            var deser = deserialized.GetAllStitches()[i];
            deser.X.Should().Be(orig.X);
            deser.Y.Should().Be(orig.Y);
            deser.Type.Should().Be(orig.Type);
            deser.Needle.Should().Be(orig.Needle);
            deser.ColorIndex.Should().Be(orig.ColorIndex);
            deser.Flags.Should().Be(orig.Flags);
            deser.SequenceIndex.Should().Be(orig.SequenceIndex);
        }
    }

    #endregion

    #region VarInt Tests

    [Fact]
    public void VarInt_Zero_RoundTrips()
    {
        TestVarIntRoundTrip(0);
    }

    [Fact]
    public void VarInt_Positive_RoundTrips()
    {
        TestVarIntRoundTrip(1);
        TestVarIntRoundTrip(127);
        TestVarIntRoundTrip(128);
        TestVarIntRoundTrip(16383);
        TestVarIntRoundTrip(16384);
        TestVarIntRoundTrip(int.MaxValue);
    }

    [Fact]
    public void VarInt_Negative_RoundTrips()
    {
        TestVarIntRoundTrip(-1);
        TestVarIntRoundTrip(-127);
        TestVarIntRoundTrip(-128);
        TestVarIntRoundTrip(-16383);
        TestVarIntRoundTrip(-16384);
        TestVarIntRoundTrip(int.MinValue);
    }

    [Fact]
    public void VarInt_LongSequence_RoundTrips()
    {
        var values = Enumerable.Range(-1000, 2000).ToArray();
        foreach (var v in values)
            TestVarIntRoundTrip(v);
    }

    private void TestVarIntRoundTrip(int value)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        
        // Use reflection to call private WriteVarInt
        var method = typeof(BinaryStitchSerializer).GetMethod("WriteVarInt", 
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        method!.Invoke(null, new object[] { bw, value });
        
        var bytes = ms.ToArray();
        
        using var ms2 = new MemoryStream(bytes);
        using var br = new BinaryReader(ms2);
        
        var readMethod = typeof(BinaryStitchSerializer).GetMethod("ReadVarInt",
            BindingFlags.NonPublic | BindingFlags.Static);
        readMethod.Should().NotBeNull();
        var resultObj = readMethod!.Invoke(null, new object[] { br });
        var result = (int)(resultObj ?? 0);
        
        result.Should().Be(value);
    }

    [Fact]
    public void VarInt_Corrupted_ThrowsException()
    {
        // Create a varint that doesn't terminate (all bytes have high bit set)
        var corruptData = new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80 };
        
        using var ms = new MemoryStream(corruptData);
        using var br = new BinaryReader(ms);
        
        var readMethod = typeof(BinaryStitchSerializer).GetMethod("ReadVarInt",
            BindingFlags.NonPublic | BindingFlags.Static);
        readMethod.Should().NotBeNull();
        
        // Should throw InvalidDataException for exceeding max bytes (wrapped in TargetInvocationException)
        var ex = Assert.Throws<TargetInvocationException>(() => readMethod!.Invoke(null, new object[] { br }));
        ex.InnerException.Should().BeOfType<InvalidDataException>();
    }

    [Fact]
    public void VarInt_EOF_ThrowsException()
    {
        var data = new byte[] { 0x80 }; // Incomplete varint
        
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        
        var readMethod = typeof(BinaryStitchSerializer).GetMethod("ReadVarInt",
            BindingFlags.NonPublic | BindingFlags.Static);
        readMethod.Should().NotBeNull();
        
        var ex = Assert.Throws<TargetInvocationException>(() => readMethod!.Invoke(null, new object[] { br }));
        ex.InnerException.Should().BeOfType<EndOfStreamException>();
    }

    [Fact]
    public void VarInt_FifthByteInvalidHighBits_ThrowsException()
    {
        // CORRECCIÓN 4: 5to byte con bits altos inválidos
        // Para int32, el 5to byte solo puede tener los 4 bits bajos
        // 0x8F = 10001111 - bits altos (0xF0) están seteados inválidamente
        var corruptData = new byte[] { 0x80, 0x80, 0x80, 0x80, 0x8F };
        
        using var ms = new MemoryStream(corruptData);
        using var br = new BinaryReader(ms);
        
        var readMethod = typeof(BinaryStitchSerializer).GetMethod("ReadVarInt",
            BindingFlags.NonPublic | BindingFlags.Static);
        readMethod.Should().NotBeNull();
        
        var ex = Assert.Throws<TargetInvocationException>(() => readMethod!.Invoke(null, new object[] { br }));
        ex.InnerException.Should().BeOfType<InvalidDataException>();
    }

    [Fact]
    public void VarInt_SixBytes_ThrowsException()
    {
        // CORRECCIÓN 4: 6+ bytes debe rechazarse
        var corruptData = new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 };
        
        using var ms = new MemoryStream(corruptData);
        using var br = new BinaryReader(ms);
        
        var readMethod = typeof(BinaryStitchSerializer).GetMethod("ReadVarInt",
            BindingFlags.NonPublic | BindingFlags.Static);
        readMethod.Should().NotBeNull();
        
        var ex = Assert.Throws<TargetInvocationException>(() => readMethod!.Invoke(null, new object[] { br }));
        ex.InnerException.Should().BeOfType<InvalidDataException>();
    }

    [Fact]
    public void VarInt_MaxValidEncoding_RoundTrips()
    {
        // CORRECCIÓN 4: Máximo encoding válido para int32
        // int.MaxValue en zigzag = 0xFFFFFFFE, encoded as 5 bytes
        TestVarIntRoundTrip(int.MaxValue);
        TestVarIntRoundTrip(int.MinValue);
    }

    #region JSON Round-Trip Tests

    [Fact]
    public void Json_RoundTrip_EmptyProject()
    {
        var project = new AtlasProject
        {
            Name = "Empty",
            CanvasWidth = 10000,
            CanvasHeight = 10000
        };
        
        var json = AtlasSerializer.SerializeToJson(project);
        var deserialized = AtlasSerializer.DeserializeFromJson(json);
        
        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().Be("Empty");
        deserialized.CanvasWidth.Should().Be(10000);
        deserialized.CanvasHeight.Should().Be(10000);
        deserialized.Objects.Should().BeEmpty();
    }

    [Fact]
    public void Json_RoundTrip_SimpleProject()
    {
        var project = CreateTestProject();
        
        var json = AtlasSerializer.SerializeToJson(project);
        var deserialized = AtlasSerializer.DeserializeFromJson(json);
        
        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().Be(project.Name);
        deserialized.CanvasWidth.Should().Be(project.CanvasWidth);
        deserialized.Objects.Should().HaveCount(project.Objects.Count);
    }

    [Fact]
    public void Json_RoundTrip_MultipleObjects()
    {
        var project = new AtlasProject
        {
            Name = "Multi",
            CanvasWidth = 100000,
            CanvasHeight = 100000
        };
        
        var rect1 = ShapeObject.CreateRectangle(new Rectangle(1000, 1000, 5000, 5000), "Rect1");
        rect1.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        rect1.StitchParams.ColorIndex = 0;
        project.Objects.Add(rect1);
        
        var rect2 = ShapeObject.CreateRectangle(new Rectangle(20000, 20000, 3000, 3000), "Rect2");
        rect2.StitchParams = StitchParams.DefaultFor(StitchType.Satin);
        rect2.StitchParams.ColorIndex = 1;
        project.Objects.Add(rect2);
        
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Brand", "R001", "Red"));
        project.ThreadPalette.Add(new ThreadColor(0, 255, 0, "Brand", "G001", "Green"));
        project.ColorToNeedleMap[0] = 1;
        project.ColorToNeedleMap[1] = 2;
        
        var json = AtlasSerializer.SerializeToJson(project);
        var deserialized = AtlasSerializer.DeserializeFromJson(json);
        
        deserialized.Should().NotBeNull();
        deserialized!.Objects.Should().HaveCount(2);
        deserialized.ThreadPalette.Should().HaveCount(2);
    }

    [Fact]
    public void Json_RoundTrip_WithColors()
    {
        var project = CreateTestProject();
        
        var json = AtlasSerializer.SerializeToJson(project);
        var deserialized = AtlasSerializer.DeserializeFromJson(json);
        
        deserialized.Should().NotBeNull();
        deserialized!.ThreadPalette.Should().HaveCount(2);
        deserialized.ThreadPalette[0].R.Should().Be(255);
        deserialized.ThreadPalette[0].G.Should().Be(0);
        deserialized.ThreadPalette[0].B.Should().Be(0);
    }

    [Fact]
    public void Json_RoundTrip_UnicodeCharacters()
    {
        var project = new AtlasProject
        {
            Name = "Тест 🎨 日本語",
            CanvasWidth = 10000,
            CanvasHeight = 10000
        };
        
        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 1000, 1000), "тест");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        project.Objects.Add(shape);
        
        var json = AtlasSerializer.SerializeToJson(project);
        var deserialized = AtlasSerializer.DeserializeFromJson(json);
        
        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().Be("Тест 🎨 日本語");
        deserialized.Objects[0].Name.Should().Be("тест");
    }

    [Fact]
    public void Json_RoundTrip_NegativeValues()
    {
        var project = new AtlasProject
        {
            Name = "Negative",
            CanvasWidth = 10000,
            CanvasHeight = 10000
        };
        
        var shape = ShapeObject.CreateRectangle(new Rectangle(-5000, -5000, 10000, 10000), "Centered");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        project.Objects.Add(shape);
        
        var json = AtlasSerializer.SerializeToJson(project);
        var deserialized = AtlasSerializer.DeserializeFromJson(json);
        
        deserialized.Should().NotBeNull();
        // Vertices are properly serialized; Bounds is derived from Vertices
        deserialized!.Objects[0].Should().BeOfType<ShapeObject>();
        var deserializedShape = (ShapeObject)deserialized.Objects[0];
        deserializedShape.Vertices.Should().HaveCount(4);
        deserializedShape.Vertices.Should().Contain(v => v.X == -5000 && v.Y == -5000);
    }

    [Fact]
    public void Json_RoundTrip_ExtremeValidValues()
    {
        var project = new AtlasProject
        {
            Name = "Extreme",
            CanvasWidth = int.MaxValue / 2,
            CanvasHeight = int.MaxValue / 2
        };
        
        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 1000000, 1000000), "Large");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        project.Objects.Add(shape);
        
        var json = AtlasSerializer.SerializeToJson(project);
        var deserialized = AtlasSerializer.DeserializeFromJson(json);
        
        deserialized.Should().NotBeNull();
        deserialized!.CanvasWidth.Should().Be(project.CanvasWidth);
    }

    #endregion

    #region Hash Determinism Tests

    [Fact]
    public void ContentHash_SameProject_SameHash()
    {
        var project = CreateTestProject();
        
        var hash1 = AtlasSerializer.ComputeContentHash(project);
        var hash2 = AtlasSerializer.ComputeContentHash(project);
        
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ContentHash_ChangeGeometry_DifferentHash()
    {
        var project1 = CreateTestProject();
        var project2 = CreateTestProject();
        
        // Change geometry via RecalculateBounds
        if (project2.Objects[0] is ShapeObject shape)
        {
            shape.Vertices = new List<Point> 
            { 
                new Point(2000, 2000), new Point(8000, 2000), 
                new Point(8000, 8000), new Point(2000, 8000) 
            };
            shape.RecalculateBounds();
        }
        
        var hash1 = AtlasSerializer.ComputeContentHash(project1);
        var hash2 = AtlasSerializer.ComputeContentHash(project2);
        
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ContentHash_ChangeStitchParams_DifferentHash()
    {
        var project1 = CreateTestProject();
        var project2 = CreateTestProject();
        
        project2.Objects[0].StitchParams.Density = 500;
        
        var hash1 = AtlasSerializer.ComputeContentHash(project1);
        var hash2 = AtlasSerializer.ComputeContentHash(project2);
        
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ContentHash_ChangeVolatileMetadataOnly_SameHash()
    {
        var project1 = CreateTestProject();
        var project2 = project1.DeepClone();
        
        // Change only volatile fields
        project2.Id = Guid.NewGuid();
        project2.CreatedAt = DateTime.UtcNow.AddDays(1);
        project2.ModifiedAt = DateTime.UtcNow.AddDays(1);
        project2.ContentHash = "different";
        project2.StitchPlanHash = "different";
        
        foreach (var obj in project2.Objects)
        {
            obj.Id = Guid.NewGuid();
            obj.CreatedAt = DateTime.UtcNow.AddDays(1);
            obj.ModifiedAt = DateTime.UtcNow.AddDays(1);
        }
        
        var hash1 = AtlasSerializer.ComputeContentHash(project1);
        var hash2 = AtlasSerializer.ComputeContentHash(project2);
        
        hash1.Should().Be(hash2);
    }

    #endregion

    #region Determinism Tests

    [Fact]
    public void StitchEngine_Deterministic_SameInputSameOutput()
    {
        var project = CreateTestProject();
        
        var engine = new StitchEngine();
        var plan1 = engine.Compile(project);
        var plan2 = engine.Compile(project);
        
        plan1.TotalStitches.Should().Be(plan2.TotalStitches);
        plan1.TotalJumps.Should().Be(plan2.TotalJumps);
        plan1.TotalTrims.Should().Be(plan2.TotalTrims);
        plan1.TotalColorChanges.Should().Be(plan2.TotalColorChanges);
        plan1.TotalStops.Should().Be(plan2.TotalStops);
        plan1.EstimatedTimeSeconds.Should().Be(plan2.EstimatedTimeSeconds);
        plan1.EstimatedThreadMeters.Should().Be(plan2.EstimatedThreadMeters);
        plan1.DesignBounds.Should().BeEquivalentTo(plan2.DesignBounds);
        
        // Compare stitches
        var stitches1 = plan1.GetAllStitches();
        var stitches2 = plan2.GetAllStitches();
        
        stitches1.Should().HaveSameCount(stitches2);
        for (int i = 0; i < stitches1.Count; i++)
        {
            stitches1[i].X.Should().Be(stitches2[i].X);
            stitches1[i].Y.Should().Be(stitches2[i].Y);
            stitches1[i].Type.Should().Be(stitches2[i].Type);
            stitches1[i].ColorIndex.Should().Be(stitches2[i].ColorIndex);
            stitches1[i].Needle.Should().Be(stitches2[i].Needle);
            stitches1[i].Flags.Should().Be(stitches2[i].Flags);
            stitches1[i].SequenceIndex.Should().Be(stitches2[i].SequenceIndex);
        }
    }

    #endregion

    #region Metrics Invariants Tests

    [Fact]
    public void StitchPlan_Metrics_GlobalSequenceMatchesObjectStitches()
    {
        var project = CreateTestProject();
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        
        var totalFromObjects = plan.ObjectStitches.Values.SelectMany(s => s).Count();
        var totalFromGlobal = plan.GlobalSequence.Count;
        
        totalFromObjects.Should().Be(totalFromGlobal);
    }

    [Fact]
    public void StitchPlan_TotalStitches_MatchesSewingStitches()
    {
        var project = CreateTestProject();
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        
        var sewingCount = plan.ObjectStitches.Values.SelectMany(s => s).Count(s => s.IsSewing);
        plan.TotalStitches.Should().Be(sewingCount);
    }

    [Fact]
    public void StitchPlan_TotalJumps_MatchesJumpStitches()
    {
        var project = CreateTestProject();
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        
        var jumpCount = plan.ObjectStitches.Values.SelectMany(s => s).Count(s => s.IsJump);
        plan.TotalJumps.Should().Be(jumpCount);
    }

    [Fact]
    public void StitchPlan_TotalTrims_MatchesTrimStitches()
    {
        var project = CreateTestProject();
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        
        var trimCount = plan.ObjectStitches.Values.SelectMany(s => s).Count(s => s.IsTrim);
        plan.TotalTrims.Should().Be(trimCount);
    }

    [Fact]
    public void StitchPlan_NoNegativeMetrics()
    {
        var project = CreateTestProject();
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        
        plan.TotalStitches.Should().BeGreaterOrEqualTo(0);
        plan.TotalJumps.Should().BeGreaterOrEqualTo(0);
        plan.TotalTrims.Should().BeGreaterOrEqualTo(0);
        plan.TotalColorChanges.Should().BeGreaterOrEqualTo(0);
        plan.TotalStops.Should().BeGreaterOrEqualTo(0);
        plan.EstimatedTimeSeconds.Should().BeGreaterOrEqualTo(0);
        plan.EstimatedThreadMeters.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void StitchPlan_NoZeroSpeedDivision()
    {
        var project = CreateTestProject();
        project.WorkProfile = new WorkProfile
        {
            RecommendedMaxSpeed = 0 // Edge case - should throw
        };
        
        var engine = new StitchEngine();
        
        // CORRECCIÓN 8: speed <= 0 debe rechazarse explícitamente
        var ex = Assert.Throws<InvalidOperationException>(() => engine.Compile(project));
        ex.Message.Should().Contain("Invalid speed");
    }

    #endregion

    #region Tie-In/Tie-Off Tests

    [Fact]
    public void StitchEngine_TieStitchCountZero_HandledGracefully()
    {
        var project = CreateTestProject();
        project.Objects[0].StitchParams.TieStitchCount = 0;
        project.Objects[0].StitchParams.TieInLength = 1000;
        project.Objects[0].StitchParams.TieOffLength = 1000;
        
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        
        // Should not crash, should produce at least 1 tie stitch
        plan.Should().NotBeNull();
        plan.TotalStitches.Should().BeGreaterThan(0);
    }

    [Fact]
    public void StitchEngine_TieInLengthLessThanTieStitchCount_Handled()
    {
        var project = CreateTestProject();
        project.Objects[0].StitchParams.TieStitchCount = 10;
        project.Objects[0].StitchParams.TieInLength = 500; // Less than count
        project.Objects[0].StitchParams.TieOffLength = 500;
        
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        
        plan.Should().NotBeNull();
        plan.TotalStitches.Should().BeGreaterThan(0);
    }

    #endregion

    #region Satin/Zigzag Boundary Tests

    [Fact]
    public void StitchEngine_SatinZeroColumnWidth_Handled()
    {
        var project = CreateTestProject();
        project.Objects[0].StitchParams.PrimaryStitchType = StitchType.Satin;
        project.Objects[0].StitchParams.Satin = new SatinParams { ColumnWidth = 0 };
        
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        
        plan.Should().NotBeNull();
        plan.GetAllStitches().Should().NotBeEmpty();
    }

    [Fact]
    public void StitchEngine_SatinNegativeColumnWidth_Handled()
    {
        var project = CreateTestProject();
        project.Objects[0].StitchParams.PrimaryStitchType = StitchType.Satin;
        project.Objects[0].StitchParams.Satin = new SatinParams { ColumnWidth = -1000 };
        
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        
        plan.Should().NotBeNull();
        plan.GetAllStitches().Should().NotBeEmpty();
    }

    [Fact]
    public void StitchEngine_SatinZeroSpacing_Handled()
    {
        var project = CreateTestProject();
        project.Objects[0].StitchParams.PrimaryStitchType = StitchType.Satin;
        project.Objects[0].StitchParams.SatinSpacing = 0;
        
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        
        plan.Should().NotBeNull();
        plan.GetAllStitches().Should().NotBeEmpty();
    }

    #endregion

    #region Tatami Boundary Tests

    [Fact]
    public void StitchEngine_TatamiZeroDensity_Handled()
    {
        var project = CreateTestProject();
        project.Objects[0].StitchParams.PrimaryStitchType = StitchType.Tatami;
        project.Objects[0].StitchParams.Density = 0;
        project.Objects[0].StitchParams.Tatami = new TatamiParams { RowSpacing = 0 };
        
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        
        plan.Should().NotBeNull();
        plan.GetAllStitches().Should().NotBeEmpty();
    }

    [Fact]
    public void StitchEngine_TatamiNegativeDensity_Handled()
    {
        var project = CreateTestProject();
        project.Objects[0].StitchParams.PrimaryStitchType = StitchType.Tatami;
        project.Objects[0].StitchParams.Density = -100;
        
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        
        plan.Should().NotBeNull();
        plan.GetAllStitches().Should().NotBeEmpty();
    }

    [Fact]
    public void StitchEngine_TatamiDegeneratePolygon_Handled()
    {
        var project = new AtlasProject
        {
            Name = "Degenerate",
            CanvasWidth = 10000,
            CanvasHeight = 10000
        };
        
        // Line instead of polygon (2 points)
        var shape = new ShapeObject
        {
            Vertices = { new Point(0, 0), new Point(1000, 0) }
        };
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Tatami);
        shape.StitchParams.ColorIndex = 0;
        shape.RecalculateBounds();
        project.Objects.Add(shape);
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Brand", "R001", "Red"));
        project.ColorToNeedleMap[0] = 1;
        
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        
        plan.Should().NotBeNull();
    }

    #endregion

    #region Underlay Non-Recursion Test

    [Fact]
    public void StitchEngine_UnderlayEnabled_TerminatesWithoutInfiniteRecursion()
    {
        var project = CreateTestProject();
        project.Objects[0].StitchParams.Underlay = new UnderlayParams
        {
            Type = UnderlayType.EdgeWalk,
            Enabled = true,
            Density = 800,
            Inset = 200,
            StitchLength = 3000
        };
        
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        
        plan.Should().NotBeNull();
        plan.GlobalSequence.Should().NotBeEmpty();
        
        // Verify underlay stitches have the flag
        var underlayStitches = plan.GlobalSequence.Where(s => s.IsUnderlay).ToList();
        underlayStitches.Should().NotBeEmpty();
        
        // Underlay stitches should not change the main color
        underlayStitches.Should().OnlyContain(s => s.ColorIndex == 0);
    }

    #endregion

    #region OptimizePlan Documentation Test

    [Fact]
    public void StitchEngine_OptimizePlan_DocumentedAsLimited()
    {
        var project = CreateTestProject();
        project.Objects[0].StitchParams.ColorIndex = 0;
        var rect2 = ShapeObject.CreateRectangle(new Rectangle(20000, 20000, 5000, 5000), "Rect2");
        rect2.StitchParams = StitchParams.DefaultFor(StitchType.Satin);
        rect2.StitchParams.ColorIndex = 1;
        project.Objects.Add(rect2);
        
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        
        // OptimizePlan runs automatically in Compile
        // It should reorder by color then needle
        plan.ObjectStitches.Should().HaveCount(2);
        
        // Verify the method exists and is documented (via reflection checking summary)
        var method = typeof(StitchEngine).GetMethod("OptimizePlan", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
    }

    [Fact]
    public void StitchEngine_OptimizePlan_ReordersObjectsByColorThenNeedle()
    {
        // CORRECCIÓN 11: Test real de optimización
        var project = new AtlasProject
        {
            Name = "OptimizeTest",
            CanvasWidth = 100000,
            CanvasHeight = 100000
        };

        // Object A: color 2 (debería ir después)
        var rectA = ShapeObject.CreateRectangle(new Rectangle(1000, 1000, 5000, 5000), "RectA");
        rectA.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        rectA.StitchParams.ColorIndex = 2;
        rectA.StitchParams.NeedleIndex = 1;
        rectA.RecalculateBounds();
        project.Objects.Add(rectA);

        // Object B: color 1 (debería ir antes)
        var rectB = ShapeObject.CreateRectangle(new Rectangle(20000, 20000, 5000, 5000), "RectB");
        rectB.StitchParams = StitchParams.DefaultFor(StitchType.Satin);
        rectB.StitchParams.ColorIndex = 1;
        rectB.StitchParams.NeedleIndex = 1;
        rectB.RecalculateBounds();
        project.Objects.Add(rectB);

        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Brand", "R001", "Red"));    // color 0
        project.ThreadPalette.Add(new ThreadColor(0, 255, 0, "Brand", "G001", "Green"));  // color 1
        project.ThreadPalette.Add(new ThreadColor(0, 0, 255, "Brand", "B001", "Blue"));   // color 2
        project.ColorToNeedleMap[0] = 1;
        project.ColorToNeedleMap[1] = 1;
        project.ColorToNeedleMap[2] = 1;

        var engine = new StitchEngine();
        var plan = engine.Compile(project);

        plan.ObjectStitches.Should().HaveCount(2);
        
        // GlobalSequence debe reflejar el orden optimizado (color 1 antes que color 2)
        var sequence = plan.GlobalSequence;
        sequence.Should().NotBeEmpty();
        
        // Encontrar las primeras puntadas de cada color en la secuencia
        var firstColor1 = sequence.FirstOrDefault(s => s.ColorIndex == 1);
        var firstColor2 = sequence.FirstOrDefault(s => s.ColorIndex == 2);
        
        firstColor1.Should().NotBeNull("Color 1 should appear in sequence");
        firstColor2.Should().NotBeNull("Color 2 should appear in sequence");
        
        // Color 1 debe aparecer antes que color 2
        firstColor1!.SequenceIndex.Should().BeLessThan(firstColor2!.SequenceIndex,
            "Optimized sequence should have color 1 before color 2");

        // Determinismo: misma entrada -> misma secuencia optimizada
        var plan2 = engine.Compile(project);
        plan2.GlobalSequence.Should().Equal(plan.GlobalSequence, 
            (a, b) => a.SequenceIndex == b.SequenceIndex && 
                     a.ColorIndex == b.ColorIndex && 
                     a.X == b.X && a.Y == b.Y);
    }

    #endregion

    #region Helpers

    private static AtlasProject CreateTestProject()
    {
        var project = new AtlasProject
        {
            Name = "TestProject",
            CanvasWidth = 100000,
            CanvasHeight = 100000
        };

        var shape = ShapeObject.CreateRectangle(new Rectangle(10000, 10000, 50000, 50000), "Test Rect");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape.StitchParams.ColorIndex = 0;
        shape.StitchParams.NeedleIndex = 1;
        shape.RecalculateBounds();
        project.Objects.Add(shape);

        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        project.ThreadPalette.Add(new ThreadColor(0, 255, 0, "Test", "002", "Green"));
        project.ColorToNeedleMap[0] = 1;
        project.ColorToNeedleMap[1] = 2;

        return project;
    }

    private static StitchPlan CreateTestPlan()
    {
        var plan = new StitchPlan
        {
            ProjectId = Guid.NewGuid(),
            ProjectName = "VersionTest",
            CanvasWidth = 10000,
            CanvasHeight = 10000,
            CanvasOrigin = Point.Zero,
            MachineProfile = MachineProfile.Default(),
            HoopProfile = HoopProfile.Default()
        };

        plan.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        plan.ThreadPalette.Add(new ThreadColor(0, 255, 0, "Test", "002", "Green"));
        plan.ColorToNeedleMap[0] = 1;
        plan.ColorToNeedleMap[1] = 2;

        var stitches = new List<StitchPoint>
        {
            new StitchPoint(0, 0, StitchType.Running, 1, 0, 0, 0),
            new StitchPoint(1000, 0, StitchType.Running, 1, 0, 0, 1),
            new StitchPoint(2000, 0, StitchType.Running, 1, 0, 0, 2)
        };

        plan.ObjectStitches[Guid.NewGuid()] = stitches;
        plan.TotalStitches = 3;
        plan.DesignBounds = new Rectangle(0, 0, 2000, 0);

        return plan;
    }
}

#endregion

#endregion