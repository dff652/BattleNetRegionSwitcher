namespace BattleNetRegionSwitcher.Core;

public sealed record WaitProgress(int SecondsRemaining, RuntimeStatus Status);

/// <summary>One explicit, bounded request. Never closes processes or retries a failed launch.</summary>
public sealed class WaitForExit
{
    private readonly ClientLauncher launcher;
    private readonly TimeProvider clock;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    public WaitForExit(ClientLauncher launcher, TimeProvider? clock = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.launcher = launcher;
        this.clock = clock ?? TimeProvider.System;
        this.delay = delay ?? ((duration, token) => Task.Delay(duration, this.clock, token));
    }

    public async Task<LaunchResult> LaunchAsync(string path, LoginRegion region, TimeSpan timeout,
        CancellationToken cancellationToken = default, IProgress<WaitProgress>? progress = null)
    {
        if (cancellationToken.IsCancellationRequested) return Cancelled();
        try { path = ClientLauncher.ValidatePath(path); }
        catch { return new(false, "等待未开始：启动器路径无效或无法访问。"); }
        if (!Enum.IsDefined(region)) return new(false, "等待未开始：不支持的登录区域。");
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(10))
            return new(false, "等待未开始：等待时限无效。");

        var started = clock.GetTimestamp();
        bool WithinDeadline() => clock.GetElapsedTime(started) < timeout;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!WithinDeadline()) return TimedOut();
                var state = launcher.ReadRuntimeStatus();
                cancellationToken.ThrowIfCancellationRequested();
                if (!WithinDeadline()) return TimedOut();
                if (!state.IsReliable) return new(false, "等待已停止：无法可靠检查现有进程，请稍后重试。");
                if (state.IsClear)
                    return launcher.LaunchWhenAllowed(path, region, cancellationToken, WithinDeadline);

                var remaining = timeout - clock.GetElapsedTime(started);
                progress?.Report(new((int)Math.Ceiling(remaining.TotalSeconds), state));
                await delay(remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Cancelled(); }
    }

    internal static LaunchResult TimedOut() => new(false, "等待已超时，未发送启动请求。确认战网、Agent 和游戏已退出后可重试。");
    private static LaunchResult Cancelled() => new(false, "等待已取消，未发送启动请求。");
}
