namespace PickNBook.Api.Models
{
    public class FlightAmendmentRequest
    {
        public int Id { get; set; }
        public int FlightReservationId { get; set; }
        public FlightReservation? FlightReservation { get; set; }
        public DateTime RequestDateUtc { get; set; }
        public string AmendmentStatus { get; set; } = "Pending";
        public string? SupplierRemark { get; set; }
        public string? CustomerRemark { get; set; }
        public string? AdminRemark { get; set; }

        // SRDV V8 Tracking
        public string? SrdvChangeRequestId { get; set; }
        public string? RequestType { get; set; }
    }
}
