using System.Text.Json;

namespace BattleNetRegionSwitcher.App;

internal sealed record ToolSettings(string LauncherPath = "", string LastRequestedRegion = "");

internal sealed class SettingsStore
{
    internal string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BattleNetRegionSwitcher");

    internal ToolSettings Load()
    {
        try
        {
            var path = Path.Combine(DirectoryPath, "settings.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<ToolSettings>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return new(); }
    }

    internal bool Save(ToolSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var target = Path.Combine(DirectoryPath, "settings.json");
            var temporary = target + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, target, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

}
