namespace AtlasEmbroidery.Tests.Formats.Pes;

using AtlasEmbroidery.Domain.Formats.Pes;
using AtlasEmbroidery.Domain.Formats;
using FluentAssertions;
using Xunit;
using System.Text;

/// <summary>
/// Malformed and truncated PES file tests
/// </summary>
public class PesMalformedTests
{
    private readonly PesFormatAdapter _adapter = new();

    [Fact]
    public async Task Read_InvalidSignature_Throws()
    {
        var data = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
        var stream = new MemoryStream(data);

        await Assert.ThrowsAsync<InvalidDataException>(() => _adapter.ReadAsync(stream));
    }

    [Fact]
    public async Task Read_TooShort_Throws()
    {
        var data = Encoding.ASCII.GetBytes("#PES0060");
        var stream = new MemoryStream(data);

        await Assert.ThrowsAsync<EndOfStreamException>(() => _adapter.ReadAsync(stream));
    }

    [Fact]
    public async Task Read_ValidSignatureMissingPecBlock_Throws()
    {
        // Valid PES v6 signature but no PEC block position / data
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("#PES0060"));
        writer.Write(0); // PEC block position (invalid)

        writer.Flush();
        stream.Position = 0;

        await Assert.ThrowsAsync<EndOfStreamException>(() => _adapter.ReadAsync(stream));
    }

    [Fact]
    public async Task Validate_InvalidSignature_ReturnsInvalid()
    {
        // 12+ bytes of invalid signature to pass length check but fail signature check
        var data = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B };
        var stream = new MemoryStream(data);

        var result = await _adapter.ValidateAsync(stream);

        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "PES.INVALID_SIGNATURE");
    }

    [Fact]
    public async Task Validate_TooShort_ReturnsInvalid()
    {
        var data = Encoding.ASCII.GetBytes("#PES0060");
        var stream = new MemoryStream(data);

        var result = await _adapter.ValidateAsync(stream);

        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "PES.TOO_SHORT");
    }

    [Fact]
    public async Task Read_TruncatedHeader_Throws()
    {
        // Valid signature but too short for full header
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("#PES0060"));
        writer.Write((int)100); // PEC block at position 100
        // Not enough data for header

        writer.Flush();
        stream.Position = 0;

        await Assert.ThrowsAsync<EndOfStreamException>(() => _adapter.ReadAsync(stream));
    }

    // ===== REGRESSION TESTS FOR END-MARKER FIX =====

    [Fact]
    public async Task Validate_EndMarkerAtEof_Valid()
    {
        // ... FF at EOF => valid termination
        var stream = CreateMinimalPesWithPecStitchData(new byte[] { 0x01, 0x00, 0x00, 0x00, 0xFF }); // END at end

        var result = await _adapter.ValidateAsync(stream);

        result.IsValid.Should().BeTrue();
        result.Issues.Should().Contain(i => i.RuleId == "PEC.END_MARKER_FOUND");
    }

    [Fact]
    public async Task Validate_StitchStreamTruncatedAtEof_Invalid()
    {
        // ... truncated normal stitch at EOF => MISSING_END_MARKER (PEC header valid, but no END)
        var stream = CreateMinimalPesWithPecStitchData(new byte[] { 0x01, 0x00 }); // valid stitch, no END

        var result = await _adapter.ValidateAsync(stream);

        result.IsValid.Should().BeFalse();
        // Valid PEC header, valid stitch data, but no END marker
        result.Issues.Should().Contain(i => i.RuleId == "PEC.MISSING_END_MARKER");
    }

    [Fact]
    public async Task Validate_ColorChangeAtEof_Invalid()
    {
        // FE B0 at EOF => critical truncated color change
        var stream = CreateMinimalPesWithPecStitchData(new byte[] { 0xFE, 0xB0 }); // missing color index

        var result = await _adapter.ValidateAsync(stream);

        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "PEC.COLOR_CHANGE_TRUNCATED");
    }

    [Fact]
    public async Task Validate_LongStitchTruncated_Invalid()
    {
        // long-stitch prefix without all required bytes => critical truncation
        // 0x80 = long form prefix, needs 3 more bytes (val2, val3, val4)
        var stream = CreateMinimalPesWithPecStitchData(new byte[] { 0x80, 0x00 }); // missing val3, val4

        var result = await _adapter.ValidateAsync(stream);

        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "PEC.LONG_STITCH_TRUNCATED");
    }

    [Fact]
    public async Task Validate_NoEndMarker_Invalid()
    {
        // no END anywhere => critical failure
        var stream = CreateMinimalPesWithPecStitchData(new byte[] { 0x01, 0x00, 0x02, 0x00, 0x03, 0x00 }); // no END

        var result = await _adapter.ValidateAsync(stream);

        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "PEC.MISSING_END_MARKER");
    }

    // ===== REGRESSION TESTS FOR OFFSET SAFETY =====

    [Fact]
    public async Task Validate_OffsetZero_Invalid()
    {
        // offset 0 - before PES prefix
        var stream = CreatePesWithPecOffset(0);

        var result = await _adapter.ValidateAsync(stream);

        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "PES.PEC_OFFSET_TOO_SMALL");
    }

    [Fact]
    public async Task Validate_OffsetEleven_Invalid()
    {
        // offset 11 - before end of PES prefix (minimum 12)
        var stream = CreatePesWithPecOffset(11);

        var result = await _adapter.ValidateAsync(stream);

        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "PES.PEC_OFFSET_TOO_SMALL");
    }

    [Fact]
    public async Task Validate_OffsetEqualsFileLength_Invalid()
    {
        // offset == file length - beyond file
        // Need a file where PEC offset equals the total file length
        // File: signature(8) + offset(4) = 12 bytes. PEC offset = 12.
        var newStream = new MemoryStream();
        var writer = new BinaryWriter(newStream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("#PES0060"));
        writer.Write(12); // PEC offset at file length (12)
        writer.Flush();
        newStream.Position = 0;

        var result = await _adapter.ValidateAsync(newStream);

        result.IsValid.Should().BeFalse();
        // Offset 12 is valid (>=12), but there's no data at offset 12, so PEC_OFFSET_BEYOND_FILE
        result.Issues.Should().Contain(i => i.RuleId == "PES.PEC_OFFSET_BEYOND_FILE");
    }

    [Fact]
    public async Task Validate_OffsetEqualsFileLengthMinusOne_Invalid()
    {
        // offset == file length - 1 - only 1 byte remaining, need 3 for 31 FF F0
        // File: signature(8) + offset(4) + 1 padding byte = 13 bytes. PEC offset = 12.
        // So offset 12 == fileLength 13 - 1, leaving exactly 1 byte at PEC offset.
        var newStream = new MemoryStream();
        var writer = new BinaryWriter(newStream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("#PES0060")); // 8 bytes
        writer.Write(12); // 4 bytes - PEC offset = 12
        writer.Write((byte)0x00); // 1 byte padding - file is now 13 bytes total
        writer.Flush();
        newStream.Position = 0;

        var result = await _adapter.ValidateAsync(newStream);

        result.IsValid.Should().BeFalse();
        // Offset 12 is valid (>=12) but only 1 byte remains (need 3 for 31 FF F0), so PEC_OFFSET_TRUNCATED
        result.Issues.Should().Contain(i => i.RuleId == "PES.PEC_OFFSET_TRUNCATED");
    }

    [Fact]
    public async Task Validate_OffsetTwelve_PecTruncated()
    {
        // offset 12 - minimum valid offset
        // But the PEC data at offset 12 is missing, so validation should fail on PEC level
        var stream = CreatePesWithPecOffset(12);

        var result = await _adapter.ValidateAsync(stream);

        // Offset 12 equals file length (12 bytes), so PEC_OFFSET_BEYOND_FILE
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "PES.PEC_OFFSET_BEYOND_FILE");
    }

    [Fact]
    public async Task Validate_MissingEndMarkerAfterPec_Invalid()
    {
        // ... <non-FF byte> at EOF => critical missing END marker
        // The PEC structure is valid but stitch stream ends without END
        var stream = CreateMinimalPesWithPecStitchData(new byte[] { 0x01, 0x00 }); // truncated normal stitch, no END

        var result = await _adapter.ValidateAsync(stream);

        result.IsValid.Should().BeFalse();
        // After valid PEC header and bounds, stitch stream has 2 bytes then EOF - MISSING_END_MARKER
        result.Issues.Should().Contain(i => i.RuleId == "PEC.MISSING_END_MARKER");
    }

    [Fact]
    public async Task Validate_NegativeOffset_Invalid()
    {
        // Negative offset (int32 min) - should fail offset too small
        var newStream = new MemoryStream();
        var writer = new BinaryWriter(newStream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("#PES0060"));
        writer.Write(int.MinValue); // negative offset
        writer.Flush();
        newStream.Position = 0;

        var result = await _adapter.ValidateAsync(newStream);

        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "PES.PEC_OFFSET_TOO_SMALL");
    }

    // Helper methods
    private MemoryStream CreateMinimalPesWithPecStitchData(byte[] stitchData)
    {
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // PES v6 signature
        writer.Write(Encoding.ASCII.GetBytes("#PES0060"));

        // PEC block position - will be at current position
        long pecBlockPos = writer.BaseStream.Position + 4; // skip placeholder
        writer.Write(0); // placeholder for PEC block position

        // Minimal PES v6 header (skip for simplicity - just write enough to reach PEC block)
        // We need to write enough header bytes so PEC block is at the right position
        // For simplicity, write minimal dummy header
        writer.BaseStream.Seek(pecBlockPos, SeekOrigin.Begin);

        // PEC block: 31 FF F0 + 4 int16 bounds (8 bytes) + stitch data
        writer.Write(new byte[] { 0x31, 0xFF, 0xF0 });
        writer.Write((short)100); // width
        writer.Write((short)100); // height
        writer.Write((short)100); // hoop width
        writer.Write((short)100); // hoop height
        writer.Write(stitchData);

        // Backfill PEC block position
        long endPos = writer.BaseStream.Position;
        writer.BaseStream.Seek(8, SeekOrigin.Begin); // after signature (8 bytes)
        writer.Write((int)pecBlockPos);

        stream.Position = 0;
        return stream;
    }

    private MemoryStream CreatePesWithPecOffset(int pecOffset)
    {
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("#PES0060"));
        writer.Write(pecOffset);

        // Add some padding so file is at least 12 bytes
        if (pecOffset > 12)
        {
            writer.Write(new byte[pecOffset - 12]);
        }

        writer.Flush();
        stream.Position = 0;
        return stream;
    }
}