using System.Runtime.InteropServices;

namespace GameLibrary.IntegrationTests;

// Test fixture generation only. Production uses only the decompression API.
public static class CoverJpegFixtures
{
    private static readonly IntPtr Library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory,
        OperatingSystem.IsMacOS() ? "libgamelibrary-turbojpeg.dylib" : "libgamelibrary-turbojpeg.so"));
    private static readonly Init InitNative = Export<Init>("tj3Init");
    private static readonly Set SetNative = Export<Set>("tj3Set");
    private static readonly Compress CompressNative = Export<Compress>("tj3Compress8");
    private static readonly Destroy DestroyNative = Export<Destroy>("tj3Destroy");
    private static readonly Destroy FreeNative = Export<Destroy>("tj3Free");

    public static byte[] Create(bool progressive = false, bool restart = false, int width = 320,
        int height = 480, int subsampling = 2, int colorSpace = 1)
    {
        var cmyk = colorSpace is 3 or 4;
        var pixels = new byte[checked(width * height * (cmyk ? 4 : 3))];
        new Random(13).NextBytes(pixels);
        var handle = InitNative(0);
        if (handle == IntPtr.Zero) throw new InvalidOperationException("Fixture encoder initialization failed.");
        var output = IntPtr.Zero;
        nuint size = 0;
        try
        {
            if (SetNative(handle, 3, 85) != 0 || SetNative(handle, 4, subsampling) != 0 ||
                SetNative(handle, 8, colorSpace) != 0 || SetNative(handle, 12, progressive ? 1 : 0) != 0 ||
                SetNative(handle, 18, restart ? 7 : 0) != 0 ||
                CompressNative(handle, pixels, width, 0, height, cmyk ? 11 : 0, ref output, ref size) != 0)
                throw new InvalidOperationException("Fixture encoding failed.");
            var bytes = new byte[checked((int)size)];
            Marshal.Copy(output, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { FreeNative(output); DestroyNative(handle); }
    }

    private static T Export<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(Library, name));
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Init(int kind);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Set(IntPtr handle, int parameter, int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Destroy(IntPtr pointer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Compress(IntPtr handle, [In] byte[] pixels,
        int width, int pitch, int height, int format, ref IntPtr output, ref nuint size);
}
