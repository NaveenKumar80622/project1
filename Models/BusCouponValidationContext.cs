using System;
using System.Collections.Generic;

namespace PickNBook.Api.Models
{
    public class BusCouponValidationContext
    {
        public string? OperatorName { get; set; }

        public string? BusType { get; set; }

        public string? SourceCity { get; set; }

        public string? DestinationCity { get; set; }

        public DateTime? TravelDate { get; set; }

        public DayOfWeek? DayOfWeek { get; set; }

        public decimal BookingFare { get; set; }

        public List<BusCouponSeatContext> SelectedSeats { get; set; } = new();
    }

    public class BusCouponSeatContext
    {
        public string? SeatName { get; set; }

        public string? SeatType { get; set; }

        public decimal Fare { get; set; }
    }

    public class BusSearchItemContext
    {
        public string TraceId { get; set; } = string.Empty;
        public string ResultIndex { get; set; } = string.Empty;
        public int SrdvIndex { get; set; }
        public string OperatorName { get; set; } = string.Empty;
        public string BusType { get; set; } = string.Empty;
        public string FromCity { get; set; } = string.Empty;
        public string ToCity { get; set; } = string.Empty;
        public string DepartureTime { get; set; } = string.Empty;
        public string ArrivalTime { get; set; } = string.Empty;
        public string DepartDate { get; set; } = string.Empty;
    }

    public class BusSeatLayoutItemContext
    {
        public string SeatName { get; set; } = string.Empty;
        public string SeatType { get; set; } = string.Empty;
        public decimal BaseFare { get; set; }
        public decimal SeatFare { get; set; }
        public decimal PublishedFare { get; set; }
    }
}
