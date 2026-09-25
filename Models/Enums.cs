namespace PickNBook.Api.Models
{
    /// <summary>
    /// Type of discount applied by flight promotions (Flat amount or Percentage).
    /// </summary>
    public enum FlightDiscountType
    {
        Flat,
        Percentage
    }

    /// <summary>
    /// Type of condition used to evaluate flight promotion eligibility.
    /// </summary>
    public enum FlightConditionType
    {
        TravelClass,
        Airline,
        Route,
        TripType,
        MinimumFare,
        PassengerCount,
        AdvanceBookingDays
    }

    /// <summary>
    /// Type of markup applied (Flat amount or Percentage).
    /// </summary>
    public enum FlightMarkupType
    {
        Flat,
        Percentage
    }

    /// <summary>
    /// Type of flight journey (OneWay or RoundTrip).
    /// </summary>
    public enum TripType
    {
        OneWay,
        RoundTrip,
        MultiCity
    }
}
