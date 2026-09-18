using System;

namespace PickNBook.Api.Helpers
{
    public static class BookingStatusResolver
    {
        public static string ResolveStatus(
            string? rawStatus,
            string? paymentStatus,
            string? fulfillmentStatus,
            DateTime journeyOrCheckOutDateUtc)
        {
            var status = (rawStatus ?? "").Trim().ToLower();
            var pStatus = (paymentStatus ?? "").Trim().ToLower();
            var fStatus = (fulfillmentStatus ?? "").Trim().ToLower();

            // 1. Cancelled
            if (status.Contains("cancel") || pStatus.Contains("cancel") || fStatus.Contains("cancel"))
            {
                return "Cancelled";
            }

            // 2. Expired (Hold timed out, payment failed, pending checkout, or supplier fulfillment failed)
            if (status.Contains("expire") || status.Contains("hold") || status.Contains("timeout") ||
                status.Contains("fail") || status.Contains("pend") ||
                pStatus.Contains("failed") || pStatus.Contains("expired") || pStatus.Contains("pend") ||
                fStatus.Contains("failed") || fStatus.Contains("rejected"))
            {
                return "Expired";
            }

            // 3. Booked (Success, Confirmed, Booked)
            if (status.Contains("success") || status.Contains("confirm") || status.Contains("book") || pStatus.Contains("success"))
            {
                return "Booked";
            }

            return "Booked";
        }
    }
}
