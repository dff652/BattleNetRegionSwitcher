using BattleNetRegionSwitcher.Core;

var tests = new (string Name, Action Run)[]
{
    ("arguments and working directory", Arguments),
    ("invalid executable rejected", InvalidPath),
    ("existing process blocks", ExistingProcess),
    ("probe failure blocks", ProbeFailure),
    ("special path preserved", SpecialPath),
    ("invalid enum rejected", InvalidEnum),
    ("missing legal file rejected", MissingLegalFile),
    ("second probe blocks duplicate", SecondProbe),
    ("starter failure reported", StarterFailure),
    ("game and updater processes block", GameAndUpdater),
    ("cancelled request never starts", CancelledRequest),
    ("cancellation during checks prevents start", CancelledDuringProbe)
};
if (OperatingSystem.IsWindows())
{
    tests = [.. tests,
        ("shell path resolves Unicode temp directory", () => ShellPath(Path.GetTempPath())),
        ("shell path resolves AppData directory", () => ShellPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))),
        ("shell path rejects a missing directory", MissingShellDirectory),
        ("shell path rejects a regular file", ShellPathRejectsFile)];
}
var failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
}
return failed;

static string TempLauncher(string directory, string name = "Battle.net.exe")
{
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, name);
    File.WriteAllText(path, "test");
    return path;
}

static void Arguments()
{
    var starter = new FakeStarter();
    var path = TempLauncher(Path.Combine(Path.GetTempPath(), "BN Core Test"));
    var result = new ClientLauncher(new FakeProbe(), starter).Launch(path, LoginRegion.Asia);
    Assert(result.Started && starter.Argument == "--setregion=TW");
    Assert(starter.WorkingDirectory == Path.GetDirectoryName(Path.GetFullPath(path)));
}

static void InvalidPath() => Assert(!new ClientLauncher(new FakeProbe(), new FakeStarter()).Launch("C:\\nope\\other.exe", LoginRegion.China).Started);

static void ExistingProcess()
{
    var path = TempLauncher(Path.Combine(Path.GetTempPath(), "BN Core Existing"));
    var starter = new FakeStarter();
    var result = new ClientLauncher(new FakeProbe("Agent"), starter).Launch(path, LoginRegion.China);
    Assert(!result.Started && result.Message.Contains("Agent") && starter.Calls == 0);
}

static void ProbeFailure()
{
    var path = TempLauncher(Path.Combine(Path.GetTempPath(), "BN Core Probe"));
    var result = new ClientLauncher(new ThrowingProbe(), new FakeStarter()).Launch(path, LoginRegion.China);
    Assert(!result.Started && result.Message.Contains("无法可靠检查"));
}

static void SpecialPath()
{
    var starter = new FakeStarter();
    var path = TempLauncher(Path.Combine(Path.GetTempPath(), "BN & Region Test"), "Battle.net Launcher.exe");
    Assert(new ClientLauncher(new FakeProbe(), starter).Launch(path, LoginRegion.China).Started);
    Assert(starter.Argument == "--setregion=CN");
}

static void InvalidEnum()
{
    var starter = new FakeStarter();
    var path = TempLauncher(Path.Combine(Path.GetTempPath(), "BN Core Enum"));
    var result = new ClientLauncher(new FakeProbe(), starter).Launch(path, (LoginRegion)99);
    Assert(!result.Started && starter.Calls == 0);
}

static void MissingLegalFile()
{
    var path = Path.Combine(Path.GetTempPath(), "BN Core Missing", "Battle.net.exe");
    var starter = new FakeStarter();
    var result = new ClientLauncher(new FakeProbe(), starter).Launch(path, LoginRegion.China);
    Assert(!result.Started && starter.Calls == 0);
}

static void SecondProbe()
{
    var path = TempLauncher(Path.Combine(Path.GetTempPath(), "BN Core Race"));
    var starter = new FakeStarter();
    var result = new ClientLauncher(new SequenceProbe(Array.Empty<string>(), new[] { "Battle.net" }), starter).Launch(path, LoginRegion.China);
    Assert(!result.Started && result.Message.Contains("Battle.net") && starter.Calls == 0);
}

static void StarterFailure()
{
    var path = TempLauncher(Path.Combine(Path.GetTempPath(), "BN Core Start Failure"));
    var starter = new FakeStarter { ShouldStart = false };
    var result = new ClientLauncher(new FakeProbe(), starter).Launch(path, LoginRegion.China);
    Assert(!result.Started && result.Message.Contains("启动失败") && starter.Calls == 1);
}

static void GameAndUpdater()
{
    var path = TempLauncher(Path.Combine(Path.GetTempPath(), "BN Core Game"));
    var starter = new FakeStarter();
    var result = new ClientLauncher(new FakeProbe("SC2_x64", "temp_ABC123"), starter).Launch(path, LoginRegion.China);
    Assert(!result.Started && result.Message.Contains("SC2_x64") && result.Message.Contains("临时更新") && starter.Calls == 0);
}

static void CancelledRequest()
{
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    var starter = new FakeStarter();
    var result = new ClientLauncher(new ThrowingProbe(), starter).Launch("", LoginRegion.China, cancellation.Token);
    Assert(!result.Started && result.Message.Contains("取消") && starter.Calls == 0);
}

static void CancelledDuringProbe()
{
    using var cancellation = new CancellationTokenSource();
    var path = TempLauncher(Path.Combine(Path.GetTempPath(), "BN Core Cancel"));
    var starter = new FakeStarter();
    var result = new ClientLauncher(new CancellingProbe(cancellation), starter).Launch(path, LoginRegion.China, cancellation.Token);
    Assert(!result.Started && result.Message.Contains("取消") && starter.Calls == 0);
}

static void ShellPath(string parent)
{
    if (!OperatingSystem.IsWindows()) return;
    var directory = Path.Combine(parent, "BN 日志 & " + Guid.NewGuid().ToString("N"));
    var file = Path.Combine(directory, "probe.txt");
    var marker = Guid.NewGuid().ToString();
    Directory.CreateDirectory(directory);
    try
    {
        File.WriteAllText(file, marker);
        var resolved = ShellDirectory.ResolvePath(directory);
        Assert(Path.IsPathFullyQualified(resolved) && !resolved.StartsWith(@"\\?\"));
        Assert(File.ReadAllText(Path.Combine(resolved, "probe.txt")) == marker);
    }
    finally
    {
        File.Delete(file);
        Directory.Delete(directory); // Non-recursive: remove only this test's empty directory.
    }
}

static void MissingShellDirectory()
{
    if (!OperatingSystem.IsWindows()) return;
    var directory = Path.Combine(Path.GetTempPath(), "BN Missing " + Guid.NewGuid().ToString("N"));
    try { ShellDirectory.ResolvePath(directory); }
    catch (IOException) { Assert(!Directory.Exists(directory)); return; }
    throw new Exception("A missing directory must fail without creating it.");
}

static void ShellPathRejectsFile()
{
    if (!OperatingSystem.IsWindows()) return;
    var file = Path.GetTempFileName();
    try
    {
        try { ShellDirectory.ResolvePath(file); }
        catch (IOException) { return; }
        throw new Exception("A regular file must not be accepted as a log directory.");
    }
    finally { File.Delete(file); }
}

static void Assert(bool value) { if (!value) throw new Exception("assertion failed"); }
sealed class FakeProbe(params string[] names) : IProcessProbe { public IReadOnlyList<string> GetProcessNames() => names; }
sealed class SequenceProbe(params IReadOnlyList<string>[] values) : IProcessProbe
{
    private int _index;
    public IReadOnlyList<string> GetProcessNames() => values[Math.Min(_index++, values.Length - 1)];
}
sealed class ThrowingProbe : IProcessProbe { public IReadOnlyList<string> GetProcessNames() => throw new InvalidOperationException(); }
sealed class CancellingProbe(CancellationTokenSource source) : IProcessProbe
{
    public IReadOnlyList<string> GetProcessNames() { source.Cancel(); return Array.Empty<string>(); }
}
sealed class FakeStarter : IProcessStarter
{
    public bool ShouldStart { get; init; } = true;
    public int Calls { get; private set; }
    public string? Argument { get; private set; }
    public string? WorkingDirectory { get; private set; }
    public bool TryStart(string path, string argument, string workingDirectory) { Calls++; Argument = argument; WorkingDirectory = workingDirectory; return ShouldStart; }
}
