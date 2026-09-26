namespace AtlasEmbroidery.Domain.Formats.Xxx;

/// <summary>
/// Singer XXX format constants and specifications
/// </summary>
public static class XxxSpec
{
    public const string Extension = ".xxx";
    public const int HeaderVariantASize = 0x100; // 256 bytes
    public const int HeaderVariantBSize = 0x100; // 256 bytes
    public const int MaxDeltaPerRecord = 124; // 8-bit signed (reserves 0xFD,0xFE,0xFF)
    public const int MaxDeltaLong = 32767; // 16-bit signed LE
    public const int MicronsPerXxxUnit = 100; // 0.1mm = 100 microns
    public const int MaxColors = 21; // Fixed 21 color entries in color table
    public const int ColorTableEntrySize = 4; // RGB + reserved (0x00)

    // Control prefix
    public const byte ControlPrefix = 0x7F;
    public const byte LongMovePrefix = 0x7D;

    // Control codes (second byte after 0x7F)
    public const byte CtrlJump = 0x01;
    public const byte CtrlTrim = 0x03;
    public const byte CtrlColorChange = 0x08;
    public const byte CtrlNeedleChangeStart = 0x0A; // 0x0A-0x17 (needle 1-14)
    public const byte CtrlNeedleChangeEnd = 0x17;
    public const byte CtrlEnd = 0x7F; // Followed by 0x02 0x14

    // End sequence
    public static readonly byte[] EndSequence = { 0x7F, 0x7F, 0x02, 0x14 };

    // Header offsets for variant B (writes "XXX" signature)
    public const int SignatureOffset = 0xA0; // "XXX" at offset 0xA0 in variant B
    public const int StitchCountOffset = 0x14; // Little-endian int32
    public const int ThreadCountOffset = 0x20; // Little-endian int32
    public const int BoundsOffset = 0x24; // 8 x int16: minX, maxX, minY, maxY, etc.
}

/// <summary>
/// XXX stitch encoder using signed 8-bit deltas with 0x7D long prefix and 0x7F control prefix
/// </summary>
public static class XxxMovementEncoder
{
    /// <summary>
    /// Encodes a normal stitch move (8-bit signed deltas)
    /// </summary>
    public static byte[] EncodeMovement(int deltaX, int deltaY)
    {
        if (deltaX < -128 || deltaX > 127)
            throw new ArgumentOutOfRangeException(nameof(deltaX), $"Delta X {deltaX} exceeds 8-bit signed range [-128, 127]");
        if (deltaY < -128 || deltaY > 127)
            throw new ArgumentOutOfRangeException(nameof(deltaY), $"Delta Y {deltaY} exceeds 8-bit signed range [-128, 127]");

        // XXX negates Y per spec
        byte x = (byte)(deltaX & 0xFF);
        byte y = (byte)((-deltaY) & 0xFF);
        return new byte[] { x, y };
    }

    /// <summary>
    /// Encodes a long move (16-bit LE signed deltas with 0x7D prefix)
    /// </summary>
    public static byte[] EncodeLongMove(int deltaX, int deltaY)
    {
        if (deltaX < -32768 || deltaX > 32767)
            throw new ArgumentOutOfRangeException(nameof(deltaX), $"Delta X {deltaX} exceeds 16-bit signed range");
        if (deltaY < -32768 || deltaY > 32767)
            throw new ArgumentOutOfRangeException(nameof(deltaY), $"Delta Y {deltaY} exceeds 16-bit signed range");

        // XXX negates Y per spec
        byte xLo = (byte)(deltaX & 0xFF);
        byte xHi = (byte)((deltaX >> 8) & 0xFF);
        byte yLo = (byte)((-deltaY) & 0xFF);
        byte yHi = (byte)(((-deltaY) >> 8) & 0xFF);

        return new byte[] { XxxSpec.LongMovePrefix, xLo, xHi, yLo, yHi };
    }

    /// <summary>
    /// Encodes a jump move
    /// </summary>
    public static byte[] EncodeJump(int deltaX, int deltaY)
    {
        var move = EncodeMovement(deltaX, deltaY);
        return new byte[] { XxxSpec.ControlPrefix, XxxSpec.CtrlJump, move[0], move[1] };
    }

    /// <summary>
    /// Encodes a trim
    /// </summary>
    public static byte[] EncodeTrim()
    {
        // XXX trim has no additional delta data per pyembroidery
        return new byte[] { XxxSpec.ControlPrefix, XxxSpec.CtrlTrim, 0x00, 0x00 };
    }

    /// <summary>
    /// Encodes a color change
    /// </summary>
    public static byte[] EncodeColorChange()
    {
        return new byte[] { XxxSpec.ControlPrefix, XxxSpec.CtrlColorChange, 0x00, 0x00 };
    }

    /// <summary>
    /// Encodes a stop (same as color change in XXX)
    /// </summary>
    public static byte[] EncodeStop()
    {
        return new byte[] { XxxSpec.ControlPrefix, XxxSpec.CtrlColorChange, 0x00, 0x00 };
    }

    /// <summary>
    /// Encodes a needle change (needle 1-14)
    /// </summary>
    public static byte[] EncodeNeedleChange(int needle)
    {
        if (needle < 1 || needle > 14)
            throw new ArgumentOutOfRangeException(nameof(needle), "Needle must be 1-14");
        byte ctrl = (byte)(XxxSpec.CtrlNeedleChangeStart + needle - 1);
        return new byte[] { XxxSpec.ControlPrefix, ctrl, 0x00, 0x00 };
    }

    /// <summary>
    /// Encodes the end marker
    /// </summary>
    public static byte[] EncodeEnd()
    {
        return XxxSpec.EndSequence;
    }
}

/// <summary>
/// XXX stitch decoder
/// </summary>
public static class XxxMovementDecoder
{
    /// <summary>
    /// Decodes the next record from XXX data
    /// Returns (deltaX, deltaY, control, bytesConsumed)
    /// </summary>
    public static (int deltaX, int deltaY, XxxControl control, int bytesConsumed) DecodeNext(ReadOnlySpan<byte> data, int offset = 0)
    {
        if (offset >= data.Length)
            throw new ArgumentOutOfRangeException(nameof(data), "Insufficient data for XXX record");

        byte b1 = data[offset];

        // Normal stitch (no control prefix)
        if (b1 != XxxSpec.ControlPrefix && b1 != XxxSpec.LongMovePrefix)
        {
            if (offset + 1 >= data.Length)
                throw new ArgumentOutOfRangeException(nameof(data), "Insufficient data for stitch record");

            byte b2 = data[offset + 1];
            int deltaX = DecodeSigned8(b1);
            int deltaY = -DecodeSigned8(b2); // XXX negates Y
            return (deltaX, deltaY, XxxControl.Normal, 2);
        }

        // Long move (0x7D prefix)
        if (b1 == XxxSpec.LongMovePrefix)
        {
            if (offset + 4 >= data.Length)
                throw new ArgumentOutOfRangeException(nameof(data), "Insufficient data for long move");

            // 0x7D [xLo][xHi][yLo][yHi] - 16-bit LE
            int deltaX = (int)(data[offset + 1] | (data[offset + 2] << 8));
            int deltaY = -((int)(data[offset + 3] | (data[offset + 4] << 8))); // negate Y
            return (deltaX, deltaY, XxxControl.Normal, 5);
        }

        // Control code (0x7F prefix)
        if (offset + 1 >= data.Length)
            throw new ArgumentOutOfRangeException(nameof(data), "Insufficient data for control code");

        byte ctrl = data[offset + 1];

        switch (ctrl)
        {
            case XxxSpec.CtrlJump:
                if (offset + 3 >= data.Length)
                    throw new ArgumentOutOfRangeException(nameof(data), "Insufficient data for jump");
                byte jx = data[offset + 2];
                byte jy = data[offset + 3];
                int jDeltaX = DecodeSigned8(jx);
                int jDeltaY = -DecodeSigned8(jy);
                return (jDeltaX, jDeltaY, XxxControl.Jump, 4);

            case XxxSpec.CtrlTrim:
                if (offset + 3 >= data.Length)
                    throw new ArgumentOutOfRangeException(nameof(data), "Insufficient data for trim");
                byte tx = data[offset + 2];
                byte ty = data[offset + 3];
                int tDeltaX = DecodeSigned8(tx);
                int tDeltaY = -DecodeSigned8(ty);
                return (tDeltaX, tDeltaY, XxxControl.Trim, 4);

            case XxxSpec.CtrlColorChange:
                if (offset + 3 >= data.Length)
                    throw new ArgumentOutOfRangeException(nameof(data), "Insufficient data for color change");
                byte ccX = data[offset + 2];
                byte ccY = data[offset + 3];
                int ccDeltaX = DecodeSigned8(ccX);
                int ccDeltaY = -DecodeSigned8(ccY);
                return (ccDeltaX, ccDeltaY, XxxControl.ColorChange, 4);

            case XxxSpec.CtrlEnd:
                // End marker: 0x7F 0x7F 0x02 0x14
                if (offset + 3 >= data.Length)
                    throw new ArgumentOutOfRangeException(nameof(data), "Insufficient data for end marker");
                return (0, 0, XxxControl.End, 4);

            default:
                if (ctrl >= XxxSpec.CtrlNeedleChangeStart && ctrl <= XxxSpec.CtrlNeedleChangeEnd)
                {
                    if (offset + 3 >= data.Length)
                        throw new ArgumentOutOfRangeException(nameof(data), "Insufficient data for needle change");
                    int needle = ctrl - XxxSpec.CtrlNeedleChangeStart + 1;
                    byte nx = data[offset + 2];
                    byte ny = data[offset + 3];
                    int nDeltaX = DecodeSigned8(nx);
                    int nDeltaY = -DecodeSigned8(ny);
                    return (nDeltaX, nDeltaY, XxxControl.NeedleChange, 4);
                }
                throw new InvalidDataException($"Unknown XXX control code: 0x{ctrl:X2}");
        }
    }

    private static int DecodeSigned8(byte b) => b <= 127 ? b : b - 256;
}

[Flags]
public enum XxxControl : byte
{
    Normal = 0,
    Jump = 1,
    Trim = 2,
    ColorChange = 4,
    Stop = 8,
    NeedleChange = 16,
    End = 32
}