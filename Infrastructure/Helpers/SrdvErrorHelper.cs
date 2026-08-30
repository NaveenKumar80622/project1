namespace PickNBook.Api.Infrastructure.Helpers
{
    public static class SrdvErrorHelper
    {
        public static string GetErrorMessage(string? errorCode)
        {
            return errorCode switch
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
                "1100" => "Invalid JSON Format",
                _ => "An unknown error occurred with the flight supplier"
            };
        }
    }
}
