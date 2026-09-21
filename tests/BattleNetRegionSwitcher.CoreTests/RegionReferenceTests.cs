using BattleNetRegionSwitcher.Core;
using System.Text;
using System.Text.Json;

namespace BattleNetRegionSwitcher.CoreTests;

public static class RegionReferenceTests
{
    public static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("CN maps to China reference", () => WithConfig(Config("CN"), result => Assert(result.Region == DetectedRegion.China)));
        yield return ("TW KR US EU map to International", InternationalCodes);
        yield return ("stale record keeps reference and timestamp", StaleRecord);
        yield return ("unknown and malformed values", UnknownValues);
        yield return ("invalid roots and keys", InvalidRootsAndKeys);
        yield return ("multiple candidates are unknown", () => WithConfig(Config("CN", "abcdef0123456789"), result => AssertUnknown(result)));
        yield return ("duplicate region fields are unknown", () => WithConfig("{\"1234567890abcdef\":{\"Services\":{\"LastLoginRegion\":\"CN\",\"LastLoginRegion\":\"TW\"}}}", result => AssertUnknown(result)));
        yield return ("duplicate service objects are unknown", () => WithConfig("{\"1234567890abcdef\":{\"Services\":{\"LastLoginRegion\":\"CN\"},\"Services\":{\"LastLoginRegion\":\"TW\"}}}", AssertUnknown));
        yield return ("BOM is accepted", () => WithConfig("\uFEFF" + Config("CN"), result => Assert(result.Region == DetectedRegion.China)));
        yield return ("locked file is unknown", LockedFile);
        yield return ("oversized file is unknown", OversizedFile);
        yield return ("description contains no account data", () => WithConfig(Config("CN", email: "person@example.test", token: "secret"), result =>
        {
            Assert(result.Description == "国服（配置记录，不代表当前登录）");
            Assert(!result.Description.Contains("person@example.test") && !result.Description.Contains("secret"));
        }));
    }

    private static void InternationalCodes()
    {
        foreach (var code in new[] { "TW", "KR", "US", "EU" })
            WithConfig(Config(code), result => Assert(result.Region == DetectedRegion.International));
    }

    private static void StaleRecord()
    {
        var old = DateTimeOffset.UtcNow.AddYears(-1);
        WithConfig(Config("CN"), result =>
        {
            Assert(result.Region == DetectedRegion.China);
            Assert(result.ConfigModifiedAt is not null && Math.Abs((result.ConfigModifiedAt.Value - old).TotalDays) < 2);
        }, old);
    }

    private static void UnknownValues()
    {
        WithConfig(Config("XX"), AssertUnknown);
        WithConfig("{\"1234567890abcdef\":{\"Services\":{\"LastLoginRegion\":123}}}", AssertUnknown);
        WithConfig("{\"1234567890abcdef\":{\"Services\":{}}}", AssertUnknown);
        WithConfig("{\"1234567890abcdef\":", AssertUnknown);
        WithMissing(AssertUnknown);
    }

    private static void InvalidRootsAndKeys()
    {
        foreach (var json in new[] { "null", "[]", "\"text\"", "{\"not-a-config-key\":{\"Services\":{\"LastLoginRegion\":\"CN\"}}}", "{\"LastLoginRegion\":\"CN\",\"1234567890abcdef\":{\"Nested\":{\"LastLoginRegion\":\"CN\"}}}" })
            WithConfig(json, AssertUnknown);
        WithConfig("{\"1234567890abcdef\":{\"Services\":\"wrong\"}}", AssertUnknown);
    }

    private static string Config(string code, string? secondKey = null, string? email = null, string? token = null)
    {
        var services = new Dictionary<string, object?> { ["LastLoginRegion"] = code };
        if (email is not null) services["Email"] = email;
        if (token is not null) services["Token"] = token;
        var root = new Dictionary<string, object?>
        {
            ["1234567890abcdef"] = new Dictionary<string, object?> { ["Services"] = services }
        };
        if (secondKey is not null)
            root[secondKey] = new Dictionary<string, object?> { ["Services"] = new Dictionary<string, object?> { ["LastLoginRegion"] = "CN" } };
        var json = JsonSerializer.Serialize(root);
        using var parsed = JsonDocument.Parse(json);
        Assert(parsed.RootElement.ValueKind == JsonValueKind.Object);
        return json;
    }

    private static void LockedFile()
    {
        var (directory, path) = NewPath();
        try
        {
            File.WriteAllText(path, Config("CN"));
            using var lockHandle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            AssertUnknown(new RegionReferenceReader().Read(path));
        }
        finally { Cleanup(directory, path); }
    }

    private static void OversizedFile()
    {
        var (directory, path) = NewPath();
        try
        {
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes(Config("CN") + new string('x', RegionReferenceReader.MaxConfigBytes)));
            AssertUnknown(new RegionReferenceReader().Read(path));
        }
        finally { Cleanup(directory, path); }
    }

    private static void WithConfig(string json, Action<RegionReference> assertion, DateTimeOffset? modifiedAt = null)
    {
        var (directory, path) = NewPath();
        try
        {
            File.WriteAllText(path, json, new UTF8Encoding(false));
            if (modifiedAt is not null) File.SetLastWriteTimeUtc(path, modifiedAt.Value.UtcDateTime);
            assertion(new RegionReferenceReader().Read(path));
        }
        finally { Cleanup(directory, path); }
    }

    private static void WithMissing(Action<RegionReference> assertion)
    {
        var (directory, path) = NewPath();
        try { assertion(new RegionReferenceReader().Read(path)); }
        finally { Cleanup(directory, path); }
    }

    private static (string Directory, string Path) NewPath()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BattleNetRegionReferenceTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return (directory, System.IO.Path.Combine(directory, RegionReferenceReader.ConfigFileName));
    }

    private static void Cleanup(string directory, string path)
    {
        try { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(directory)) Directory.Delete(directory); }
        catch { }
    }

    private static void AssertUnknown(RegionReference result) => Assert(result.Region == DetectedRegion.Unknown);
    private static void Assert(bool condition) { if (!condition) throw new Exception("assertion failed"); }
}
