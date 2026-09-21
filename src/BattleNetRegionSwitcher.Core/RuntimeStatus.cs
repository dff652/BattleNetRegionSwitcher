namespace BattleNetRegionSwitcher.Core;

public sealed record RuntimeStatus(bool IsReliable, bool ClientRunning, bool AgentRunning,
    IReadOnlyList<string> KnownGames, bool TemporaryProcessRunning, DateTimeOffset CheckedAt)
{
    public bool IsClear => IsReliable && !ClientRunning && !AgentRunning && KnownGames.Count == 0 && !TemporaryProcessRunning;

    public string Description => !IsReliable ? "运行状态：暂时无法检查，启动将被阻止。"
        : $"战网：{(ClientRunning ? "运行中" : "未检测到")}  ·  Agent：{(AgentRunning ? "运行中" : "未检测到")}\n"
          + $"游戏：{(KnownGames.Count > 0 ? "检测到已知游戏进程" : "未检测到已知游戏进程")}"
          + (TemporaryProcessRunning ? "  ·  检测到临时进程，暂缓启动" : "");

    internal IReadOnlyList<string> Blockers()
    {
        if (!IsReliable) return ["无法可靠检查现有进程，已安全中止"];
        var blockers = new List<string>();
        if (ClientRunning) blockers.Add("检测到 Battle.net 正在运行，请从战网菜单正常退出");
        if (AgentRunning) blockers.Add("Battle.net Agent 正在运行，请等待其结束或人工处理");
        blockers.AddRange(KnownGames.Select(n => $"检测到 {n} 正在运行，请先正常关闭"));
        if (TemporaryProcessRunning) blockers.Add("检测到可能的临时更新进程，请确认更新结束后重试");
        return blockers;
    }
}

internal static class ProcessClassification
{
    private static readonly string[] Games =
    [
        "Wow", "WowT", "WowB", "WowClassic", "WowClassicT", "WowClassicB", "Overwatch", "DiabloIV", "DiabloIII",
        "Hearthstone", "SC2_x64", "SC2", "HeroesOfTheStorm_x64", "HeroesOfTheStorm", "D2R", "cod"
    ];

    internal static RuntimeStatus Read(IProcessProbe probe)
    {
        try
        {
            var names = new HashSet<string>(probe.GetProcessNames(), StringComparer.OrdinalIgnoreCase);
            return new(true, names.Contains("Battle.net") || names.Contains("Battle.net Launcher"), names.Contains("Agent"),
                Games.Where(names.Contains).ToArray(), names.Any(n => n.StartsWith("temp_", StringComparison.OrdinalIgnoreCase)), DateTimeOffset.Now);
        }
        catch { return new(false, false, false, [], false, DateTimeOffset.Now); }
    }
}
