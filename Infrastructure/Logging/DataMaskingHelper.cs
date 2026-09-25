using System.Text.RegularExpressions;

namespace PickNBook.Api.Infrastructure.Logging;

public static class DataMaskingHelper
{
    private static readonly string[] SensitiveKeys = { "password", "token", "cvv", "cardnumber", "card_number", "creditcard", "authorization" };

    public static string MaskPayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return payload;

        // Masking logic using regex to replace values of sensitive JSON keys
        foreach (var key in SensitiveKeys)
        {
            var pattern = $@"""({key})""\s*:\s*""([^""]*)""";
            payload = Regex.Replace(payload, pattern, @"""$1"": ""***MASKED***""", RegexOptions.IgnoreCase);
            
            var patternNum = $@"""({key})""\s*:\s*([0-9\.]+)";
            payload = Regex.Replace(payload, patternNum, @"""$1"": ""***MASKED***""", RegexOptions.IgnoreCase);
        }

        // Truncate if too large to avoid memory issues (e.g., > 10KB)
        if (payload.Length > 10000)
        {
            payload = payload.Substring(0, 10000) + "... [TRUNCATED]";
        }

        return payload;
    }
}
