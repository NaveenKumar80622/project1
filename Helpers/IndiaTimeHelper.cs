using System;

namespace PickNBook.Api.Helpers;

/// <summary>
/// Authoritative helper for Indian Standard Time (IST, UTC+05:30) calculations,
/// supplier payload normalization, and presentation formatting.
/// </summary>
public static class IndiaTimeHelper
{
    public static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);

    /// <summary>
    /// Parses an IST date/time string from a supplier (e.g., "2026-10-14 21:10", "2026-10-14T21:10:00")
    /// into a genuine UTC DateTime suitable for storage in MySQL.
    /// </summary>
    public static DateTime ToUtcFromIst(string? istString, DateTime? fallbackUtc = null)
    {
        if (string.IsNullOrWhiteSpace(istString))
            return fallbackUtc ?? DateTime.UtcNow;

        if (DateTime.TryParse(istString.Trim(), out var parsed))
        {
            return ToUtcFromIst(parsed);
        }

        return fallbackUtc ?? DateTime.UtcNow;
    }

    /// <summary>
    /// Treats the provided DateTime as Indian Standard Time and converts it to UTC.
    /// </summary>
    public static DateTime ToUtcFromIst(DateTime istDateTime)
    {
        if (istDateTime == default) return default;
        // Strip any existing Kind and subtract 5.5 hours to yield true UTC
        var unspecified = DateTime.SpecifyKind(istDateTime, DateTimeKind.Unspecified);
        var utc = unspecified - IndiaOffset;
        return DateTime.SpecifyKind(utc, DateTimeKind.Utc);
    }

    /// <summary>
    /// Converts a UTC DateTime (or unspecified DateTime from MySQL stored as UTC) to IST.
    /// </summary>
    public static DateTime ToIst(DateTime dt)
    {
        if (dt == default) return default;

        DateTime utc;
        if (dt.Kind == DateTimeKind.Utc)
        {
            utc = dt;
        }
        else if (dt.Kind == DateTimeKind.Local)
        {
            utc = dt.ToUniversalTime();
        }
        else
        {
            // Unspecified: treat as UTC from database
            utc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }

        return utc + IndiaOffset;
    }

    /// <summary>
    /// Nullable overload for ToIst.
    /// </summary>
    public static DateTime? ToIst(DateTime? dt)
    {
        if (!dt.HasValue || dt.Value == default) return null;
        return ToIst(dt.Value);
    }

    /// <summary>
    /// Formats a UTC DateTime into an IST string representation.
    /// </summary>
    public static string FormatIst(DateTime dt, string format = "dd MMM yyyy, hh:mm tt")
    {
        if (dt == default) return string.Empty;
        return ToIst(dt).ToString(format);
    }

    /// <summary>
    /// Nullable overload for FormatIst.
    /// </summary>
    public static string FormatIst(DateTime? dt, string format = "dd MMM yyyy, hh:mm tt")
    {
        if (!dt.HasValue || dt.Value == default) return string.Empty;
        return ToIst(dt.Value).ToString(format);
    }

    /// <summary>
    /// Returns the current instant in Indian Standard Time.
    /// </summary>
    public static DateTime UtcNowIst()
    {
        return DateTime.UtcNow + IndiaOffset;
    }
}
