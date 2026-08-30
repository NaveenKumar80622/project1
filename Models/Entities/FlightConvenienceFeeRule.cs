namespace PickNBook.Api.Models
{
    /// <summary>
    /// Represents convenience fee rules depending on the trip type.
    /// </summary>
    public class FlightConvenienceFeeRule
    {
        public int Id { get; set; }
        public TripType TripType { get; set; }
        public string FeeType { get; set; } = "Flat"; // Flat or Percentage
        public decimal FeeValue { get; set; }
        public bool IsActive { get; set; }
    }
}
