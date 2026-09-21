using BattleNetRegionSwitcher.Core;

namespace BattleNetRegionSwitcher.CoreTests;

public static class RuntimeAndWaitTests
{
    public static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("runtime distinguishes client agent game and temporary processes", RuntimeCategories);
        yield return ("failed runtime probe never means clear", () => Assert(!new ClientLauncher(new ThrowingProbe()).ReadRuntimeStatus().IsClear));
        yield return ("idle runtime ignores unrelated processes", () => Assert(new ClientLauncher(new FakeProbe("explorer", "notepad")).ReadRuntimeStatus().IsClear));
        yield return ("waiting launches once after all blockers exit", WaitUntilClear);
        yield return ("waiting times out without launch", Timeout);
        yield return ("waiting cancels during delay without launch", CancelDuringWait);
        yield return ("cancel before waiting avoids even probing", CancelBeforeWait);
        yield return ("waiting stops on probe failure", ProbeFails);
        yield return ("waiting rejects path and region before polling", InvalidInputs);
        yield return ("waiting keeps game and updater blocked", KeepBlockers);
        yield return ("waiting final recheck blocks new client", FinalRace);
        yield return ("waiting deadline applies during final probes", DeadlineDuringFinalProbe);
        yield return ("waiting cancellation applies during final probes", CancellationDuringFinalProbe);
        yield return ("waiting does not retry failed starter", FailedStarter);
    }

    private static void RuntimeCategories()
    {
        var state = new ClientLauncher(new FakeProbe("battle.NET launcher", "AGENT", "wowclassic", "TEMP_privateValue")).ReadRuntimeStatus();
        Assert(state.IsReliable && state.ClientRunning && state.AgentRunning && state.KnownGames.SequenceEqual(["WowClassic"]) && state.TemporaryProcessRunning && !state.IsClear);
        Assert(!state.Description.Contains("privateValue"));
    }

    private static void WaitUntilClear() => WithLauncherPath(path =>
    {
        var clock = new TestClock();
        var starter = new FakeStarter();
        var probe = new SequenceProbe(["Battle.net", "Agent"], ["Agent"], []);
        var events = new List<WaitProgress>();
        var result = Run(new ClientLauncher(probe, starter), path, clock, progress: new InlineProgress(events.Add));
        Assert(result.Started && starter.Calls == 1 && starter.Argument == "--setregion=TW");
        Assert(events.Count == 2 && events[0].Status.ClientRunning && !events[1].Status.ClientRunning && events[1].Status.AgentRunning);
        Assert(events[0].SecondsRemaining > events[1].SecondsRemaining);
    });

    private static void Timeout() => WithLauncherPath(path =>
    {
        var starter = new FakeStarter();
        var result = Run(new ClientLauncher(new FakeProbe("Agent"), starter), path, new TestClock());
        Assert(!result.Started && result.Message.Contains("超时") && starter.Calls == 0);
    });

    private static void CancelDuringWait() => WithLauncherPath(path =>
    {
        using var source = new CancellationTokenSource();
        var starter = new FakeStarter();
        var result = Run(new ClientLauncher(new FakeProbe("Battle.net"), starter), path, new TestClock(), source.Token,
            onDelay: () => source.Cancel());
        Assert(!result.Started && result.Message.Contains("取消") && starter.Calls == 0);
    });

    private static void CancelBeforeWait()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var probe = new CallbackProbe(() => throw new Exception("No probe expected"));
        var result = Run(new ClientLauncher(probe, new FakeStarter()), "", new TestClock(), source.Token);
        Assert(!result.Started && result.Message.Contains("取消") && probe.Calls == 0);
    }

    private static void ProbeFails() => WithLauncherPath(path =>
    {
        var clock = new TestClock();
        var starter = new FakeStarter();
        var result = Run(new ClientLauncher(new ThrowingProbe(), starter), path, clock);
        Assert(!result.Started && result.Message.Contains("无法可靠检查") && clock.GetTimestamp() == 0 && starter.Calls == 0);
    });

    private static void InvalidInputs() => WithLauncherPath(path =>
    {
        var probe = new CallbackProbe(() => []);
        var launcher = new ClientLauncher(probe, new FakeStarter());
        Assert(!Run(launcher, path + ".missing", new TestClock()).Started);
        Assert(!Run(launcher, path, new TestClock(), region: (LoginRegion)42).Started);
        Assert(!new WaitForExit(launcher).LaunchAsync(path, LoginRegion.Asia, TimeSpan.Zero).GetAwaiter().GetResult().Started);
        Assert(probe.Calls == 0);
    });

    private static void KeepBlockers() => WithLauncherPath(path =>
    {
        foreach (var name in new[] { "Agent", "Wow", "temp_ABCD" })
        {
            var starter = new FakeStarter();
            Assert(!Run(new ClientLauncher(new FakeProbe(name), starter), path, new TestClock()).Started && starter.Calls == 0);
        }
    });

    private static void FinalRace() => WithLauncherPath(path =>
    {
        var starter = new FakeStarter();
        var result = Run(new ClientLauncher(new SequenceProbe([], [], ["Battle.net"]), starter), path, new TestClock());
        Assert(!result.Started && starter.Calls == 0);
    });

    private static void DeadlineDuringFinalProbe() => WithLauncherPath(path =>
    {
        var clock = new TestClock();
        var calls = 0;
        var probe = new CallbackProbe(() => { if (++calls == 3) clock.Advance(TimeSpan.FromSeconds(3)); return []; });
        var starter = new FakeStarter();
        var result = Run(new ClientLauncher(probe, starter), path, clock);
        Assert(!result.Started && result.Message.Contains("超时") && starter.Calls == 0);
    });

    private static void CancellationDuringFinalProbe() => WithLauncherPath(path =>
    {
        using var source = new CancellationTokenSource();
        var calls = 0;
        var probe = new CallbackProbe(() => { if (++calls == 3) source.Cancel(); return []; });
        var starter = new FakeStarter();
        var result = Run(new ClientLauncher(probe, starter), path, new TestClock(), source.Token);
        Assert(!result.Started && result.Message.Contains("取消") && starter.Calls == 0);
    });

    private static void FailedStarter() => WithLauncherPath(path =>
    {
        var starter = new FakeStarter { ShouldStart = false };
        Assert(!Run(new ClientLauncher(new FakeProbe(), starter), path, new TestClock()).Started && starter.Calls == 1);
    });

    private static LaunchResult Run(ClientLauncher launcher, string path, TestClock clock, CancellationToken token = default,
        Action? onDelay = null, LoginRegion region = LoginRegion.Asia, IProgress<WaitProgress>? progress = null)
        => new WaitForExit(launcher, clock, (duration, cancellation) =>
        {
            clock.Advance(duration);
            onDelay?.Invoke();
            cancellation.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }).LaunchAsync(path, region, TimeSpan.FromSeconds(3), token, progress).GetAwaiter().GetResult();

    private static void WithLauncherPath(Action<string> run)
    {
        var directory = Directory.CreateTempSubdirectory("BNWait-").FullName;
        var path = Path.Combine(directory, "Battle.net Launcher.exe");
        try { File.WriteAllText(path, "test fixture; never executed"); run(path); }
        finally { File.Delete(path); Directory.Delete(directory); }
    }

    private static void Assert(bool condition) { if (!condition) throw new Exception("assertion failed"); }
    private sealed class TestClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public void Advance(TimeSpan amount) => ticks += amount.Ticks;
    }
    private sealed class CallbackProbe(Func<IReadOnlyList<string>> read) : IProcessProbe
    {
        public int Calls { get; private set; }
        public IReadOnlyList<string> GetProcessNames() { Calls++; return read(); }
    }
    private sealed class InlineProgress(Action<WaitProgress> report) : IProgress<WaitProgress>
    { public void Report(WaitProgress value) => report(value); }
}
