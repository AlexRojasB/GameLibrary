using System.Buffers.Binary;
using System.Text;
using GameLibrary.Api.CoverUploads;
using GameLibrary.Core.CoverUploads;
using SkiaSharp;

namespace GameLibrary.IntegrationTests;

public sealed class CoverImageProcessorTests
{
    private readonly CoverImageProcessor processor = new();

    [Fact]
    public void Decoder_tolerated_nonconforming_ac_run_is_normalized_as_fresh_bounded_jpeg()
    {
        var jpeg = new List<byte> { 255, 216 };
        void Segment(byte marker, byte[] payload)
        {
            jpeg.AddRange([255, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2)]);
            jpeg.AddRange(payload);
        }
        Segment(0xdb, [0, .. Enumerable.Repeat((byte)1, 64)]);
        Segment(0xc0, [8, 0, 240, 0, 160, 1, 1, 0x11, 0]);
        Segment(0xc4, [0, 1, .. new byte[15], 0]);
        // AC code 0 = (run 15,size 10), code 10 = EOB. The table itself is legal.
        Segment(0xc4, [0x10, 1, 1, .. new byte[14], 0xfa, 0]);
        Segment(0xda, [1, 1, 0, 0, 63, 0]);
        var header = jpeg.ToArray();
        // Positive control with exactly the same headers/tables: DC 0 + EOB = 010.
        AssertCanonical(processor.Process([.. header,
            .. Enumerable.Range(0, 75).SelectMany(_ => new byte[] { 0x49, 0x24, 0x92 }), 255, 217]), 160, 240);
        // Each block: DC category 0, then four AC (run=15,size=10) codes.
        // The fourth run targets coefficient 64, outside the legal 0..63 block.
        // 600 blocks * (1 + 4 * (1 + 10)) bits, with no truncation or bad marker.
        jpeg.AddRange(new byte[600 * 45 / 8]);
        jpeg.AddRange([255, 217]);
        // Explicitly approved bounded-normalization policy: this source is still
        // nonconforming, but passes structure/limits and reports no decoder warning.
        var original = jpeg.ToArray();
        var normalized = processor.Process(original);
        AssertCanonical(normalized, 160, 240);
        Assert.False(original.SequenceEqual(normalized));
        AssertCanonical(processor.Process(normalized), 160, 240);
    }

    [Fact]
    public void Investigated_39_case_corpus_passes_the_integrated_pipeline()
    {
        var corpus = new List<(byte[] Bytes, bool Valid)>();
        foreach (var noise in new[] { false, true })
        {
            byte[] jpeg;
            if (noise)
            {
                using var bitmap = new SKBitmap(320, 480, SKColorType.Bgra8888, SKAlphaType.Opaque);
                var samples = bitmap.GetPixelSpan();
                new Random(13).NextBytes(samples);
                for (var i = 3; i < samples.Length; i += 4) samples[i] = 255;
                using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
                jpeg = encoded.ToArray();
            }
            else jpeg = Raster(320, 480, SKEncodedImageFormat.Jpeg);
            corpus.Add((jpeg, true));
            var sos = FindMarker(jpeg, 0xda);
            var start = sos + 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(sos + 2));
            foreach (var percent in new[] { 1, 10, 50, 90, 99 })
            {
                var end = start + Math.Max(1, (jpeg.Length - start - 2) * percent / 100);
                while (jpeg[end - 1] == 255) end--;
                corpus.Add(([.. jpeg[..end], 255, 217], false));
            }
            corpus.Add(([.. jpeg[..start], 255, 217], false));
            corpus.Add((jpeg[..^2], false));
        }
        foreach (var damage in new[] { "length-zero", "length-one", "length-overrun", "missing-sof", "missing-sos",
            "missing-dqt", "missing-dht", "empty-dqt", "empty-dht", "duplicate-sof", "duplicate-components",
            "zero-sampling", "unknown-scan-component", "invalid-sequential-scan" })
            corpus.Add((JpegStructuralFixture(damage), false));
        foreach (var progressive in new[] { false, true })
        foreach (var restart in new[] { false, true })
        {
            var jpeg = CoverJpegFixtures.Create(progressive, restart);
            corpus.Add((jpeg, true));
            var sos = FindMarker(jpeg, 0xda);
            var start = sos + 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(sos + 2));
            corpus.Add(([.. jpeg[..(start + 4)], 255, 217], false));
            if (!progressive && !restart)
            {
                var stripped = jpeg;
                var p = 2;
                while (stripped[p + 1] != 0xda)
                {
                    var length = 2 + BinaryPrimitives.ReadUInt16BigEndian(stripped.AsSpan(p + 2));
                    if (stripped[p + 1] == 0xc4) stripped = [.. stripped[..p], .. stripped[(p + length)..]];
                    else p += length;
                }
                corpus.Add((stripped, false));
            }
        }
        Assert.Equal(39, corpus.Count);
        foreach (var item in corpus)
            if (item.Valid) AssertCanonical(processor.Process(item.Bytes), 320, 480);
            else AssertFailure(item.Bytes, CoverFailure.InvalidImage);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Baseline_progressive_and_restart_images_remain_valid(bool progressive, bool restart)
    {
        AssertCanonical(processor.Process(CoverJpegFixtures.Create(progressive, restart)), 320, 480);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 1)]
    [InlineData(0, 3)]
    [InlineData(0, 4)]
    public void Supported_subsampling_grayscale_and_cmyk_images_remain_valid(int subsampling, int colorSpace)
    {
        AssertCanonical(processor.Process(CoverJpegFixtures.Create(subsampling: subsampling, colorSpace: colorSpace)), 320, 480);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Premature_eoi_in_baseline_progressive_and_restart_scans_is_rejected(bool progressive, bool restart)
    {
        var jpeg = CoverJpegFixtures.Create(progressive, restart);
        var sos = FindMarker(jpeg, 0xda);
        var start = sos + 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(sos + 2));
        AssertFailure([.. jpeg[..(start + 4)], 255, 217], CoverFailure.InvalidImage);
    }

    [Fact]
    public void Missing_standard_huffman_tables_must_not_be_repaired_using_mjpeg_defaults()
    {
        var jpeg = CoverJpegFixtures.Create();
        var position = 2;
        while (jpeg[position + 1] != 0xda)
        {
            var length = 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(position + 2));
            if (jpeg[position + 1] == 0xc4) jpeg = [.. jpeg[..position], .. jpeg[(position + length)..]];
            else position += length;
        }
        AssertFailure(jpeg, CoverFailure.InvalidImage);
    }

    [Theory]
    [InlineData("oversubscribed-huffman")]
    [InlineData("incomplete-huffman")]
    [InlineData("invalid-huffman-id")]
    [InlineData("zero-quantizer")]
    [InlineData("invalid-quantizer-id")]
    [InlineData("duplicate-scan-components")]
    public void Invalid_table_contents_and_scan_components_are_rejected(string damage)
    {
        var jpeg = Raster(320, 480, SKEncodedImageFormat.Jpeg);
        var dht = FindMarker(jpeg, 0xc4);
        var dqt = FindMarker(jpeg, 0xdb);
        var sos = FindMarker(jpeg, 0xda);
        switch (damage)
        {
            case "oversubscribed-huffman": jpeg[dht + 5] = 2; break;
            case "incomplete-huffman": jpeg[dht + 20] = 255; break;
            case "invalid-huffman-id": jpeg[dht + 4] = 0xff; break;
            case "zero-quantizer": jpeg[dqt + 5] = 0; break;
            case "invalid-quantizer-id": jpeg[dqt + 4] = 0x0f; break;
            case "duplicate-scan-components": jpeg[sos + 7] = jpeg[sos + 5]; break;
        }
        AssertFailure(jpeg, CoverFailure.InvalidImage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Wrong_restart_order_and_missing_restart_interval_are_rejected(bool removeInterval)
    {
        var jpeg = CoverJpegFixtures.Create(restart: true);
        if (removeInterval)
        {
            var dri = FindMarker(jpeg, 0xdd);
            jpeg = [.. jpeg[..dri], .. jpeg[(dri + 6)..]];
        }
        else
        {
            var rst = jpeg.AsSpan().IndexOf(new byte[] { 255, 208 });
            Assert.True(rst > 0);
            jpeg[rst + 1] = 215;
        }
        AssertFailure(jpeg, CoverFailure.InvalidImage);
    }

    [Fact]
    public void Deterministic_mutated_jpegs_never_escape_the_safe_processing_boundary()
    {
        var source = CoverJpegFixtures.Create(progressive: true, restart: true);
        var random = new Random(1301);
        for (var i = 0; i < 1000; i++)
        {
            var bytes = source.ToArray();
            for (var j = 0; j <= i % 7; j++) bytes[random.Next(2, bytes.Length)] ^= (byte)random.Next(1, 256);
            try
            {
                var output = processor.Process(bytes);
                using var codec = SKCodec.Create(new MemoryStream(output));
                AssertCanonical(output, codec.Info.Width, codec.Info.Height);
            }
            catch (CoverException ex)
            {
                Assert.Equal(CoverFailure.InvalidImage, ex.Failure);
                Assert.Null(ex.InnerException);
            }
        }
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public void Supported_rasters_are_fully_decoded_and_encoded_as_fresh_jpeg(SKEncodedImageFormat format)
    {
        var input = Raster(320, 480, format);
        var output = processor.Process(input);
        AssertCanonical(output, 320, 480);
        Assert.False(input.SequenceEqual(output));
        using var bitmap = SKBitmap.Decode(output);
        AssertColor(bitmap.GetPixel(40, 40), SKColors.Red);
        AssertColor(bitmap.GetPixel(280, 440), SKColors.Blue);
    }

    [Theory]
    [InlineData(160, 240, 160, 240)]
    [InlineData(161, 242, 162, 243)]
    [InlineData(161, 241, 162, 243)]
    [InlineData(161, 243, 162, 243)]
    [InlineData(160, 239, 160, 240)]
    [InlineData(160, 241, 162, 243)]
    [InlineData(1200, 1800, 960, 1440)]
    [InlineData(1632, 2448, 960, 1440)]
    public void Canonical_rounding_and_resize_do_not_crop_or_upscale(int width, int height, int outputWidth, int outputHeight)
    {
        AssertCanonical(processor.Process(Raster(width, height, SKEncodedImageFormat.Png)), outputWidth, outputHeight);
    }

    [Fact]
    public void Rounding_gaps_are_white_padding_not_stretched_pixels()
    {
        using var bitmap = Solid(160, 241, SKColors.Black);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var result = SKBitmap.Decode(processor.Process(encoded.ToArray()));
        Assert.Equal(162, result.Width);
        AssertColor(result.GetPixel(0, 100), SKColors.White);
        AssertColor(result.GetPixel(1, 100), SKColors.Black);
        AssertColor(result.GetPixel(161, 100), SKColors.White);
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(2, true, false)]
    [InlineData(3, true, true)]
    [InlineData(4, false, true)]
    [InlineData(5, false, false)]
    [InlineData(6, true, false)]
    [InlineData(7, true, true)]
    [InlineData(8, false, true)]
    public void Every_exif_orientation_is_applied_before_portrait_validation(int orientation, bool reverseX, bool reverseY)
    {
        var swapped = orientation >= 5;
        var input = Raster(swapped ? 480 : 320, swapped ? 320 : 480, SKEncodedImageFormat.Jpeg);
        input = WithExif(input, orientation);
        using var sourceData = SKData.CreateCopy(input);
        using var codec = SKCodec.Create(sourceData);
        Assert.Equal(orientation, (int)codec.EncodedOrigin);
        var output = processor.Process(input);
        AssertCanonical(output, 320, 480);
        using var bitmap = SKBitmap.Decode(output);
        // Source uses opposite red/blue halves of both axes. Test all four quadrants.
        AssertColor(bitmap.GetPixel(reverseX ? 280 : 40, reverseY ? 440 : 40), SKColors.Red);
        AssertColor(bitmap.GetPixel(reverseX ? 40 : 280, reverseY ? 40 : 440), SKColors.Blue);
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public void Alpha_is_flattened_on_white(SKEncodedImageFormat format)
    {
        using var bitmap = Solid(160, 240, SKColors.Transparent);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = new SKColor(255, 0, 0, 128) })
            canvas.DrawRect(0, 120, 160, 120, paint);
        using var data = bitmap.Encode(format, 100);
        using var result = SKBitmap.Decode(processor.Process(data.ToArray()));
        AssertColor(result.GetPixel(80, 60), SKColors.White);
        AssertColor(result.GetPixel(80, 180), new SKColor(255, 127, 127), 8);
    }

    [Fact]
    public void Embedded_display_p3_is_converted_to_srgb_before_profile_removal()
    {
        var primaries = new SKColorSpacePrimaries(.68f, .32f, .265f, .69f, .15f, .06f, .3127f, .329f);
        using var p3 = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, primaries.ToColorSpaceXyz());
        using var bitmap = new SKBitmap(new SKImageInfo(160, 240, SKColorType.Rgba8888, SKAlphaType.Opaque, p3));
        // Write literal P3 samples; Erase/SetPixel/Canvas convert sRGB colors on entry.
        var samples = bitmap.GetPixelSpan();
        for (var i = 0; i < samples.Length; i += 4)
        {
            samples[i] = 180;
            samples[i + 1] = 100;
            samples[i + 2] = 60;
            samples[i + 3] = 255;
        }
        using var pixels = bitmap.PeekPixels();
        using var data = pixels.Encode(SKPngEncoderOptions.Default);
        Assert.NotNull(data);
        var input = data.ToArray();
        Assert.Contains("iCCP", Encoding.Latin1.GetString(input));
        using var codec = SKCodec.Create(new MemoryStream(input));
        using var colorSpace = codec.Info.ColorSpace;
        Assert.NotNull(colorSpace);
        Assert.False(colorSpace.IsSrgb);
        var output = processor.Process(input);
        AssertCanonical(output, 160, 240);
        using var result = SKBitmap.Decode(output);
        AssertColor(result.GetPixel(80, 120), new SKColor(193, 95, 47), 7);
        Assert.DoesNotContain("ICC_PROFILE", Encoding.Latin1.GetString(output));
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public void Metadata_and_trailing_payload_are_not_copied(SKEncodedImageFormat format)
    {
        var input = Raster(160, 240, format);
        if (format == SKEncodedImageFormat.Jpeg) input = WithExif(input, 1);
        if (format == SKEncodedImageFormat.Png)
            input = [.. input[..33], .. PngChunk("tEXt", "Comment\0PRIVATE-GPS-THUMBNAIL"u8.ToArray()), .. input[33..]];
        input = [.. input, .. "<svg onload='PRIVATE-TRAILING-PAYLOAD'/>"u8];
        var output = processor.Process(input);
        AssertCanonical(output, 160, 240);
        Assert.DoesNotContain("PRIVATE", Encoding.Latin1.GetString(output));
        Assert.DoesNotContain("Exif", Encoding.Latin1.GetString(output));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(158, 237)]
    [InlineData(320, 320)]
    [InlineData(320, 477)]
    [InlineData(4097, 10)]
    [InlineData(2000, 3000)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void Invalid_ratios_small_images_and_dimension_bombs_are_rejected_before_decode(int width, int height)
    {
        var input = Raster(160, 240, SKEncodedImageFormat.Png);
        var header = input.AsSpan(16, 13).ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        input = [.. input[..8], .. PngChunk("IHDR", header), .. input[33..]];
        AssertFailure(input, CoverFailure.InvalidImage);
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public void Truncated_containers_and_corrupt_compressed_data_are_rejected(SKEncodedImageFormat format)
    {
        var input = Raster(320, 480, format);
        AssertFailure(input[..^1], CoverFailure.InvalidImage);
        AssertFailure(input[..(input.Length / 2)], CoverFailure.InvalidImage);
        // Preserve a complete container and valid chunk checksums, but destroy the raster stream.
        if (format == SKEncodedImageFormat.Png)
        {
            var chunks = PngChunks(input);
            input = [.. input[..8], .. chunks.SelectMany(c => PngChunk(c.Type,
                c.Type == "IDAT" ? new byte[c.Data.Length] : c.Data))];
        }
        else if (format == SKEncodedImageFormat.Jpeg)
        {
            var sos = FindMarker(input, 0xda);
            var start = sos + 2 + BinaryPrimitives.ReadUInt16BigEndian(input.AsSpan(sos + 2));
            input = [.. input[..start], 0xff, 0xd9];
        }
        else
        {
            // Clear the VP8/VP8L entropy payload while leaving its dimensional header intact.
            var index = FindWebpRaster(input);
            Array.Clear(input, index + 18, input.Length - index - 18);
        }
        AssertFailure(input, CoverFailure.InvalidImage);
    }

    [Fact]
    public void Jpeg_scan_requires_entropy_not_only_markers_or_fill_bytes()
    {
        var jpeg = Raster(320, 480, SKEncodedImageFormat.Jpeg);
        var sos = FindMarker(jpeg, 0xda);
        var start = sos + 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(sos + 2));
        foreach (var markerOnly in new byte[][] { [], [0xff], [0xff, 0xff], [0xff, 0xd0],
            [0xff, 0xd0, 0xff, 0xd1], [0xff, 0xff, 0x00] })
            AssertFailure([.. jpeg[..start], .. markerOnly, 0xff, 0xd9], CoverFailure.InvalidImage);
    }

    [Theory]
    [InlineData("length-zero")]
    [InlineData("length-one")]
    [InlineData("length-overrun")]
    [InlineData("missing-sof")]
    [InlineData("missing-sos")]
    [InlineData("missing-dqt")]
    [InlineData("missing-dht")]
    [InlineData("empty-dqt")]
    [InlineData("empty-dht")]
    [InlineData("duplicate-sof")]
    [InlineData("duplicate-components")]
    [InlineData("zero-sampling")]
    [InlineData("unknown-scan-component")]
    [InlineData("invalid-sequential-scan")]
    public void Malformed_jpeg_segments_tables_and_scan_headers_are_rejected(string damage)
    {
        AssertFailure(JpegStructuralFixture(damage), CoverFailure.InvalidImage);
    }

    // Shared with the isolated G01 candidate probe: all mutations retain the same
    // bounded raster dimensions, so rejection must concern the JPEG, not the ratio.
    public static byte[] JpegStructuralFixture(string damage)
    {
        var jpeg = Raster(320, 480, SKEncodedImageFormat.Jpeg);
        var marker = damage switch
        {
            "missing-sof" or "duplicate-sof" or "duplicate-components" or "zero-sampling" => (byte)0xc0,
            "missing-sos" or "unknown-scan-component" or "invalid-sequential-scan" => (byte)0xda,
            "missing-dqt" or "empty-dqt" => (byte)0xdb,
            "missing-dht" or "empty-dht" => (byte)0xc4,
            _ => (byte)0xdb
        };
        var start = FindMarker(jpeg, marker);
        var length = 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(start + 2));
        if (damage.StartsWith("missing-", StringComparison.Ordinal))
        {
            if (damage == "missing-sos") return [.. jpeg[..start], 0xff, 0xd9];
            // There can be several DQT/DHT markers; remove every matching table.
            while (true)
            {
                jpeg = [.. jpeg[..start], .. jpeg[(start + length)..]];
                if (marker is not (0xdb or 0xc4)) return jpeg;
                start = 2;
                while (jpeg[start + 1] != marker && jpeg[start + 1] != 0xda)
                    start += 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(start + 2));
                if (jpeg[start + 1] == 0xda) return jpeg;
                length = 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(start + 2));
            }
        }
        switch (damage)
        {
            case "length-zero": jpeg[start + 2] = 0; jpeg[start + 3] = 0; break;
            case "length-one": jpeg[start + 2] = 0; jpeg[start + 3] = 1; break;
            case "length-overrun": jpeg[start + 2] = 255; jpeg[start + 3] = 255; break;
            case "empty-dqt" or "empty-dht":
                return [.. jpeg[..start], 0xff, marker, 0, 2, .. jpeg[(start + length)..]];
            case "duplicate-sof": return [.. jpeg[..start], .. jpeg.AsSpan(start, length).ToArray(), .. jpeg[start..]];
            case "duplicate-components": jpeg[start + 13] = jpeg[start + 10]; break;
            case "zero-sampling": jpeg[start + 11] = 0; break;
            case "unknown-scan-component": jpeg[start + 5] = 254; break;
            case "invalid-sequential-scan": jpeg[start + length - 2] = 0; break;
            default: throw new ArgumentOutOfRangeException(nameof(damage));
        }
        return jpeg;
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 10)]
    [InlineData(false, 50)]
    [InlineData(false, 90)]
    [InlineData(false, 99)]
    [InlineData(true, 1)]
    [InlineData(true, 10)]
    [InlineData(true, 50)]
    [InlineData(true, 90)]
    [InlineData(true, 99)]
    public void Nonempty_truncated_jpeg_entropy_with_inserted_eoi_is_rejected(bool noise, int retainedPercent)
    {
        byte[] jpeg;
        if (noise)
        {
            using var source = new SKBitmap(320, 480, SKColorType.Bgra8888, SKAlphaType.Opaque);
            var samples = source.GetPixelSpan();
            new Random(13).NextBytes(samples);
            for (var i = 3; i < samples.Length; i += 4) samples[i] = 255;
            using var encoded = source.Encode(SKEncodedImageFormat.Jpeg, 90);
            jpeg = encoded.ToArray();
        }
        else jpeg = Raster(320, 480, SKEncodedImageFormat.Jpeg);
        AssertCanonical(processor.Process(jpeg), 320, 480);

        var sos = FindMarker(jpeg, 0xda);
        var start = sos + 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(sos + 2));
        var entropyLength = jpeg.Length - start - 2;
        var end = start + Math.Max(1, entropyLength * retainedPercent / 100);
        // Cut at a real entropy byte boundary, not in the middle of an FF00 escape.
        while (jpeg[end - 1] == 0xff) end--;
        Assert.InRange(end - start, 1, entropyLength - 1);
        byte[] damaged = [.. jpeg[..end], 0xff, 0xd9];

        using var codec = SKCodec.Create(new MemoryStream(damaged));
        Assert.NotNull(codec);
        using var pixels = new SKBitmap(codec.Info);
        var fullResult = codec.GetPixels(pixels.Info, pixels.GetPixels());
        using var scanlineCodec = SKCodec.Create(new MemoryStream(damaged));
        var scanlineResult = scanlineCodec.StartScanlineDecode(pixels.Info);
        var rows = scanlineResult == SKCodecResult.Success
            ? scanlineCodec.GetScanlines(pixels.GetPixels(), pixels.Height, pixels.RowBytes) : 0;
        var error = Record.Exception(() => processor.Process(damaged));
        Assert.True(error is CoverException { Failure: CoverFailure.InvalidImage },
            $"Truncated JPEG accepted: noise={noise}, entropy={end - start}/{entropyLength}, " +
            $"GetPixels={fullResult}, StartScanlineDecode={scanlineResult}, decodedRows={rows}/{pixels.Height}.");
    }

    [Fact]
    public void Bad_png_crc_order_and_unknown_critical_chunks_are_rejected()
    {
        var input = Raster(160, 240, SKEncodedImageFormat.Png);
        var corrupt = input.ToArray();
        corrupt[32] ^= 1;
        AssertFailure(corrupt, CoverFailure.InvalidImage);
        AssertFailure([.. input[..33], .. PngChunk("ABCD", []), .. input[33..]], CoverFailure.InvalidImage);
        AssertFailure([.. input[..33], .. PngChunk("IHDR", input[16..29]), .. input[33..]], CoverFailure.InvalidImage);
        var chunks = PngChunks(input);
        var idat = chunks.First(c => c.Type == "IDAT").Data;
        var split = idat.Length / 2;
        var brokenOrder = chunks.SelectMany(c => c.Type == "IDAT"
            ? PngChunk("IDAT", idat[..split]).Concat(PngChunk("tEXt", "x\0y"u8.ToArray())).Concat(PngChunk("IDAT", idat[split..]))
            : PngChunk(c.Type, c.Data));
        AssertFailure([.. input[..8], .. brokenOrder], CoverFailure.InvalidImage);
    }

    [Fact]
    public void Actual_two_frame_apng_is_rejected_even_if_codec_exposes_only_first_frame()
    {
        var png = Raster(160, 240, SKEncodedImageFormat.Png);
        var chunks = PngChunks(png);
        var compressed = chunks.Where(c => c.Type == "IDAT").SelectMany(c => c.Data).ToArray();
        var animation = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(animation, 2);
        byte[] Control(uint sequence)
        {
            var control = new byte[26];
            BinaryPrimitives.WriteUInt32BigEndian(control, sequence);
            BinaryPrimitives.WriteUInt32BigEndian(control.AsSpan(4), 160);
            BinaryPrimitives.WriteUInt32BigEndian(control.AsSpan(8), 240);
            control[21] = 1;
            control[23] = 10;
            return control;
        }
        var frame = new byte[compressed.Length + 4];
        BinaryPrimitives.WriteUInt32BigEndian(frame, 2);
        compressed.CopyTo(frame, 4);
        var input = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }
            .Concat(PngChunk("IHDR", chunks[0].Data))
            .Concat(PngChunk("acTL", animation))
            .Concat(PngChunk("fcTL", Control(0)))
            .Concat(PngChunk("IDAT", compressed))
            .Concat(PngChunk("fcTL", Control(1)))
            .Concat(PngChunk("fdAT", frame))
            .Concat(PngChunk("IEND", [])).ToArray();
        using var firstFrame = SKBitmap.Decode(input);
        Assert.NotNull(firstFrame);
        AssertFailure(input, CoverFailure.InvalidImage);
        // Individual animation chunks must also fail, even without acTL.
        foreach (var type in new[] { "acTL", "fcTL", "fdAT" })
            AssertFailure([.. png[..33], .. PngChunk(type, type == "acTL" ? animation : Control(0)), .. png[33..]], CoverFailure.InvalidImage);
    }

    [Fact]
    public void Actual_animated_webp_and_hidden_animation_chunks_are_rejected()
    {
        using var red = Solid(160, 240, SKColors.Red);
        using var blue = Solid(160, 240, SKColors.Blue);
        using var redPixels = red.PeekPixels();
        using var bluePixels = blue.PeekPixels();
        var frames = new[] { new SKWebpEncoderFrame(redPixels, TimeSpan.FromMilliseconds(100)),
            new SKWebpEncoderFrame(bluePixels, TimeSpan.FromMilliseconds(100)) };
        using var encoded = SKWebpEncoder.EncodeAnimated(frames, SKWebpEncoderOptions.Default);
        Assert.NotNull(encoded);
        var input = encoded.ToArray();
        using var codec = SKCodec.Create(new MemoryStream(input));
        Assert.True(codec.FrameCount > 1);
        AssertFailure(input, CoverFailure.InvalidImage);
        var png = Raster(160, 240, SKEncodedImageFormat.Webp);
        foreach (var type in new[] { "ANIM", "ANMF" })
        {
            var extended = new byte[png.Length + 8];
            png.CopyTo(extended, 0);
            Encoding.ASCII.GetBytes(type).CopyTo(extended, png.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(extended.AsSpan(4), (uint)(extended.Length - 8));
            AssertFailure(extended, CoverFailure.InvalidImage);
        }
    }

    [Fact]
    public void Webp_invalid_riff_lengths_duplicate_images_and_dimension_mismatches_are_rejected()
    {
        var input = Raster(160, 240, SKEncodedImageFormat.Webp);
        var oversized = input.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(oversized.AsSpan(4), uint.MaxValue);
        AssertFailure(oversized, CoverFailure.InvalidImage);
        var duplicate = input.Concat(input[12..]).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(duplicate.AsSpan(4), (uint)(duplicate.Length - 8));
        AssertFailure(duplicate, CoverFailure.InvalidImage);
    }

    [Fact]
    public void Unsupported_and_signature_only_files_fail_safely()
    {
        foreach (var input in new[] { "<svg onload='attack'/>"u8.ToArray(), "GIF89a"u8.ToArray(),
            "\0\0\0\x18ftypheic"u8.ToArray(), "\0\0\0\x18ftypavif"u8.ToArray(), "%PDF-1.7"u8.ToArray() })
            AssertFailure(input, CoverFailure.UnsupportedFormat);
        AssertFailure([0xff, 0xd8], CoverFailure.InvalidImage);
        AssertFailure([137, 80, 78, 71, 13, 10, 26, 10], CoverFailure.InvalidImage);
        AssertFailure("RIFF\x04\0\0\0WEBP"u8.ToArray(), CoverFailure.InvalidImage);
        AssertFailure([], CoverFailure.InvalidImage);
        AssertFailure(new byte[CoverLimits.MaxUploadBytes + 1], CoverFailure.TooLarge);
    }

    [Fact]
    public void Detailed_high_entropy_image_stays_within_output_budget_and_quality_85()
    {
        var input = ResourceFixture();
        var output = processor.Process(input);
        AssertCanonical(output, 960, 1440);
        using var data = SKData.CreateCopy(input);
        using var codec = SKCodec.Create(data);
        using var srgb = SKColorSpace.CreateSrgb();
        using var decoded = new SKBitmap(new SKImageInfo(960, 1440, SKColorType.Bgra8888, SKAlphaType.Premul, srgb));
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(decoded.Info, decoded.GetPixels()));
        using var canvasBitmap = new SKBitmap(new SKImageInfo(960, 1440, SKColorType.Bgra8888, SKAlphaType.Opaque, srgb));
        using (var canvas = new SKCanvas(canvasBitmap))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(decoded, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        }
        using var pixels = canvasBitmap.PeekPixels();
        using var expected = pixels.Encode(new SKJpegEncoderOptions(85));
        Assert.NotNull(expected);
        // Compare quantization tables, independent of encoder APP/ICC metadata.
        Assert.Equal(JpegTable(expected.ToArray()), JpegTable(output));
    }

    // Used by the final-image harness as two simultaneous decode/encode pipelines.
    public static byte[] ResourceFixture()
    {
        using var bitmap = new SKBitmap(960, 1440, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var random = new Random(13);
        var pixels = new byte[bitmap.ByteCount];
        random.NextBytes(pixels);
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private void AssertFailure(byte[] input, CoverFailure failure)
    {
        var exception = Assert.Throws<CoverException>(() => processor.Process(input));
        Assert.Equal(failure, exception.Failure);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("Skia", exception.Message);
    }

    private static void AssertCanonical(byte[] bytes, int width, int height)
    {
        Assert.InRange(width, 160, 960);
        Assert.InRange(height, 240, 1440);
        Assert.InRange(bytes.Length, 1, CoverLimits.MaxOutputBytes);
        Assert.Equal(new byte[] { 0xff, 0xd8 }, bytes[..2]);
        Assert.Equal(new byte[] { 0xff, 0xd9 }, bytes[^2..]);
        using var codec = SKCodec.Create(new MemoryStream(bytes));
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
        Assert.Equal(SKEncodedOrigin.TopLeft, codec.EncodedOrigin);
        Assert.Equal(width, codec.Info.Width);
        Assert.Equal(height, codec.Info.Height);
        Assert.Equal(3 * width, 2 * height);
        using var bitmap = new SKBitmap(codec.Info);
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(bitmap.Info, bitmap.GetPixels()));
        var position = 2;
        while (bytes[position + 1] != 0xda)
        {
            Assert.False(bytes[position + 1] is >= 0xe0 and <= 0xef or 0xfe, "Output retained APP/COM metadata.");
            position += 2 + BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position + 2));
        }
    }

    private static void AssertColor(SKColor actual, SKColor expected, int tolerance = 12)
    {
        Assert.InRange(Math.Abs(actual.Red - expected.Red), 0, tolerance);
        Assert.InRange(Math.Abs(actual.Green - expected.Green), 0, tolerance);
        Assert.InRange(Math.Abs(actual.Blue - expected.Blue), 0, tolerance);
    }

    private static SKBitmap Solid(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        return bitmap;
    }

    private static byte[] Raster(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = Solid(width, height, SKColors.Green);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.Red })
        {
            canvas.DrawRect(0, 0, width / 2f, height / 2f, paint);
            paint.Color = SKColors.Blue;
            canvas.DrawRect(width / 2f, height / 2f, width, height, paint);
        }
        using var encoded = bitmap.Encode(format, 100);
        return encoded.ToArray();
    }

    private static byte[] WithExif(byte[] jpeg, int orientation)
    {
        // A little-endian TIFF IFD with orientation, GPS IFD pointer, and a thumbnail IFD.
        byte[] tiff = [0x49, 0x49, 42, 0, 8, 0, 0, 0, 2, 0,
            0x12, 1, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0,
            0x25, 0x88, 4, 0, 1, 0, 0, 0, 38, 0, 0, 0,
            56, 0, 0, 0,
            1, 0, 1, 0, 2, 0, 2, 0, 0, 0, (byte)'N', 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0];
        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).Concat("PRIVATE-GPS-THUMBNAIL"u8.ToArray()).ToArray();
        var marker = new byte[payload.Length + 4];
        marker[0] = 0xff;
        marker[1] = 0xe1;
        BinaryPrimitives.WriteUInt16BigEndian(marker.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(marker, 4);
        return [.. jpeg[..2], .. marker, .. jpeg[2..]];
    }

    private static List<(string Type, byte[] Data)> PngChunks(byte[] png)
    {
        var chunks = new List<(string, byte[])>();
        for (var p = 8; p < png.Length;)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(p));
            chunks.Add((Encoding.ASCII.GetString(png, p + 4, 4), png.AsSpan(p + 8, size).ToArray()));
            p += size + 12;
        }
        return chunks;
    }

    private static byte[] PngChunk(string type, byte[] data)
    {
        var chunk = new byte[data.Length + 12];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        var crc = uint.MaxValue;
        foreach (var value in chunk.AsSpan(4, data.Length + 4))
        {
            crc ^= value;
            for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(chunk.Length - 4), ~crc);
        return chunk;
    }

    private static int FindMarker(byte[] jpeg, byte marker)
    {
        var p = 2;
        while (jpeg[p + 1] != marker) p += 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(p + 2));
        return p;
    }

    private static byte[] JpegTable(byte[] jpeg)
    {
        var p = FindMarker(jpeg, 0xdb);
        var size = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(p + 2));
        return jpeg.AsSpan(p + 4, size - 2).ToArray();
    }

    private static int FindWebpRaster(byte[] webp)
    {
        var p = 12;
        while (!webp.AsSpan(p, 4).SequenceEqual("VP8 "u8) && !webp.AsSpan(p, 4).SequenceEqual("VP8L"u8))
        {
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(p + 4));
            p += 8 + size + (size & 1);
        }
        return p;
    }
}
