using CoverApiVerification;
using Xunit;

internal static class ResourceBudgetTests
{
    internal static async Task Run()
    {
        var good = new NativeResourceBudget.Snapshot(805306368, 0, 0, 671088639, 0, 0, 0, 0, 0, 0, 0, 0);
        var cases = 0;
        void Test(string name, Action action) { action(); cases++; Console.WriteLine("BUDGET_PASS " + name); }
        void Reject(string name, NativeResourceBudget.Snapshot value) =>
            Test(name, () => Assert.Throws<InvalidDataException>(() => NativeResourceBudget.Validate(value)));
        Test("below", () => NativeResourceBudget.Validate(good));
        Test("exact", () => NativeResourceBudget.Validate(good with { Peak = 671088640 }));
        Reject("above", good with { Peak = 671088641 });
        Reject("zero-peak", good with { Peak = 0 });
        Reject("wrong-memory", good with { MemoryMax = 536870912 });
        Reject("extra-swap", good with { SwapMax = 1 });
        Reject("used-swap", good with { SwapCurrent = 1 });
        Reject("memory-max-events", good with { Max = 1 });
        Reject("memory-high-events", good with { High = 1 });
        Reject("oom-ten-not-zero", good with { Oom = 10 });
        Reject("oom-kill", good with { OomKill = 1 });
        Reject("group-kill", good with { OomGroupKill = 1 });
        Reject("swap-high", good with { SwapHigh = 1 });
        Reject("swap-max", good with { SwapEventsMax = 1 });
        Reject("swap-fail", good with { SwapFail = 1 });

        var files = new Dictionary<string, string>
        {
            ["memory.max"] = "805306368\n", ["memory.swap.max"] = "0\n",
            ["memory.swap.current"] = "0\n", ["memory.peak"] = "671088639\n",
            ["memory.events"] = "low 0\nhigh 0\nmax 0\noom 0\noom_kill 0\noom_group_kill 0\n",
            ["memory.swap.events"] = "high 0\nmax 0\nfail 0\n",
            ["cpu.max"] = "200000 100000\n", ["pids.max"] = "256\n",
        };
        NativeResourceBudget.Snapshot Read() => NativeResourceBudget.Read(p => files[Path.GetFileName(p)]);
        Test("parse-complete", () => Assert.Equal(good, Read()));
        files["memory.events"] += "sock_throttled 1\n";
        Test("unexpected-memory-event", () => Assert.Throws<InvalidDataException>(() => Read()));
        files["memory.events"] = files["memory.events"].Replace("sock_throttled 1\n", "");
        files["memory.swap.events"] += "future_limit 1\n";
        Test("unexpected-swap-event", () => Assert.Throws<InvalidDataException>(() => Read()));
        files["memory.swap.events"] = files["memory.swap.events"].Replace("future_limit 1\n", "");
        foreach (var key in files.Keys.ToArray())
        {
            var original = files[key];
            files.Remove(key);
            Test("missing-" + key, () => Assert.Throws<KeyNotFoundException>(() => Read()));
            files[key] = "invalid";
            Test("invalid-" + key, () => Assert.Throws<InvalidDataException>(() => Read()));
            files[key] = original;
        }
        foreach (var invalid in new[] { "max", "-1", "+1", "１２", "9223372036854775808", "", "1 2" })
        {
            files["memory.peak"] = invalid;
            Test("invalid-number-" + cases, () => Assert.Throws<InvalidDataException>(() => Read()));
        }
        files["memory.peak"] = "671088639";
        files["cpu.max"] = "100000 100000";
        Test("wrong-effective-cpu", () => Assert.Throws<InvalidDataException>(() => Read()));
        files["cpu.max"] = "200000 100000";
        files["pids.max"] = "512";
        Test("wrong-effective-pids", () => Assert.Throws<InvalidDataException>(() => Read()));
        files["pids.max"] = "256";
        files["memory.events"] += "oom 0\n";
        Test("duplicate-counter", () => Assert.Throws<InvalidDataException>(() => Read()));
        files["memory.events"] = "max 0\nhigh 0\noom 0\noom_kill 0";
        Test("incomplete-counters", () => Assert.Throws<InvalidDataException>(() => Read()));

        var disposed = false;
        var reads = 0;
        var lines = new List<string>();
        async Task<int> Work()
        {
            await using var owner = new Teardown(() => disposed = true);
            await Task.Yield();
            return 0;
        }
        Assert.Equal(0, await NativeResourceBudget.VerifyAsync(Work, () =>
        {
            reads++;
            if (reads == 2) Assert.True(disposed);
            return good;
        }, lines.Add));
        Assert.Equal(2, reads);
        Assert.Equal(2, lines.Count);
        Assert.Contains("stage=teardown-complete", lines[1]);
        cases++;
        Console.WriteLine("BUDGET_PASS after-async-disposal");
        var peak = good.Peak;
        async Task<int> PeakDuringTeardown()
        {
            await using var owner = new Teardown(() => peak = 671088641);
            await Task.Yield();
            return 0;
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => NativeResourceBudget.VerifyAsync(PeakDuringTeardown,
            () => good with { Peak = peak }, _ => { }));
        cases++;
        Console.WriteLine("BUDGET_PASS teardown-crosses-threshold");
        reads = 0;
        await Assert.ThrowsAsync<IOException>(() => NativeResourceBudget.VerifyAsync(Work,
            () => ++reads == 1 ? good : throw new IOException("synthetic unavailable metric"), _ => { }));
        cases++;
        Console.WriteLine("BUDGET_PASS missing-final-metric");
        await Assert.ThrowsAsync<InvalidDataException>(() => NativeResourceBudget.VerifyAsync(
            () => Task.FromResult(137), () => good, _ => { }));
        cases++;
        Console.WriteLine("BUDGET_PASS nonzero-exit");
        lines.Clear();
        await Assert.ThrowsAsync<IOException>(() => NativeResourceBudget.VerifyAsync(
            () => throw new IOException("synthetic failed cleanup"), () => good, lines.Add));
        Assert.Single(lines);
        cases++;
        Console.WriteLine("BUDGET_PASS cleanup-failure-not-complete");
        reads = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => NativeResourceBudget.VerifyAsync(Work,
            () => ++reads == 1 ? good : good with { Peak = 1 }, _ => { }));
        cases++;
        Console.WriteLine("BUDGET_PASS stale-decreasing-peak");
        Console.WriteLine($"BUDGET_TESTS passed={cases} failed=0");
    }

    private sealed class Teardown(Action dispose) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() { await Task.Yield(); dispose(); }
    }
}
