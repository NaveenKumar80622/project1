namespace PickNBook.Api.Models.Payments
{
    /// <summary>
    /// String constants for payment status values.
    /// Follows the project's string-based status convention (e.g., BusReservation.Status = "Booked").
    /// </summary>
    public static class PaymentStatus
    {
        public const string Created = "Created";
        public const string Pending = "Pending";
        public const string Success = "Success";
        public const string Failed = "Failed";
        public const string Cancelled = "Cancelled";
        public const string Expired = "Expired";
    }
}
