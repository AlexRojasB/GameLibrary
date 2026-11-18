using GameLibrary.Core.CoverUploads;
using SkiaSharp;

namespace GameLibrary.Api.CoverUploads;

public sealed class CoverImageProcessor
{
    public byte[] Process(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
            throw Invalid("Choose a non-empty JPEG, PNG or static WebP image.");
        if (bytes.Length > CoverLimits.MaxUploadBytes)
            throw new CoverException(CoverFailure.TooLarge, "The cover image must be at most 5 MiB.");

        try
        {
            // Keep header validation and native buffer sizing on the same private snapshot.
            bytes = bytes.ToArray();
            var container = CoverImageContainer.Validate(bytes);
            if (container.Format == SKEncodedImageFormat.Jpeg)
                CoverJpegValidator.Validate(bytes, container);
            // Decode only the validated container, never a trailing second file/payload.
            using var stream = new MemoryStream(bytes, 0, container.Length, writable: false);
            using var codec = SKCodec.Create(stream);
            if (codec is null || codec.EncodedFormat != container.Format)
                throw Invalid("The image could not be decoded. Export it again as JPEG, PNG or static WebP.");

            var info = codec.Info;
            using var sourceColorSpace = info.ColorSpace;
            CoverImageContainer.ValidateDimensions(info.Width, info.Height);
            if (info.Width != container.Width || info.Height != container.Height ||
                codec.FrameCount > 1 || codec.RepetitionCount != 0)
                throw Invalid("Use a single static image, not an animation or multiple frames.");

            var origin = codec.EncodedOrigin;
            if ((int)origin is < 1 or > 8)
                throw Invalid("The image orientation is invalid.");
            var swapsAxes = (int)origin >= 5;
            var width = swapsAxes ? info.Height : info.Width;
            var height = swapsAxes ? info.Width : info.Height;
            if (Math.Abs(height - Math.Round(width * 1.5, MidpointRounding.AwayFromZero)) > 1)
                throw Invalid("Crop or fit the cover to a portrait 2:3 ratio before uploading.");

            var k = Math.Min(480, Math.Max((width + 1) / 2, (height + 2) / 3));
            if (k < 80)
                throw Invalid("The cover must be at least 160 by 240 pixels.");

            using var srgb = SKColorSpace.CreateSrgb();
            using var decoded = new SKBitmap();
            var decodeInfo = new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul, srgb);
            if (!decoded.TryAllocPixels(decodeInfo))
                throw Unavailable();
            // GetPixels (unlike SKBitmap.Decode) must report complete success, not IncompleteInput.
            if (codec.GetPixels(decodeInfo, decoded.GetPixels()) != SKCodecResult.Success)
                throw Invalid("The image is damaged or incomplete. Export it again before uploading.");
            if (decoded.Width != info.Width || decoded.Height != info.Height)
                throw Invalid("The decoded image dimensions are invalid.");

            using var output = new SKBitmap();
            if (!output.TryAllocPixels(new SKImageInfo(2 * k, 3 * k, SKColorType.Bgra8888, SKAlphaType.Opaque, srgb)))
                throw Unavailable();
            using (var canvas = new SKCanvas(output))
            {
                canvas.Clear(SKColors.White);
                var scale = Math.Min(1f, Math.Min(output.Width / (float)width, output.Height / (float)height));
                canvas.Translate((output.Width - width * scale) / 2, (output.Height - height * scale) / 2);
                canvas.Scale(scale);
                var orientation = origin switch
                {
                    SKEncodedOrigin.TopLeft => SKMatrix.Identity,
                    SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, info.Width, 0, 1, 0, 0, 0, 1),
                    SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, info.Width, 0, -1, info.Height, 0, 0, 1),
                    SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, info.Height, 0, 0, 1),
                    SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
                    SKEncodedOrigin.RightTop => new SKMatrix(0, -1, info.Height, 1, 0, 0, 0, 0, 1),
                    SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, info.Height, -1, 0, info.Width, 0, 0, 1),
                    SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, info.Width, 0, 0, 1),
                    _ => throw Invalid("The image orientation is invalid.")
                };
                canvas.Concat(orientation);
                canvas.DrawBitmap(decoded, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            }

            using var pixels = output.PeekPixels();
            using var encoded = pixels.Encode(new SKJpegEncoderOptions(85));
            if (encoded is null || encoded.Size == 0 || encoded.Size > CoverLimits.MaxOutputBytes)
                throw Invalid("The normalized JPEG must be at most 2 MiB. Choose a simpler or smaller cover.");
            // The fresh raster has no source metadata. Remove encoder ICC/APP metadata as well:
            // pixels have already been converted to sRGB, which is the untagged JPEG default.
            return CoverImageContainer.StripJpegMetadata(encoded.ToArray());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or
                                   BadImageFormatException or TypeInitializationException or OutOfMemoryException)
        {
            throw Unavailable();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
        {
            throw Invalid("The image is malformed. Export it again before uploading.");
        }
    }

    internal static CoverException Invalid(string message) => new(CoverFailure.InvalidImage, message);
    private static CoverException Unavailable() => new(CoverFailure.Unavailable, "Cover image processing is unavailable. Try again later.");
}
