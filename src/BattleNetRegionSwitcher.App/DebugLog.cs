namespace BattleNetRegionSwitcher.App;

internal sealed class DebugLog
{
    private const int MaxEntries = 300;
    private readonly string? directory;
    private readonly List<string> entries = [];
    internal IReadOnlyList<string> Entries => entries;
    internal bool LastWriteSucceeded { get; private set; } = true;

    internal DebugLog(string? directory)
    {
        this.directory = directory;
        if (directory is null) { LastWriteSucceeded = false; return; }
        try
        {
            var path = Path.Combine(directory, "debug.log");
            if (File.Exists(path) && new FileInfo(path).Length <= 512 * 1024)
                entries.AddRange(File.ReadLines(path).TakeLast(MaxEntries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal string Write(string level, string message)
    {
        // Callers supply app-generated diagnostics only. Never pass client logs, account data,
        // full paths, exception messages, environment variables or command lines here.
        if (message.Length > 2048) message = message[..2048] + "…";
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} [{level}] {message.Replace('\r', ' ').Replace('\n', ' ')}";
        entries.Add(line);
        if (entries.Count > MaxEntries) entries.RemoveAt(0);
        if (directory is null) { LastWriteSucceeded = false; return line; }
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "debug.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                File.Move(path, Path.Combine(directory, "debug.previous.log"), true);
            File.AppendAllText(path, line + Environment.NewLine);
            LastWriteSucceeded = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { LastWriteSucceeded = false; }
        return line;
    }
}
