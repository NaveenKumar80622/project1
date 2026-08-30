using Microsoft.Extensions.Caching.Memory;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace PickNBook.Api.Services;

public interface IGeoIpService
{
    Task<string> ResolveRegionAsync(string? ipAddress, CancellationToken cancellationToken = default);
}

public class GeoIpService : IGeoIpService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;

    public GeoIpService(HttpClient httpClient, IMemoryCache cache)
    {
        _httpClient = httpClient;
        _cache = cache;
        _httpClient.Timeout = TimeSpan.FromSeconds(3);
    }

    public async Task<string> ResolveRegionAsync(string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return "Unknown";
        }

        if (!IPAddress.TryParse(ipAddress, out var ip))
        {
            return "Unknown";
        }

        if (IPAddress.IsLoopback(ip))
        {
            return "Localhost";
        }

        if (IsPrivate(ip))
        {
            return "Private Network";
        }

        var cacheKey = $"geo-region:{ipAddress}";
        if (_cache.TryGetValue(cacheKey, out string? cachedRegion) &&
            !string.IsNullOrWhiteSpace(cachedRegion))
        {
            return cachedRegion;
        }

        var resolvedRegion = await ResolvePublicIpRegionAsync(ipAddress, cancellationToken);
        _cache.Set(cacheKey, resolvedRegion, TimeSpan.FromHours(6));
        return resolvedRegion;
    }

    private async Task<string> ResolvePublicIpRegionAsync(string ipAddress, CancellationToken cancellationToken)
    {
        try
        {
            var encodedIp = Uri.EscapeDataString(ipAddress);
            var endpoint = $"https://ipwho.is/{encodedIp}";

            using var response = await _httpClient.GetAsync(endpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return "Public Network";
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                return "Public Network";
            }

            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;

            if (root.TryGetProperty("success", out var successProperty) &&
                successProperty.ValueKind == JsonValueKind.False)
            {
                return "Public Network";
            }

            var city = ReadString(root, "city");
            var region = ReadString(root, "region");
            var country = ReadString(root, "country");

            var parts = new[] { city, region, country }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (parts.Length == 0)
            {
                return "Public Network";
            }

            return string.Join(", ", parts);
        }
        catch
        {
            return "Public Network";
        }
    }

    private static bool IsPrivate(IPAddress ipAddress)
    {
        if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ipAddress.GetAddressBytes();

            if (bytes[0] == 10)
            {
                return true;
            }

            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            return false;
        }

        if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return ipAddress.IsIPv6LinkLocal || ipAddress.IsIPv6SiteLocal;
        }

        return false;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
