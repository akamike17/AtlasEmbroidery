namespace AtlasEmbroidery.Domain.Formats.Pec;

using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using FmtValidationIssue = AtlasEmbroidery.Domain.Formats.ValidationIssue;
using FmtValidationSeverity = AtlasEmbroidery.Domain.Formats.ValidationSeverity;
using System.Text;

/// <summary>
/// Brother PEC format constants and specifications
/// </summary>
public static class PecSpec
{
    public const string Signature = "#PEC0001";
    public const int HeaderSize = 520; // Fixed header size before stitch block
    public const int MaxDeltaPerRecord = 2047; // 12-bit signed (0x7FF)
    public const int MicronsPerPecUnit = 100; // 1 PEC unit = 0.1mm = 100 microns
    public const int MaxColors = 255;
    public const int IconWidth = 48;
    public const int IconHeight = 38;
    public const int IconByteStride = IconWidth / 8; // 6 bytes per row
    public const int GraphicsByteCount = IconByteStride * IconHeight; // 228 bytes

    // Control codes in high byte of 16-bit value
    public const byte FlagLong = 0x80;    // Bit 7: 12-bit encoding
    public const byte JumpCode = 0x10;    // Bit 4: Jump
    public const byte TrimCode = 0x20;    // Bit 5: Trim
    public const byte ColorChangeMarker1 = 0xFE;
    public const byte ColorChangeMarker2 = 0xB0;
    public const byte EndMarker = 0xFF;

    // Stitch block markers
    public static readonly byte[] BlockHeader = { 0x31, 0xFF, 0xF0 };
    public static readonly byte[] EmptyColorTable = { 0x20, 0x20, 0x20, 0x20, 0x64, 0x20, 0x00, 0x20, 0x00, 0x20, 0x20, 0x20, 0xFF };
}

/// <summary>
    /// PEC stitch encoder using Brother 12-bit/7-bit variable-length encoding
    /// </summary>
    public static class PecMovementEncoder
    {
        /// <summary>
        /// Encodes a delta into PEC format.
        /// Returns a variable-length byte array (2 bytes for short, 4 bytes for long).
        /// Short form: |dx| < 64 and |dy| < 64 and no flags
        /// Long form: 12-bit signed with FLAG_LONG bit set
        /// </summary>
        public static byte[] EncodeMovement(int deltaX, int deltaY, byte flags = 0)
        {
            // Special case: END marker (0xFF) - should use EncodeEnd()
            if (deltaX == 0 && deltaY == 0 && flags == 0xFF)
            {
                return EncodeEnd();
            }

            // Special case: Color change - should use EncodeColorChange()
            if (deltaX == 0 && deltaY == 0 && flags == 0xFE)
            {
                // This is not the right way - we need a separate method
            }

            if (deltaX < -2048 || deltaX > 2047)
                throw new ArgumentOutOfRangeException(nameof(deltaX), $"Delta X {deltaX} exceeds max ±2047");
            if (deltaY < -2048 || deltaY > 2047)
                throw new ArgumentOutOfRangeException(nameof(deltaY), $"Delta Y {deltaY} exceeds max ±2047");

            // PEC negates Y per spec
            deltaY = -deltaY;

            bool isLong = (flags & (FlagLong | JumpCode | TrimCode)) != 0 ||
                          deltaX <= -64 || deltaX >= 63 ||
                          deltaY <= -64 || deltaY >= 63;

            var result = new List<byte>();

            if (isLong)
                    {
                        // 12-bit encoding: 2 bytes per coordinate
                        // Flags (jump/trim) applied to BOTH coordinates per PEC spec
                        int xEncoded = Encode12Bit(deltaX, (byte)(flags & (JumpCode | TrimCode)));
                        int yEncoded = Encode12Bit(deltaY, (byte)(flags & (JumpCode | TrimCode)));
                        result.Add((byte)(xEncoded >> 8));
                        result.Add((byte)(xEncoded & 0xFF));
                        result.Add((byte)(yEncoded >> 8));
                        result.Add((byte)(yEncoded & 0xFF));
                    }
            else
            {
                // 7-bit encoding: 1 byte per coordinate
                result.Add((byte)(deltaX & 0x7F));
                result.Add((byte)(deltaY & 0x7F));
            }

            return result.ToArray();
        }

        private const int MaxDelta = 2047;
        private const byte FlagLong = 0x80;
        private const byte JumpCode = 0x10;
        private const byte TrimCode = 0x20;

        private static int Encode12Bit(int value, byte flags)
        {
            if (value < -2048 || value > 2047)
                throw new ArgumentOutOfRangeException(nameof(value), $"12-bit value {value} out of range [-2048, 2047]");

            int encoded = value & 0xFFF;
            encoded |= (FlagLong | flags) << 8;
            return encoded;
        }

        /// <summary>
        /// Encodes a color change marker (0xFE 0xB0 + color index byte)
        /// </summary>
        public static byte[] EncodeColorChange(byte colorIndex)
        {
            return new byte[] { PecSpec.ColorChangeMarker1, PecSpec.ColorChangeMarker2, colorIndex };
        }

        /// <summary>
        /// Encodes the END marker (0xFF)
        /// </summary>
        public static byte[] EncodeEnd()
        {
            return new byte[] { PecSpec.EndMarker };
        }
    }

/// <summary>
/// PEC stitch decoder
/// </summary>
public static class PecMovementDecoder
{
    public static (int deltaX, int deltaY, PecControl control, int bytesConsumed) DecodeMovement(ReadOnlySpan<byte> data, int offset = 0)
    {
        if (offset >= data.Length)
            throw new ArgumentOutOfRangeException(nameof(data), "Insufficient data for PEC record");

        byte val1 = data[offset];

        // Check for END marker (single byte 0xFF)
        if (val1 == PecSpec.EndMarker)
        {
            return (0, 0, PecControl.End, 1);
        }

        // Need at least 2 bytes for other records
        if (offset + 1 >= data.Length)
            throw new ArgumentOutOfRangeException(nameof(data), "Insufficient data for PEC record");

        byte val2 = data[offset + 1];

        // Check for color change marker (FE B0)
        if (val1 == PecSpec.ColorChangeMarker1 && val2 == PecSpec.ColorChangeMarker2)
        {
            if (offset + 2 >= data.Length)
                throw new ArgumentOutOfRangeException(nameof(data), "Incomplete color change record");
            return (0, 0, PecControl.ColorChange, 3);
        }

        bool xLong = (val1 & PecSpec.FlagLong) != 0;

        if (xLong)
        {
            if (offset + 3 >= data.Length)
                throw new ArgumentOutOfRangeException(nameof(data), "Insufficient data for long record");
            byte val3 = data[offset + 2];
            byte val4 = data[offset + 3];

            int xCode = (val1 << 8) | val2;
            int yCode = (val3 << 8) | val4;

            int deltaX = Decode12Bit(xCode);
            int deltaY = Decode12Bit(yCode);

            bool jump = (val1 & PecSpec.JumpCode) != 0 || (val3 & PecSpec.JumpCode) != 0;
            bool trim = (val1 & PecSpec.TrimCode) != 0 || (val3 & PecSpec.TrimCode) != 0;

            var control = jump ? PecControl.Jump : (trim ? PecControl.Trim : PecControl.Normal);
            return (deltaX, -deltaY, control, 4);
        }
        else
        {
            int deltaX = Decode7Bit(val1);
            int deltaY = Decode7Bit(val2);
            return (deltaX, -deltaY, PecControl.Normal, 2);
        }
    }

    private static int Decode7Bit(byte b) => b <= 63 ? b : b - 128;

    private static int Decode12Bit(int code)
    {
        int value = code & 0xFFF;
        return value > 0x7FF ? value - 0x1000 : value;
    }
}

[Flags]
public enum PecControl : byte
{
    Normal = 0,
    Jump = PecSpec.JumpCode,
    Trim = PecSpec.TrimCode,
    ColorChange = 0x80,
    End = 0xFF
}