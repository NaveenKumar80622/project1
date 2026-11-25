namespace PickNBook.Api.Infrastructure.Helpers
{
    public static class SrdvErrorHelper
    {
        public static string GetErrorMessage(string? errorCode, string? defaultSupplierMessage = null)
        {
            var mapped = errorCode switch
            {
                "0" => "Request executed successfully",
                "1" => "Supplier Log not found",
                "2" => "Booking Failed from supplier / Provided Segment Not available on supplier",
                "4" => "Invalid URL",
                "10" => "Your Booking is in process, Please wait for some time or contact support team",
                "100" => "Supplier Ticket Id, Selected Result Index not found",
                "900" => "IP not whitelisted",
                "997" => "User Name or Password mismatch",
                "998" => "API not activated, Please contact your Air Representative or Support Team",
                "999" => "Invalid Api-Token / Account Not Activated",
                "1003" => "Invalid Request",
                "1005" => "Forbidden",
                "1100" => "Invalid JSON Format",
                "2008" => "Invalid API Token",
                "7201" => "Suggested location/hotel ID has expired (older than 30 days). Please fetch fresh suggestions.",
                "13000" => "HotelCodes mode is not compatible with this Hotel route.",
                "13005" => "Location suggestions are not available for this Hotel route.",
                "13007" => "Upstream hotel list could not be read. Please try again.",
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(mapped))
            {
                return mapped;
            }

            if (!string.IsNullOrWhiteSpace(defaultSupplierMessage))
            {
                return defaultSupplierMessage;
            }

            return "An unknown error occurred with the flight supplier";
        }
    }
}
