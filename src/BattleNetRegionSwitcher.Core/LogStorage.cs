namespace BattleNetRegionSwitcher.Core;

public sealed record LogStorage(string? DirectoryPath, bool UsesFallback)
{
    public static LogStorage Select(string applicationDirectory, string userDataDirectory)
    {
        var portable = Path.Combine(applicationDirectory, "logs");
        if (CanWrite(portable)) return new(portable, false);

        var fallback = Path.Combine(userDataDirectory, "logs");
        return CanWrite(fallback) ? new(fallback, true) : new(null, true);
    }

    private static bool CanWrite(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probePath = Path.Combine(directory, ".write-check-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var probe = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                probe.WriteByte(0);
                probe.Flush();
            }
            // A writable directory can still contain an unwritable existing log.
            var currentLog = Path.Combine(directory, "debug.log");
            if (File.Exists(currentLog))
            {
                using var existing = new FileStream(currentLog, FileMode.Open, FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
