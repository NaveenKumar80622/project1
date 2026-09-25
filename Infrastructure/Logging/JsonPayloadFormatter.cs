using System;
using System.Text.Json;
using PickNBook.Api.Models.Config;

namespace PickNBook.Api.Infrastructure.Logging
{
    public static class JsonPayloadFormatter
    {
        private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

        public static string Format(string? rawJson, PayloadLoggingMode mode, int maxChars = 500000)
        {
            if (string.IsNullOrWhiteSpace(rawJson)) return string.Empty;

            string formatted = rawJson;

            if (mode == PayloadLoggingMode.Beautified)
            {
                try
                {
                    using var doc = JsonDocument.Parse(rawJson);
                    formatted = JsonSerializer.Serialize(doc.RootElement, IndentedOptions);
                }
                catch
                {
                    // Fallback to raw string if not valid JSON
                    formatted = rawJson;
                }
            }

            // Apply truncation if exceeds maxChars limit
            if (maxChars > 0 && formatted.Length > maxChars)
            {
                return formatted.Substring(0, maxChars) + $"\n...[truncated, original size: {formatted.Length} chars]";
            }

            return formatted;
        }
    }
}
