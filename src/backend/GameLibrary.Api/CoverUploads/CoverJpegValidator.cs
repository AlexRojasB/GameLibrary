using System.Runtime.InteropServices;
using System.Security.Cryptography;
using GameLibrary.Core.CoverUploads;
using Microsoft.Win32.SafeHandles;

namespace GameLibrary.Api.CoverUploads;

// Public TurboJPEG 3 C ABI only. Never use repaired pixels or a private Skia codec handle.
internal static class CoverJpegValidator
{
    private const string Library = "GameLibrary.TurboJpeg.3.2.1";
    private static readonly Lazy<IntPtr> NativeLibraryHandle = new(LoadLibrary);

    static CoverJpegValidator() => NativeLibrary.SetDllImportResolver(typeof(CoverJpegValidator).Assembly,
        (name, _, _) => name == Library ? NativeLibraryHandle.Value : IntPtr.Zero);

    internal static void Validate(byte[] bytes, CoverImageContainer container)
    {
        if (bytes.Length > CoverLimits.MaxUploadBytes || container.Length > bytes.Length || container.Length < 4)
            throw CoverImageProcessor.Invalid("Invalid JPEG size.");
        CoverImageContainer.ValidateDimensions(container.Width, container.Height);
        using var handle = Init(1);
        if (handle.IsInvalid) throw Unavailable();
        SetParameter(handle, 0, 1); // STOPONWARNING: before header parsing, not just the pixel decode.
        SetParameter(handle, 13, CoverJpegStructure.MaxScans);
        SetParameter(handle, 23, 64); // MAXMEMORY MiB, intermediate coefficient buffers.
        SetParameter(handle, 24, 4_000_000);
        SetParameter(handle, 25, 0); // No source-marker/ICC retention in this validation pass.
        Check(DecompressHeader(handle, bytes, (nuint)container.Length));
        if (Get(handle, 5) != container.Width || Get(handle, 6) != container.Height || Get(handle, 7) != 8)
            throw CoverImageProcessor.Invalid("Invalid JPEG dimensions or precision.");
        var colorSpace = Get(handle, 8);
        if (colorSpace is < 0 or > 4) throw CoverImageProcessor.Invalid("Invalid JPEG color space.");
        var cmyk = colorSpace is 3 or 4;
        var size = checked(container.Width * container.Height * (cmyk ? 4 : 3));
        var pixels = Marshal.AllocHGlobal(size);
        try
        {
            // No scaling/cropping/skipped rows. A fresh handle and exact bounded buffer per decode.
            Check(Decompress(handle, bytes, (nuint)container.Length, pixels, 0, cmyk ? 11 : 0));
        }
        finally { Marshal.FreeHGlobal(pixels); }
    }

    private static void SetParameter(JpegHandle handle, int parameter, int value)
    {
        if (Set(handle, parameter, value) != 0) throw Unavailable();
    }

    private static void Check(int result)
    {
        if (result != 0) throw CoverImageProcessor.Invalid("The JPEG is damaged or incomplete. Export the image again.");
    }

    private static IntPtr LoadLibrary()
    {
        var linux = OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64;
        var mac = OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;
        if (!linux && !mac) throw Unavailable();
        var name = linux ? "libgamelibrary-turbojpeg.so" : "libgamelibrary-turbojpeg.dylib";
        var expected = linux
            ? "DFFB4DC98AC21A0BDB18BA5987B9AC97FB8AB6DA92C9679ADD294C7CB50CAA8F"
            : "D82A04634D7E9C5A714F573C422D3D8D7F1C49DE966F3AF59EC9F6DC928997AE";
        // A content-pinned, application-local asset only; never use PATH/LD_LIBRARY_PATH/system fallback.
        var path = Path.Combine(Path.GetDirectoryName(typeof(CoverJpegValidator).Assembly.Location)!, name);
        try
        {
            using var stream = File.OpenRead(path);
            if (Convert.ToHexString(SHA256.HashData(stream)) != expected) throw Unavailable();
            return NativeLibrary.Load(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException or BadImageFormatException)
        {
            throw Unavailable();
        }
    }

    private static CoverException Unavailable() => new(CoverFailure.Unavailable, "Strict JPEG validation is unavailable.");

    private sealed class JpegHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public JpegHandle() : base(ownsHandle: true) { }
        protected override bool ReleaseHandle() { Destroy(handle); return true; }
    }

    [DllImport(Library, EntryPoint = "tj3Init", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern JpegHandle Init(int kind);
    [DllImport(Library, EntryPoint = "tj3Destroy", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern void Destroy(IntPtr handle);
    [DllImport(Library, EntryPoint = "tj3Set", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int Set(JpegHandle handle, int parameter, int value);
    [DllImport(Library, EntryPoint = "tj3Get", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int Get(JpegHandle handle, int parameter);
    [DllImport(Library, EntryPoint = "tj3DecompressHeader", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int DecompressHeader(JpegHandle handle, [In] byte[] bytes, nuint size);
    [DllImport(Library, EntryPoint = "tj3Decompress8", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int Decompress(JpegHandle handle, [In] byte[] bytes, nuint size, IntPtr pixels, int pitch, int format);
}
