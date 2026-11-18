using System.Buffers.Binary;
using GameLibrary.Core.CoverUploads;
using SkiaSharp;

namespace GameLibrary.Api.CoverUploads;

// Container validation complements the codec: decoders may tolerate missing end markers,
// invalid checksums or unknown animation chunks and still return a usable first frame.
internal readonly record struct CoverImageContainer(SKEncodedImageFormat Format, int Width, int Height, int Length)
{
    public static CoverImageContainer Validate(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0xff, 0xd8 })) return CoverJpegStructure.Validate(bytes);
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return Png(bytes);
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
            return WebP(bytes);
        throw new CoverException(CoverFailure.UnsupportedFormat, "Choose JPEG, PNG or static WebP. SVG, GIF, HEIC and AVIF are not supported.");
    }

    public static void ValidateDimensions(long width, long height)
    {
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096 || checked(width * height) > 4_000_000)
            throw CoverImageProcessor.Invalid("The image must have at most 4 million pixels and no edge longer than 4096 pixels.");
    }

    private static CoverException Malformed() => CoverImageProcessor.Invalid("The image container is damaged, incomplete or animated. Export a static image again.");

    private static CoverImageContainer Png(ReadOnlySpan<byte> bytes)
    {
        var position = 8;
        var width = 0;
        var height = 0;
        var data = false;
        var dataEnded = false;
        var palette = false;
        byte color = 0;
        while (position <= bytes.Length - 12)
        {
            var size = BinaryPrimitives.ReadUInt32BigEndian(bytes[position..]);
            if (size > bytes.Length - position - 12) throw Malformed();
            var length = (int)size;
            var type = bytes.Slice(position + 4, 4);
            foreach (var letter in type)
                if (letter is not (>= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z')) throw Malformed();
            if ((type[2] & 32) != 0 || Crc32(bytes.Slice(position + 4, length + 4)) !=
                BinaryPrimitives.ReadUInt32BigEndian(bytes[(position + 8 + length)..])) throw Malformed();
            var chunk = bytes.Slice(position + 8, length);
            if (type.SequenceEqual("acTL"u8) || type.SequenceEqual("fcTL"u8) || type.SequenceEqual("fdAT"u8))
                throw Malformed();
            if (position == 8 && !type.SequenceEqual("IHDR"u8)) throw Malformed();
            if (type.SequenceEqual("IHDR"u8))
            {
                if (position != 8 || length != 13) throw Malformed();
                var w = BinaryPrimitives.ReadUInt32BigEndian(chunk);
                var h = BinaryPrimitives.ReadUInt32BigEndian(chunk[4..]);
                ValidateDimensions(w, h);
                width = (int)w;
                height = (int)h;
                color = chunk[9];
                var depth = chunk[8];
                if (chunk[10] != 0 || chunk[11] != 0 || chunk[12] > 1 || !(color switch
                    {
                        0 => depth is 1 or 2 or 4 or 8 or 16,
                        2 or 4 or 6 => depth is 8 or 16,
                        3 => depth is 1 or 2 or 4 or 8,
                        _ => false
                    })) throw Malformed();
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (palette || data || color is 0 or 4 || length == 0 || length > 768 || length % 3 != 0)
                    throw Malformed();
                palette = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (dataEnded || (color == 3 && !palette)) throw Malformed();
                data = true;
            }
            else
            {
                if (data) dataEnded = true;
                if (type.SequenceEqual("IEND"u8))
                {
                    if (!data || length != 0) throw Malformed();
                    return new(SKEncodedImageFormat.Png, width, height, position + 12);
                }
                if ((type[0] & 32) == 0) throw Malformed();
            }
            position += length + 12;
        }
        throw Malformed();
    }

    private static CoverImageContainer WebP(ReadOnlySpan<byte> bytes)
    {
        var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        if (size < 4 || size > bytes.Length - 8 || (size & 1) != 0) throw Malformed();
        var end = (int)size + 8;
        var position = 12;
        var width = 0;
        var height = 0;
        var image = false;
        var extended = false;
        var alpha = false;
        var flags = 0;
        var seen = new HashSet<uint>();
        while (position <= end - 8)
        {
            var type = bytes.Slice(position, 4);
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(position + 4)..]);
            if (chunkSize > end - position - 8) throw Malformed();
            var length = (int)chunkSize;
            var next = checked(position + 8 + length + (length & 1));
            if (next > end || ((length & 1) != 0 && bytes[next - 1] != 0)) throw Malformed();
            var chunk = bytes.Slice(position + 8, length);
            if (type.SequenceEqual("ANIM"u8) || type.SequenceEqual("ANMF"u8)) throw Malformed();
            if (type.SequenceEqual("VP8X"u8))
            {
                if (position != 12 || length != 10 || (chunk[0] & 0xc3) != 0 ||
                    chunk[1] != 0 || chunk[2] != 0 || chunk[3] != 0) throw Malformed();
                extended = true;
                flags = chunk[0];
                width = 1 + Read24(chunk[4..]);
                height = 1 + Read24(chunk[7..]);
                ValidateDimensions(width, height);
            }
            else if (type.SequenceEqual("VP8 "u8) || type.SequenceEqual("VP8L"u8))
            {
                if (image) throw Malformed();
                int w, h;
                if (type.SequenceEqual("VP8 "u8))
                {
                    if (length < 10 || (chunk[0] & 1) != 0 || !chunk.Slice(3, 3).SequenceEqual(new byte[] { 0x9d, 0x01, 0x2a }))
                        throw Malformed();
                    w = BinaryPrimitives.ReadUInt16LittleEndian(chunk[6..]) & 0x3fff;
                    h = BinaryPrimitives.ReadUInt16LittleEndian(chunk[8..]) & 0x3fff;
                }
                else
                {
                    if (length < 5 || chunk[0] != 0x2f || (chunk[4] & 0xe0) != 0 || alpha) throw Malformed();
                    var bits = BinaryPrimitives.ReadUInt32LittleEndian(chunk[1..]);
                    w = 1 + (int)(bits & 0x3fff);
                    h = 1 + (int)((bits >> 14) & 0x3fff);
                }
                ValidateDimensions(w, h);
                if (extended && (w != width || h != height)) throw Malformed();
                width = w;
                height = h;
                image = true;
            }
            else if (type.SequenceEqual("ALPH"u8))
            {
                if (!extended || image || alpha || length == 0 || (flags & 16) == 0) throw Malformed();
                alpha = true;
            }
            else if (type.SequenceEqual("ICCP"u8) || type.SequenceEqual("EXIF"u8) || type.SequenceEqual("XMP "u8))
            {
                var flag = type.SequenceEqual("ICCP"u8) ? 32 : type.SequenceEqual("EXIF"u8) ? 8 : 4;
                if (!extended || (flags & flag) == 0 || !seen.Add(BinaryPrimitives.ReadUInt32LittleEndian(type)) ||
                    (flag == 32 && image) || (flag != 32 && !image)) throw Malformed();
            }
            position = next;
        }
        if (position != end || !image || ((flags & 32) != 0 && !seen.Contains(BinaryPrimitives.ReadUInt32LittleEndian("ICCP"u8))) ||
            ((flags & 8) != 0 && !seen.Contains(BinaryPrimitives.ReadUInt32LittleEndian("EXIF"u8))) ||
            ((flags & 4) != 0 && !seen.Contains(BinaryPrimitives.ReadUInt32LittleEndian("XMP "u8)))) throw Malformed();
        return new(SKEncodedImageFormat.Webp, width, height, end);
    }

    private static int Read24(ReadOnlySpan<byte> value) => value[0] | value[1] << 8 | value[2] << 16;

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        return ~crc;
    }

    public static byte[] StripJpegMetadata(byte[] bytes)
    {
        using var output = new MemoryStream(bytes.Length);
        output.Write(bytes, 0, 2);
        var position = 2;
        while (position < bytes.Length)
        {
            var marker = bytes[position + 1];
            if (marker == 0xda)
            {
                output.Write(bytes, position, bytes.Length - position);
                return output.ToArray();
            }
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position + 2)) + 2;
            if (marker is not (>= 0xe0 and <= 0xef or 0xfe)) output.Write(bytes, position, length);
            position += length;
        }
        throw Malformed();
    }
}
