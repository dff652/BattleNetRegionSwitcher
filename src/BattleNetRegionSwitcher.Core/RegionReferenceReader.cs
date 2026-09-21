using System.Security;
using System.Text.Json;

namespace BattleNetRegionSwitcher.Core;

public enum DetectedRegion
{
    Unknown,
    China,
    International
}

public sealed record RegionReference(
    DetectedRegion Region,
    string Description,
    DateTimeOffset CheckedAt,
    DateTimeOffset? ConfigModifiedAt);

public sealed class RegionReferenceReader
{
    public const string ConfigFileName = "Battle.net.config";
    public const string LastLoginRegionFieldPath = "$.<16位十六进制配置键>.Services.LastLoginRegion";
    public const int MaxConfigBytes = 1024 * 1024;

    public RegionReference Read(string? configPath = null)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        var path = string.IsNullOrWhiteSpace(configPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Battle.net", ConfigFileName)
            : configPath;

        DateTimeOffset? modifiedAt = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaxConfigBytes)
                return Create(DetectedRegion.Unknown, checkedAt, null);

            modifiedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            ReadOnlyMemory<byte> bytes = ReadAtMost(stream, MaxConfigBytes);
            if (bytes.Span.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) bytes = bytes[3..];
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Create(DetectedRegion.Unknown, checkedAt, modifiedAt);

            var candidates = document.RootElement.EnumerateObject()
                .Where(property => IsConfigKey(property.Name)).ToArray();
            if (candidates.Length != 1 || candidates[0].Value.ValueKind != JsonValueKind.Object)
                return Create(DetectedRegion.Unknown, checkedAt, modifiedAt);

            var serviceProperties = candidates[0].Value.EnumerateObject().Where(p => p.NameEquals("Services")).ToArray();
            if (serviceProperties.Length != 1 || serviceProperties[0].Value.ValueKind != JsonValueKind.Object)
                return Create(DetectedRegion.Unknown, checkedAt, modifiedAt);
            var services = serviceProperties[0].Value;

            var regionProperties = services.EnumerateObject()
                .Where(property => property.NameEquals("LastLoginRegion")).ToArray();
            if (regionProperties.Length != 1 ||
                regionProperties[0].Value.ValueKind != JsonValueKind.String)
                return Create(DetectedRegion.Unknown, checkedAt, modifiedAt);

            var value = regionProperties[0].Value;

            var code = value.GetString();
            var region = code switch
            {
                "CN" => DetectedRegion.China,
                "TW" or "KR" or "US" or "EU" => DetectedRegion.International,
                _ => DetectedRegion.Unknown
            };
            return Create(region, checkedAt, modifiedAt);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or NotSupportedException or SecurityException)
        {
            return Create(DetectedRegion.Unknown, checkedAt, null);
        }
    }

    private static byte[] ReadAtMost(Stream stream, int maxBytes)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = stream.Read(chunk, 0, chunk.Length);
            if (read == 0) break;
            if (buffer.Length + read > maxBytes)
                throw new IOException("Configuration exceeds the supported size.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static bool IsConfigKey(string value)
    {
        if (value.Length != 16) return false;
        foreach (var character in value)
            if (!((character >= '0' && character <= '9') ||
                  (character >= 'a' && character <= 'f') ||
                  (character >= 'A' && character <= 'F')))
                return false;
        return true;
    }

    private static RegionReference Create(DetectedRegion region, DateTimeOffset checkedAt, DateTimeOffset? modifiedAt) =>
        new(region, region switch
        {
            DetectedRegion.China => "国服（配置记录，不代表当前登录）",
            DetectedRegion.International => "国际服（配置记录，不代表当前登录）",
            _ => "未知（未取得可用配置记录）"
        }, checkedAt, modifiedAt);
}
