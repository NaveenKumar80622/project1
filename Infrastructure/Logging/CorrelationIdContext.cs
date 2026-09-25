using System.Threading;

namespace PickNBook.Api.Infrastructure.Logging;

public static class CorrelationIdContext
{
    private static readonly AsyncLocal<string> _correlationId = new AsyncLocal<string>();

    public static string CorrelationId
    {
        get => _correlationId.Value ?? string.Empty;
        set => _correlationId.Value = value;
    }
}
