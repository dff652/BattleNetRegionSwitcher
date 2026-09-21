using Microsoft.Win32;
using System.Diagnostics;

namespace BattleNetRegionSwitcher.Core;

public enum LoginRegion
{
    China,
    Asia
}

public record LaunchResult(bool Started, string Message);

public interface IProcessProbe
{
    IReadOnlyList<string> GetProcessNames();
}

public interface IProcessStarter
{
    bool TryStart(string path, string argument, string workingDirectory);
}

internal sealed class SystemProcessProbe : IProcessProbe
{
    public IReadOnlyList<string> GetProcessNames()
    {
        var names = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { names.Add(process.ProcessName); }
                catch (InvalidOperationException) { /* exited between enumeration and inspection */ }
                catch { throw; }
            }
        }
        return names;
    }
}

internal sealed class SystemProcessStarter : IProcessStarter
{
    public bool TryStart(string path, string argument, string workingDirectory)
    {
        try
        {
            var info = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                WorkingDirectory = workingDirectory
            };
            info.ArgumentList.Add(argument);
            using var process = Process.Start(info);
            return process is not null;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class ClientLauncher
{
    private static readonly HashSet<string> AllowedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Battle.net Launcher.exe", "Battle.net.exe"
    };

    private readonly IProcessProbe _probe;
    private readonly IProcessStarter _starter;

    public ClientLauncher(IProcessProbe? probe = null, IProcessStarter? starter = null)
    {
        _probe = probe ?? new SystemProcessProbe();
        _starter = starter ?? new SystemProcessStarter();
    }

    public LaunchResult Launch(string launcherPath, LoginRegion region, CancellationToken cancellationToken = default)
        => LaunchWhenAllowed(launcherPath, region, cancellationToken, () => true);

    internal LaunchResult LaunchWhenAllowed(string launcherPath, LoginRegion region, CancellationToken cancellationToken, Func<bool> withinDeadline)
    {
        if (cancellationToken.IsCancellationRequested) return new(false, "启动请求已取消。");
        string path;
        try { path = ValidatePath(launcherPath); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        { return new(false, "启动已取消：启动器路径无效或无法访问。" + (ex is UnauthorizedAccessException ? "请检查权限。" : "")); }

        if (!Enum.IsDefined(region))
            return new(false, "启动已取消：不支持的登录区域。");

        var blockers = GetBlockers(path);
        if (blockers.Count > 0)
            return new(false, "启动已取消：" + string.Join("；", blockers));

        // Recheck immediately before starting to reduce duplicate-start races.
        blockers = GetBlockers(path);
        if (blockers.Count > 0)
            return new(false, "启动已取消：" + string.Join("；", blockers));

        var argument = region switch
        {
            LoginRegion.China => "--setregion=CN",
            LoginRegion.Asia => "--setregion=TW",
            _ => throw new ArgumentOutOfRangeException(nameof(region))
        };

        if (cancellationToken.IsCancellationRequested) return new(false, "启动请求已取消。");
        if (!withinDeadline()) return WaitForExit.TimedOut();
        if (!_starter.TryStart(path, argument, Path.GetDirectoryName(path)!))
            return new(false, "启动失败：无法启动战网客户端，请检查权限或路径。");

        return new(true, "已发送战网启动请求。请在客户端确认地区并登录。");
    }

    public IReadOnlyList<string> GetBlockers(string launcherPath)
    {
        try { _ = ValidatePath(launcherPath); }
        catch (UnauthorizedAccessException) { return new[] { "无法检查启动器路径权限" }; }
        catch (Exception) { return new[] { "启动器路径无效或不存在" }; }

        return ReadRuntimeStatus().Blockers();
    }

    public RuntimeStatus ReadRuntimeStatus() => ProcessClassification.Read(_probe);

    public static string? ResolveLauncherPath(string? savedPath, Func<string?>? discover = null)
    {
        if (!string.IsNullOrWhiteSpace(savedPath) && TryValidPath(savedPath, out var saved))
            return saved;
        var detected = (discover ?? FindLauncher)();
        return !string.IsNullOrWhiteSpace(detected) && TryValidPath(detected, out var valid) ? valid : null;
    }

    public static string? FindLauncher()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net", "Battle.net Launcher.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Battle.net", "Battle.net Launcher.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net", "Battle.net.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Battle.net", "Battle.net.exe")
        };

        foreach (var candidate in candidates)
            if (TryValidPath(candidate, out var valid)) return valid;

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Battle.net", false)
                    ?? root.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Battle.net", false);
                var install = key?.GetValue("InstallLocation") as string;
                foreach (var name in AllowedFileNames)
                    if (!string.IsNullOrWhiteSpace(install) && TryValidPath(Path.Combine(install, name), out var valid)) return valid;
            }
            catch { /* read-only discovery: ignore inaccessible keys */ }
        }
        return null;
    }

    public static string ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is empty.", nameof(path));
        var full = Path.GetFullPath(path);
        if (!AllowedFileNames.Contains(Path.GetFileName(full))) throw new ArgumentException("Unsupported executable.", nameof(path));
        if (!File.Exists(full)) throw new FileNotFoundException("Executable was not found.", full);
        _ = File.GetAttributes(full);
        return full;
    }

    private static bool TryValidPath(string path, out string? valid)
    {
        try { valid = ValidatePath(path); return true; }
        catch { valid = null; return false; }
    }
}
