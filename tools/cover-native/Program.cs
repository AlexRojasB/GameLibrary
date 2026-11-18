using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using GameLibrary.Api.CoverUploads;
using GameLibrary.Core.CoverUploads;
using GameLibrary.IntegrationTests;
using Xunit;

if (args.Contains("--expect-unavailable"))
{
    using var bitmap = new SkiaSharp.SKBitmap(160, 240);
    bitmap.Erase(SkiaSharp.SKColors.White);
    using var encoded = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 85);
    try { new CoverImageProcessor().Process(encoded.ToArray()); }
    catch (CoverException ex) when (ex.Failure == CoverFailure.Unavailable && ex.InnerException is null)
    {
        Console.WriteLine("PASS missing/tampered native asset fails unavailable without decoder fallback.");
        return 0;
    }
    return 1;
}
Console.WriteLine($"Runtime={RuntimeInformation.FrameworkDescription} OS={RuntimeInformation.OSDescription} arch={RuntimeInformation.ProcessArchitecture} user={Environment.UserName}");
if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64 || Environment.UserName == "root")
    throw new Exception("Native Linux amd64/non-root gate failed.");
var tests = new CoverImageProcessorTests();
var passed = 0;
var failed = 0;
foreach (var method in typeof(CoverImageProcessorTests).GetMethods().Where(m => m.GetCustomAttribute<FactAttribute>() is not null))
{
    var data = method.GetCustomAttributes<InlineDataAttribute>().SelectMany(a => a.GetData(method)).ToArray();
    if (data.Length == 0) data = [Array.Empty<object>()];
    foreach (var arguments in data)
    {
        try { method.Invoke(tests, arguments); passed++; }
        catch (Exception ex) { Console.WriteLine($"FAIL {method.Name}: {ex.InnerException ?? ex}"); failed++; }
    }
}
Console.WriteLine($"IMAGE TESTS passed={passed} failed={failed}");

// Two admitted pipelines, including full near-4MP baseline/progressive native passes.
var jpeg = CoverJpegFixtures.Create(width: 1632, height: 2448);
var progressive = CoverJpegFixtures.Create(progressive: true, width: 1632, height: 2448);
var png = CoverImageProcessorTests.ResourceFixture();
foreach (var fixture in new[] { jpeg, progressive, png })
    if (fixture.Length > CoverLimits.MaxUploadBytes) throw new Exception("Resource fixture exceeds upload limit.");
GC.Collect();
GC.WaitForPendingFinalizers();
var watch = Stopwatch.StartNew();
await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
{
    var processor = new CoverImageProcessor();
    for (var i = 0; i < 10; i++)
        foreach (var fixture in new[] { jpeg, progressive, png })
            if (processor.Process(fixture).Length > CoverLimits.MaxOutputBytes) throw new Exception("Output budget exceeded.");
})));
Console.WriteLine($"RESOURCE pipelines=2 operations=60 JPEG={jpeg.Length} progressive={progressive.Length} PNG={png.Length} elapsedMs={watch.ElapsedMilliseconds} processPeakBytes={Process.GetCurrentProcess().PeakWorkingSet64}");
foreach (var path in new[] { "/sys/fs/cgroup/memory.peak", "/sys/fs/cgroup/memory.events", "/sys/fs/cgroup/memory.max" })
    if (File.Exists(path)) Console.WriteLine($"{path}: {File.ReadAllText(path).Trim()}");
return failed == 0 ? 0 : 1;
