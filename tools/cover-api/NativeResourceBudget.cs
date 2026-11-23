using System.Globalization;

namespace CoverApiVerification;

// Test-only, fixed acceptance profile. No environment/CLI production or diagnostic overrides.
internal static class NativeResourceBudget
{
    internal const long MemoryLimitBytes = 768L * 1024 * 1024;
    internal const long PeakLimitBytes = 640L * 1024 * 1024;
    private const string Cgroup = "/sys/fs/cgroup/";

    internal sealed record Snapshot(long MemoryMax, long SwapMax, long SwapCurrent, long Peak,
        long Max, long High, long Oom, long OomKill, long OomGroupKill,
        long SwapHigh, long SwapEventsMax, long SwapFail);

    internal static async Task<int> VerifyAsync(Func<Task<int>> workload,
        Func<Snapshot>? read = null, Action<string>? emit = null)
    {
        read ??= () => Read(File.ReadAllText);
        emit ??= Console.WriteLine;
        var start = read();
        emit(Line("startup", start));
        Validate(start);
        // Await the method boundary, not an inner scope: all await using/finally
        // teardown in the verifier must complete before this high-water-mark read.
        var exit = await workload();
        var final = read();
        emit(Line("teardown-complete", final));
        Validate(final);
        if (exit != 0 || final.Peak < start.Peak) throw Invalid();
        return exit;
    }

    internal static Snapshot Read(Func<string, string> read)
    {
        string Text(string file)
        {
            var text = read(Cgroup + file);
            if (text.Length > 4096) throw Invalid();
            return text.Trim();
        }
        var events = Counters(Text("memory.events"));
        var swap = Counters(Text("memory.swap.events"));
        if (events.Any(p => p.Value != 0 && p.Key is not ("max" or "high" or "oom" or "oom_kill" or "oom_group_kill"))
            || swap.Any(p => p.Value != 0 && p.Key is not ("high" or "max" or "fail"))) throw Invalid();
        if (Text("cpu.max") != "200000 100000" || Text("pids.max") != "256") throw Invalid();
        return new(Number(Text("memory.max")), Number(Text("memory.swap.max")),
            Number(Text("memory.swap.current")), Number(Text("memory.peak")),
            Required(events, "max"), Required(events, "high"), Required(events, "oom"),
            Required(events, "oom_kill"), Required(events, "oom_group_kill"),
            Required(swap, "high"), Required(swap, "max"), Required(swap, "fail"));
    }

    internal static void Validate(Snapshot value)
    {
        if (value.MemoryMax != MemoryLimitBytes || value.SwapMax != 0 || value.SwapCurrent != 0
            || value.Peak <= 0 || value.Peak > PeakLimitBytes || value.Peak > value.MemoryMax
            || value.Max != 0 || value.High != 0 || value.Oom != 0 || value.OomKill != 0
            || value.OomGroupKill != 0 || value.SwapHigh != 0 || value.SwapEventsMax != 0 || value.SwapFail != 0)
            throw Invalid();
    }

    private static long Number(string text)
    {
        if (text.Length == 0 || text.Any(c => !char.IsAsciiDigit(c))
            || !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            throw Invalid();
        return value;
    }

    private static Dictionary<string, long> Counters(string text)
    {
        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var pair = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (pair.Length != 2 || pair[0].Any(c => c is not (>= 'a' and <= 'z') and not '_')
                || !values.TryAdd(pair[0], Number(pair[1]))) throw Invalid();
        }
        return values;
    }

    private static long Required(Dictionary<string, long> values, string key) =>
        values.TryGetValue(key, out var value) ? value : throw Invalid();

    private static string Line(string stage, Snapshot s) => FormattableString.Invariant(
        $"NATIVE_RESOURCE stage={stage} memoryMax={s.MemoryMax} swapMax={s.SwapMax} swapCurrent={s.SwapCurrent} peak={s.Peak} max={s.Max} high={s.High} oom={s.Oom} oomKill={s.OomKill} oomGroupKill={s.OomGroupKill} swapHigh={s.SwapHigh} swapEventsMax={s.SwapEventsMax} swapFail={s.SwapFail} threshold={PeakLimitBytes}");

    private static InvalidDataException Invalid() => new("Native verifier resource contract failed.");
}
