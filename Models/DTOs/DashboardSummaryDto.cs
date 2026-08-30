namespace PickNBook.Api.Models.DTOs;

public class DashboardSummaryDto
{
    public int TotalBookings { get; set; }
    public decimal CompletionRatePercent { get; set; }
    public DashboardPendingActionsDto PendingActions { get; set; } = new();
    public DashboardRevenueSnapshotDto RevenueSnapshot { get; set; } = new();
    public DashboardBookingBreakdownDto FlightBookings { get; set; } = new();
    public DashboardBookingBreakdownDto BusBookings { get; set; } = new();
    public List<DashboardRecentUpdateDto> RecentUpdates { get; set; } = [];
    public DashboardRecentUpdateCounterDto RecentUpdateCounters { get; set; } = new();
    public List<DashboardTopRouteDto> TopRoutes { get; set; } = [];
}

public class DashboardPendingActionsDto
{
    public int Cancellations { get; set; }
    public int Deposits { get; set; }
    public int TravelerUpdates { get; set; }
    public int Total { get; set; }
}

public class DashboardRevenueSnapshotDto
{
    public decimal TotalRevenueInr { get; set; }
    public decimal TotalSavingsInr { get; set; }
    public decimal CancelledValueInr { get; set; }
}

public class DashboardBookingBreakdownDto
{
    public int Completed { get; set; }
    public int Upcoming { get; set; }
    public int Cancelled { get; set; }
    public int Total { get; set; }
}

public class DashboardRecentUpdateDto
{
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}

public class DashboardRecentUpdateCounterDto
{
    public int BookingUpdates { get; set; }
    public int CancellationUpdates { get; set; }
    public int TravelerUpdates { get; set; }
    public int WalletPaymentUpdates { get; set; }
    public int BankAddUpdates { get; set; }
}

public class DashboardTopRouteDto
{
    public string TripType { get; set; } = string.Empty;
    public string FromCity { get; set; } = string.Empty;
    public string ToCity { get; set; } = string.Empty;
    public long SearchCount { get; set; }
    public long BookingCount { get; set; }
    public long Score { get; set; }
}
