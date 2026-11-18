using System.Buffers.Binary;
using GameLibrary.Core.CoverUploads;
using SkiaSharp;

namespace GameLibrary.Api.CoverUploads;

// Only the supported 8-bit Huffman JPEG cover profile. Entropy/MCU validation
// belongs to TurboJPEG, not to this bounded marker/table preflight.
internal static class CoverJpegStructure
{
    internal const int MaxScans = 99;

    public static CoverImageContainer Validate(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > CoverLimits.MaxUploadBytes)
            throw new CoverException(CoverFailure.TooLarge, "The cover image must be at most 5 MiB.");
        var position = 2;
        var width = 0;
        var height = 0;
        var components = 0;
        var scans = 0;
        var progressive = false;
        var baseline = false;
        var restartInterval = 0;
        Span<byte> ids = stackalloc byte[4];
        Span<byte> quantizers = stackalloc byte[4];
        Span<byte> quantizationPrecision = stackalloc byte[4];
        quantizationPrecision.Fill(255);
        Span<bool> huffman = stackalloc bool[8];
        huffman.Clear();
        Span<sbyte> approximation = stackalloc sbyte[4 * 64];
        approximation.Fill(-1);

        while (position < bytes.Length)
        {
            if (bytes[position++] != 255) throw Invalid();
            while (position < bytes.Length && bytes[position] == 255) position++;
            if (position == bytes.Length) throw Invalid();
            var marker = bytes[position++];
            if (marker == 0xd9)
            {
                if (scans == 0) throw Invalid();
                for (var i = 0; i < components; i++)
                    if (approximation[i * 64] < 0) throw Invalid();
                return new(SKEncodedImageFormat.Jpeg, width, height, position);
            }
            if (position + 2 > bytes.Length) throw Invalid();
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[position..]);
            if (length < 2 || length > bytes.Length - position) throw Invalid();
            var segment = bytes.Slice(position + 2, length - 2);
            position += length;

            switch (marker)
            {
                case 0xc0 or 0xc1 or 0xc2:
                    if (components != 0 || segment.Length < 6 || segment[0] != 8 ||
                        segment[5] is < 1 or > 4 || segment.Length != 6 + 3 * segment[5]) throw Invalid();
                    height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
                    width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
                    CoverImageContainer.ValidateDimensions(width, height);
                    components = segment[5];
                    progressive = marker == 0xc2;
                    baseline = marker == 0xc0;
                    var blocks = 0;
                    for (var i = 0; i < components; i++)
                    {
                        var offset = 6 + 3 * i;
                        for (var j = 0; j < i; j++)
                            if (ids[j] == segment[offset]) throw Invalid();
                        ids[i] = segment[offset];
                        var horizontal = segment[offset + 1] >> 4;
                        var vertical = segment[offset + 1] & 15;
                        if (horizontal is < 1 or > 4 || vertical is < 1 or > 4 || segment[offset + 2] > 3)
                            throw Invalid();
                        blocks += horizontal * vertical;
                        quantizers[i] = segment[offset + 2];
                    }
                    if (components > 1 && blocks > 10) throw Invalid();
                    break;
                case 0xdb:
                    if (segment.IsEmpty) throw Invalid();
                    while (!segment.IsEmpty)
                    {
                        var key = segment[0];
                        var precision = key >> 4;
                        var index = key & 15;
                        var size = 1 + 64 * (precision + 1);
                        if (precision > 1 || index > 3 || segment.Length < size) throw Invalid();
                        for (var i = 1; i < size; i += precision + 1)
                            if (precision == 0 ? segment[i] == 0 : BinaryPrimitives.ReadUInt16BigEndian(segment[i..]) == 0)
                                throw Invalid();
                        quantizationPrecision[index] = (byte)precision;
                        segment = segment[size..];
                    }
                    break;
                case 0xc4:
                    if (segment.IsEmpty) throw Invalid();
                    while (!segment.IsEmpty)
                    {
                        if (segment.Length < 17) throw Invalid();
                        var kind = segment[0] >> 4;
                        var index = segment[0] & 15;
                        if (kind > 1 || index > 3) throw Invalid();
                        var symbols = 0;
                        var slots = 1;
                        for (var i = 1; i <= 16; i++)
                        {
                            symbols += segment[i];
                            slots = 2 * slots - segment[i];
                            // JPEG Huffman codes may not fill the all-ones code.
                            if (slots <= 0) throw Invalid();
                        }
                        if (symbols is < 1 or > 256 || segment.Length < 17 + symbols) throw Invalid();
                        foreach (var symbol in segment.Slice(17, symbols))
                            if (kind == 0 ? symbol > 11 : (symbol & 15) > 10) throw Invalid();
                        huffman[4 * kind + index] = true;
                        segment = segment[(17 + symbols)..];
                    }
                    break;
                case 0xdd:
                    if (segment.Length != 2) throw Invalid();
                    restartInterval = BinaryPrimitives.ReadUInt16BigEndian(segment);
                    break;
                case 0xda:
                    if (components == 0 || ++scans > MaxScans || segment.Length < 6 || segment[0] is < 1 or > 4 ||
                        segment[0] > components || segment.Length != 4 + 2 * segment[0]) throw Invalid();
                    var first = segment[^3];
                    var last = segment[^2];
                    var high = segment[^1] >> 4;
                    var low = segment[^1] & 15;
                    if (!progressive && (first != 0 || last != 63 || high != 0 || low != 0)) throw Invalid();
                    if (progressive && (last > 63 || first > last || (first == 0 ? last != 0 : segment[0] != 1) ||
                        high > 13 || low > 13 || (high != 0 && low != high - 1))) throw Invalid();
                    for (var i = 0; i < segment[0]; i++)
                    {
                        var id = segment[1 + 2 * i];
                        for (var j = 0; j < i; j++)
                            if (segment[1 + 2 * j] == id) throw Invalid();
                        var component = ids[..components].IndexOf(id);
                        if (component < 0) throw Invalid();
                        var dc = segment[2 + 2 * i] >> 4;
                        var ac = segment[2 + 2 * i] & 15;
                        var precision = quantizationPrecision[quantizers[component]];
                        if (dc > 3 || ac > 3 || precision == 255 || (baseline && precision != 0) ||
                            ((!progressive || first == 0 && high == 0) && !huffman[dc]) ||
                            ((!progressive || first > 0) && !huffman[4 + ac]) ||
                            (progressive && (first == 0 ? ac != 0 : dc != 0))) throw Invalid();
                        for (var coefficient = first; coefficient <= last; coefficient++)
                        {
                            var index = component * 64 + coefficient;
                            if (approximation[index] != (high == 0 ? -1 : high)) throw Invalid();
                            approximation[index] = (sbyte)low;
                        }
                    }
                    var entropy = false;
                    var restart = 0;
                    while (position < bytes.Length)
                    {
                        if (bytes[position] != 255) { entropy = true; position++; continue; }
                        var start = position++;
                        while (position < bytes.Length && bytes[position] == 255) position++;
                        if (position == bytes.Length) throw Invalid();
                        if (bytes[position] == 0)
                        {
                            if (position != start + 1) throw Invalid();
                            entropy = true;
                            position++;
                            continue;
                        }
                        if (bytes[position] is >= 0xd0 and <= 0xd7)
                        {
                            if (!entropy || restartInterval == 0 || bytes[position] != 0xd0 + restart) throw Invalid();
                            restart = (restart + 1) % 8;
                            entropy = false;
                            position++;
                            continue;
                        }
                        position = start;
                        break;
                    }
                    if (!entropy) throw Invalid();
                    break;
                case >= 0xe0 and <= 0xef or 0xfe:
                    break;
                default:
                    throw Invalid();
            }
        }
        throw Invalid();
    }

    private static CoverException Invalid() => CoverImageProcessor.Invalid(
        "The JPEG structure is damaged, incomplete or unsupported. Export the image again.");
}
