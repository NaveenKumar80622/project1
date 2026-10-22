
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PickNBook.Api.Helpers;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using QRCoder;

namespace PickNBook.Api.Services;

public class TicketPdfService : ITicketPdfService
{
    public List<(string FileName, byte[] Content)> GenerateFlightTicketPdf(SendFlightTicketEmailRequest request)
    {
        var result = new List<(string FileName, byte[] Content)>();

        var titleFont = new XFont("Arial", 18, XFontStyle.Bold);
        var headerFont = new XFont("Arial", 11, XFontStyle.Bold);
        var valueFont = new XFont("Arial", 11, XFontStyle.Regular);
        var smallFont = new XFont("Arial", 9, XFontStyle.Regular);

        var passengersList = request.Passengers != null && request.Passengers.Any() 
            ? request.Passengers 
            : new List<FlightPassengerTicketDto> { new FlightPassengerTicketDto { FullName = request.PassengerName, SeatNumber = request.SeatNumber, Status = "Booked" } };

        foreach (var px in passengersList)
        {
            var document = new PdfDocument();
            document.Info.Title = $"Flight Ticket - {request.BookingReference}";

            var page = document.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            double left = 40;
            double width = page.Width - 80;
            double y = 48;

            string titleText = string.IsNullOrEmpty(request.AgentCompanyName) 
                ? "Pick&book Flight Ticket" 
                : $"{request.AgentCompanyName} Flight Ticket";

            gfx.DrawString(
                titleText,
                titleFont,
                XBrushes.DarkBlue,
                new XRect(left, y, width, 30),
                XStringFormats.TopLeft);

            if (!string.IsNullOrEmpty(request.AgentLogoUrl))
            {
                var webRootPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot");
                var physicalPath = System.IO.Path.Combine(webRootPath, request.AgentLogoUrl.TrimStart('/'));
                if (System.IO.File.Exists(physicalPath))
                {
                    try
                    {
                        using var logoImage = XImage.FromFile(physicalPath);
                        double logoWidth = 80;
                        double logoHeight = 40;
                        double logoLeft = page.Width - 40 - logoWidth;
                        gfx.DrawImage(logoImage, logoLeft, 35, logoWidth, logoHeight);
                    }
                    catch
                    {
                        // Resilient fallback
                    }
                }
            }

            y += 38;

            gfx.DrawRectangle(XPens.Gray, left, y, width, 1);
            y += 16;

            DrawRow(gfx, headerFont, valueFont, left, ref y, "Booking Ref", request.BookingReference);
            y += 10;
            gfx.DrawLine(XPens.LightGray, left, y, width, y);
            y += 15;

            if (request.Segments != null && request.Segments.Any())
            {
                int legIndex = 1;
                foreach (var seg in request.Segments)
                {
                    bool isCancelled = (px.Status == "Cancelled" || seg.Status == "Cancelled");
                    var titleBrush = isCancelled ? XBrushes.Red : XBrushes.DarkBlue;
                    var textBrush = isCancelled ? XBrushes.DarkRed : XBrushes.Black;
                    
                    var legTitle = $"Flight Leg {legIndex}: {seg.FromCity} -> {seg.ToCity}";
                    if (isCancelled) legTitle += " **CANCELLED**";

                    gfx.DrawString(legTitle, headerFont, titleBrush, left, y);
                    y += 21;
                    
                    var pxName = px.FullName;
                    if (px.Status == "Cancelled") pxName += " (CANCELLED)";
                    
                    DrawRow(gfx, headerFont, valueFont, left, ref y, $"Passenger ({px.PassengerType ?? "Adult"})", pxName, textBrush);
                    DrawRow(gfx, headerFont, valueFont, left, ref y, "Airline", seg.Airline, textBrush);
                    DrawRow(gfx, headerFont, valueFont, left, ref y, "Flight Number", seg.FlightNumber, textBrush);
                    DrawRow(gfx, headerFont, valueFont, left, ref y, "Departure", seg.DepartureTime.ToString("dd MMM yyyy, hh:mm tt"), textBrush);
                    DrawRow(gfx, headerFont, valueFont, left, ref y, "Arrival", seg.ArrivalTime.ToString("dd MMM yyyy, hh:mm tt"), textBrush);
                    
                    if (!string.IsNullOrWhiteSpace(seg.Pnr))
                    {
                        DrawRow(gfx, headerFont, valueFont, left, ref y, "PNR", seg.Pnr, textBrush);
                    }
                    else if (!string.IsNullOrWhiteSpace(request.Pnr))
                    {
                        DrawRow(gfx, headerFont, valueFont, left, ref y, "PNR", request.Pnr, textBrush);
                    }

                    y += 10;
                    gfx.DrawLine(XPens.LightGray, left, y, width, y);
                    y += 15;
                    
                    if (y > page.Height - 80)
                    {
                        gfx.Dispose();
                        page = document.AddPage();
                        gfx = XGraphics.FromPdfPage(page);
                        y = 48;
                    }
                    
                    legIndex++;
                }
            }
            else
            {
                // Fallback for single leg
                bool isCancelled = px.Status == "Cancelled";
                var titleBrush = isCancelled ? XBrushes.Red : XBrushes.DarkBlue;
                var textBrush = isCancelled ? XBrushes.DarkRed : XBrushes.Black;
                
                var legTitle = $"Flight: {request.Origin} -> {request.Destination}";
                if (isCancelled) legTitle += " **CANCELLED**";

                gfx.DrawString(legTitle, headerFont, titleBrush, left, y);
                y += 21;
                
                var pxName = px.FullName;
                if (px.Status == "Cancelled") pxName += " (CANCELLED)";
                
                DrawRow(gfx, headerFont, valueFont, left, ref y, "Passenger", pxName, textBrush);
                DrawRow(gfx, headerFont, valueFont, left, ref y, "Airline", request.Airline, textBrush);
                DrawRow(gfx, headerFont, valueFont, left, ref y, "Departure", request.DepartureTime.ToString("dd MMM yyyy, hh:mm tt"), textBrush);
                DrawRow(gfx, headerFont, valueFont, left, ref y, "Arrival", request.ArrivalTime.ToString("dd MMM yyyy, hh:mm tt"), textBrush);
                
                if (!string.IsNullOrWhiteSpace(request.Pnr))
                {
                    DrawRow(gfx, headerFont, valueFont, left, ref y, "PNR", request.Pnr, textBrush);
                }

                y += 10;
                gfx.DrawLine(XPens.LightGray, left, y, width, y);
                y += 15;
                
                if (y > page.Height - 80)
                {
                    gfx.Dispose();
                    page = document.AddPage();
                    gfx = XGraphics.FromPdfPage(page);
                    y = 48;
                }
            }

            DrawRow(gfx, headerFont, valueFont, left, ref y, "Fare", $"{request.Price:0.00} {request.Currency}");

            y += 20;

            gfx.DrawRectangle(XPens.Gray, left, y, width, 1);

            y += 14;

            gfx.DrawString(
                "Please carry a valid government ID proof while traveling. This is a system generated ticket.",
                smallFont,
                XBrushes.DarkSlateGray,
                new XRect(left, y, width, 40),
                XStringFormats.TopLeft);

            gfx.Dispose();
            using var stream = new MemoryStream();
            document.Save(stream, false);
            document.Dispose();
            
            var safeName = string.Join("_", (px.FullName ?? "Passenger").Split(System.IO.Path.GetInvalidFileNameChars()));
            result.Add(($"Ticket-{safeName}.pdf", stream.ToArray()));
        }

        return result;
    }

    private static void DrawRow(
        XGraphics gfx,
        XFont headerFont,
        XFont valueFont,
        double left,
        ref double y,
        string label,
        string value,
        XSolidBrush brush = null)
    {
        brush ??= XBrushes.Black;
        
        gfx.DrawString(
            label,
            headerFont,
            brush,
            new XRect(left, y, 170, 18),
            XStringFormats.TopLeft);

        gfx.DrawString(
            value,
            valueFont,
            brush,
            new XRect(left + 175, y, 340, 18),
            XStringFormats.TopLeft);

        y += 21;
    }

    public byte[] GenerateBusTicketPdf(SendBusTicketEmailRequest request, bool isCancelled = false, decimal refundAmount = 0m)
    {
        using var document = new PdfDocument();
        document.Info.Title = isCancelled ? $"Bus Ticket (CANCELLED) - {request.BookingReference}" : $"Bus Ticket - {request.BookingReference}";

        BuildBusTicketPage(document, request, isCancelled, refundAmount);

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    private static void BuildBusTicketPage(
        PdfDocument document,
        SendBusTicketEmailRequest req,
        bool isCancelled = false,
        decimal refundAmount = 0m)
    {
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(595); // A4 width
        page.Height = XUnit.FromPoint(842); // A4 height

        using var gfx = XGraphics.FromPdfPage(page);

        // Brushes & Pens
        var darkBrush = new XSolidBrush(XColor.FromArgb(15, 23, 42));       // #0f172a
        var slateBrush = new XSolidBrush(XColor.FromArgb(51, 65, 85));      // #334155
        var grayBrush = new XSolidBrush(XColor.FromArgb(100, 116, 139));    // #64748b
        var redBrush = new XSolidBrush(XColor.FromArgb(220, 38, 38));       // #dc2626
        var greenTextBrush = new XSolidBrush(XColor.FromArgb(21, 128, 61)); // #15803d
        var greenBgBrush = new XSolidBrush(XColor.FromArgb(220, 252, 231)); // #dcfce7
        var redTextBrush = new XSolidBrush(XColor.FromArgb(185, 28, 28));   // #b91c1c
        var redBgBrush = new XSolidBrush(XColor.FromArgb(254, 226, 226));   // #fee2e2
        var cardBgBrush = new XSolidBrush(XColor.FromArgb(248, 250, 252));  // #f8fafc
        var amberBgBrush = new XSolidBrush(XColor.FromArgb(255, 251, 235)); // #fffbeb
        var amberTextBrush = new XSolidBrush(XColor.FromArgb(146, 64, 14)); // #92400e
        var amberBorderPen = new XPen(XColor.FromArgb(254, 243, 199), 1.0); // #fef3c7
        var borderPen = new XPen(XColor.FromArgb(226, 232, 240), 1.0);      // #e2e8f0
        var dashedPen = new XPen(XColor.FromArgb(203, 213, 225), 1.0) { DashStyle = XDashStyle.Dash };

        double left = 28;
        double contentW = page.Width - 56; // 539 pt
        double right = left + contentW;
        double y = 28;
        double startY = y;

        // =====================================================================
        // 1. TICKET HEADER & META BAR
        // =====================================================================
        // Red top stripe
        gfx.DrawRectangle(redBrush, left, y, contentW, 4);
        y += 4;

        double headerH = 68;
        // White header bg
        gfx.DrawRectangle(XBrushes.White, left, y, contentW, headerH);
        gfx.DrawLine(borderPen, left, y + headerH, right, y + headerH);

        // Logo
        string[] candidateLogoPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "picknbook-logo.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "picknbook-logo.png"),
            Path.Combine(AppContext.BaseDirectory, "picknbook-logo.png")
        };
        string? foundLogo = candidateLogoPaths.FirstOrDefault(File.Exists);
        if (foundLogo != null)
        {
            try
            {
                using var logoImg = XImage.FromFile(foundLogo);
                gfx.DrawImage(logoImg, left + 14, y + 8, 112, 22);
            }
            catch { }
        }

        // Title: Bus Ticket
        string ticketTitle = isCancelled ? "BUS TICKET CANCELLATION" : "BUS TICKET";
        gfx.DrawString(ticketTitle, new XFont("Arial", 11, XFontStyle.Bold), redBrush, left + 14, y + 46);

        // Booking Date
        var datePrefix = isCancelled ? "Cancelled on " : "Booked on ";
        var effectiveDate = isCancelled && req.CancelledAtUtc.HasValue 
            ? req.CancelledAtUtc.Value 
            : (req.BookedAtUtc != default ? req.BookedAtUtc : DateTime.UtcNow);
        var bookingDateStr = req.DepartureTime != default 
            ? $"{datePrefix}{effectiveDate:dd MMM yyyy, hh:mm tt}" 
            : "";
        gfx.DrawString(bookingDateStr, new XFont("Arial", 8, XFontStyle.Regular), grayBrush, left + 14, y + 58);

        // Right side: Booking ID, PNR, Status (Cleanly bounded to avoid crossing right border)
        double metaBoxW = 230;
        double metaLeft = right - metaBoxW - 14;

        gfx.DrawString("Booking ID:", new XFont("Arial", 8.5, XFontStyle.Regular), grayBrush, metaLeft, y + 16);
        gfx.DrawString(req.BookingReference, new XFont("Arial", 9.5, XFontStyle.Bold), redBrush, new XRect(metaLeft, y + 6, metaBoxW, 14), XStringFormats.TopRight);

        gfx.DrawString("Bus Partner PNR:", new XFont("Arial", 8.5, XFontStyle.Regular), grayBrush, metaLeft, y + 30);
        gfx.DrawString(req.Pnr, new XFont("Arial", 9, XFontStyle.Bold), darkBrush, new XRect(metaLeft, y + 20, metaBoxW, 14), XStringFormats.TopRight);

        string statusBadge = isCancelled ? "CANCELLED" : "CONFIRMED";
        XBrush badgeBg = isCancelled ? redBgBrush : greenBgBrush;
        XBrush badgeTextBrush = isCancelled ? redTextBrush : greenTextBrush;
        gfx.DrawRoundedRectangle(badgeBg, right - 95, y + 40, 75, 16, 3, 3);
        gfx.DrawString(statusBadge, new XFont("Arial", 8, XFontStyle.Bold), badgeTextBrush, new XRect(right - 95, y + 40, 75, 16), XStringFormats.Center);

        y += headerH;

        // =====================================================================
        // 2. OPERATOR & SERVICE DETAILS
        // =====================================================================
        double sec2H = 48;
        gfx.DrawRectangle(cardBgBrush, left, y, contentW, sec2H);
        gfx.DrawLine(borderPen, left, y + sec2H, right, y + sec2H);

        // Left
        gfx.DrawString(req.OperatorName, new XFont("Arial", 12, XFontStyle.Bold), darkBrush, left + 14, y + 16);
        gfx.DrawString(req.BusType, new XFont("Arial", 8.5, XFontStyle.Regular), slateBrush, left + 14, y + 30);
        var depTime = req.DepartureTime;
        var depTimeStr = depTime != default ? depTime.ToString("hh:mm tt") : "";
        gfx.DrawString($"Bus Start Time: {depTimeStr}", new XFont("Arial", 8, XFontStyle.Regular), grayBrush, left + 14, y + 42);

        // Right
        gfx.DrawString($"Service # {req.Pnr}", new XFont("Arial", 9, XFontStyle.Bold), darkBrush, new XRect(right - 200, y + 6, 186, 12), XStringFormats.TopRight);
        gfx.DrawString(req.OperatorName, new XFont("Arial", 8, XFontStyle.Regular), grayBrush, new XRect(right - 200, y + 19, 186, 12), XStringFormats.TopRight);

        y += sec2H;

        // =====================================================================
        // 3. JOURNEY ROUTE & TIMELINE WITH QR CODE
        // =====================================================================
        double sec3H = 114;
        gfx.DrawRectangle(XBrushes.White, left, y, contentW, sec3H);
        gfx.DrawLine(borderPen, left, y + sec3H, right, y + sec3H);

        // Column 1: DEPARTURE
        gfx.DrawString("DEPARTURE", new XFont("Arial", 7.5, XFontStyle.Bold), grayBrush, left + 14, y + 14);
        gfx.DrawString(req.Origin, new XFont("Arial", 13, XFontStyle.Bold), darkBrush, left + 14, y + 32);
        var bTime = req.BoardingPointTime != default ? req.BoardingPointTime : req.DepartureTime;
        var bTimeStr = bTime != default ? bTime.ToString("ddd, dd MMM yyyy hh:mm tt") : "-";
        gfx.DrawString(bTimeStr, new XFont("Arial", 8.5, XFontStyle.Bold), slateBrush, left + 14, y + 46);
        gfx.DrawString("Boarding Point:", new XFont("Arial", 7.5, XFontStyle.Bold), slateBrush, left + 14, y + 59);
        gfx.DrawString(req.BoardingPoint ?? "-", new XFont("Arial", 7.5, XFontStyle.Regular), grayBrush, new XRect(left + 14, y + 63, 160, 22), XStringFormats.TopLeft);

        // Vector Arrow -> (clean crisp red vector arrow)
        double ax1 = left + 176;
        double ax2 = left + 196;
        double ay = y + 27;
        var arrowPen = new XPen(XColor.FromArgb(220, 38, 38), 1.8);
        gfx.DrawLine(arrowPen, ax1, ay, ax2, ay);
        gfx.DrawLine(arrowPen, ax2, ay, ax2 - 5, ay - 4);
        gfx.DrawLine(arrowPen, ax2, ay, ax2 - 5, ay + 4);

        // Column 2: ARRIVAL
        gfx.DrawString("ARRIVAL", new XFont("Arial", 7.5, XFontStyle.Bold), grayBrush, left + 205, y + 14);
        gfx.DrawString(req.Destination, new XFont("Arial", 13, XFontStyle.Bold), darkBrush, left + 205, y + 32);
        var dTime = req.ArrivalPointTime != default ? req.ArrivalPointTime : req.ArrivalTime;
        var dTimeStr = dTime != default ? dTime.ToString("ddd, dd MMM yyyy hh:mm tt") : "-";
        gfx.DrawString(dTimeStr, new XFont("Arial", 8.5, XFontStyle.Bold), slateBrush, left + 205, y + 46);
        gfx.DrawString("Dropping Point:", new XFont("Arial", 7.5, XFontStyle.Bold), slateBrush, left + 205, y + 59);
        gfx.DrawString(req.ArrivalPoint ?? "-", new XFont("Arial", 7.5, XFontStyle.Regular), grayBrush, new XRect(left + 205, y + 63, 160, 22), XStringFormats.TopLeft);

        // Column 3: QR CODE (Contains full ticket & passenger details)
        gfx.DrawLine(dashedPen, right - 110, y + 6, right - 110, y + 84);
        try
        {
            var pListPreview = req.Passengers != null && req.Passengers.Count > 0 
                ? req.Passengers 
                : new List<BusPassengerSeatDto> { new BusPassengerSeatDto { FullName = req.PassengerName, SeatNumber = req.SeatNumber, Gender = "-" } };

            var paxSummary = string.Join(", ", pListPreview.Select(p => $"{p.FullName} (Seat {p.SeatNumber})"));
            var refundText = isCancelled && refundAmount > 0 ? $"\nRefund: INR {refundAmount:0.00}" : "";
            var qrText = $"PICK&BOOK BUS TICKET\n" +
                         $"Booking ID: {req.BookingReference}\n" +
                         $"PNR: {req.Pnr}\n" +
                         $"Operator: {req.OperatorName} ({req.BusType})\n" +
                         $"Route: {req.Origin} -> {req.Destination}\n" +
                         $"Boarding: {req.BoardingPoint ?? "-"} ({bTimeStr})\n" +
                         $"Dropping: {req.ArrivalPoint ?? "-"} ({dTimeStr})\n" +
                         $"Passengers: {paxSummary}\n" +
                         $"Fare: INR {req.Price:0.00}\n" +
                         $"Status: {statusBadge}{refundText}\n" +
                         $"Support: https://picknbook.in/contact";

            using var qrGen = new QRCodeGenerator();
            var qrData = qrGen.CreateQrCode(qrText, QRCodeGenerator.ECCLevel.M);
            using var qrCode = new PngByteQRCode(qrData);
            byte[] qrBytes = qrCode.GetGraphic(3);
            var qrImg = XImage.FromStream(() => new MemoryStream(qrBytes));
            gfx.DrawImage(qrImg, right - 95, y + 8, 68, 68);
            gfx.DrawString("SCAN FOR DETAILS", new XFont("Arial", 6.2, XFontStyle.Bold), grayBrush, new XRect(right - 105, y + 78, 88, 10), XStringFormats.Center);
        }
        catch { }

        // Notice banner
        if (isCancelled)
        {
            gfx.DrawRoundedRectangle(redBgBrush, left + 14, y + 90, contentW - 28, 16, 3, 3);
            gfx.DrawString("• Cancellation Confirmation: This bus booking is cancelled. Seat is released and invalid for travel.", new XFont("Arial", 7.5, XFontStyle.Bold), redTextBrush, left + 22, y + 101);
        }
        else
        {
            gfx.DrawRoundedRectangle(amberBgBrush, left + 14, y + 90, contentW - 28, 16, 3, 3);
            gfx.DrawRoundedRectangle(amberBorderPen, left + 14, y + 90, contentW - 28, 16, 3, 3);
            var reportTimeStr = bTime != default
                ? bTime.AddMinutes(-15).ToString("hh:mm tt")
                : "";
            gfx.DrawString($"* Reporting Time at the boarding point: {reportTimeStr} (15 minutes prior to departure)", new XFont("Arial", 7.5, XFontStyle.Bold), amberTextBrush, left + 22, y + 101);
        }

        y += sec3H;

        // =====================================================================
        // 4. PASSENGER DETAILS & PAYMENT DETAILS
        // =====================================================================
        var pList = req.Passengers != null && req.Passengers.Count > 0 
            ? req.Passengers 
            : new List<BusPassengerSeatDto> { new BusPassengerSeatDto { FullName = req.PassengerName, SeatNumber = req.SeatNumber, Gender = "-" } };

        int pxCount = pList.Count;
        double pxTableH = 36 + (pxCount * 16);
        double payTableH = 80;
        if (isCancelled) payTableH += 30;
        double sec4H = Math.Max(pxTableH, payTableH) + 12;

        gfx.DrawRectangle(cardBgBrush, left, y, contentW, sec4H);
        gfx.DrawLine(borderPen, left, y + sec4H, right, y + sec4H);

        double midX = left + (contentW * 0.52);
        gfx.DrawLine(borderPen, midX, y, midX, y + sec4H);

        // Left: Passenger Details
        gfx.DrawString("PASSENGER DETAILS", new XFont("Arial", 8, XFontStyle.Bold), darkBrush, left + 14, y + 14);
        gfx.DrawString("Name", new XFont("Arial", 7.5, XFontStyle.Bold), grayBrush, left + 14, y + 26);
        gfx.DrawString("Gender", new XFont("Arial", 7.5, XFontStyle.Bold), grayBrush, left + 150, y + 26);
        gfx.DrawString("Seat No", new XFont("Arial", 7.5, XFontStyle.Bold), grayBrush, new XRect(midX - 60, y + 18, 46, 10), XStringFormats.TopRight);
        gfx.DrawLine(borderPen, left + 14, y + 30, midX - 14, y + 30);

        double py = y + 42;
        foreach (var p in pList)
        {
            gfx.DrawString(p.FullName, new XFont("Arial", 7.5, XFontStyle.Bold), darkBrush, left + 14, py);
            gfx.DrawString(string.IsNullOrWhiteSpace(p.Gender) ? "-" : p.Gender, new XFont("Arial", 7.5, XFontStyle.Regular), grayBrush, left + 150, py);
            gfx.DrawString(p.SeatNumber ?? "-", new XFont("Arial", 7.5, XFontStyle.Bold), redBrush, new XRect(midX - 60, py - 8, 46, 10), XStringFormats.TopRight);
            gfx.DrawLine(borderPen, left + 14, py + 4, midX - 14, py + 4);
            py += 15;
        }

        // Right: Payment Details
        gfx.DrawString("PAYMENT DETAILS", new XFont("Arial", 8, XFontStyle.Bold), darkBrush, midX + 14, y + 14);
        double payY = y + 26;
        decimal totalGross = req.TotalFareBeforeDiscount > 0 
            ? req.TotalFareBeforeDiscount 
            : (req.Price + (req.DiscountAmount ?? 0m));

        DrawPdfPayRow(gfx, midX + 14, right - 14, ref payY, "Total Fare:", $"INR {totalGross:0.00}", grayBrush, darkBrush, false);

        var totalDisc = req.AutoDiscountAmount + req.CouponDiscountAmount;
        if (totalDisc == 0 && req.DiscountAmount.GetValueOrDefault() > 0) totalDisc = req.DiscountAmount.Value;
        if (totalDisc > 0)
        {
            DrawPdfPayRow(gfx, midX + 14, right - 14, ref payY, "Offer / Coupon Discount:", $"- INR {totalDisc:0.00}", greenTextBrush, greenTextBrush, false);
        }
        DrawPdfPayRow(gfx, midX + 14, right - 14, ref payY, isCancelled ? "Original Paid:" : "Amount Paid:", $"INR {req.Price:0.00}", darkBrush, redBrush, true, borderPen);
        if (isCancelled)
        {
            DrawPdfPayRow(gfx, midX + 14, right - 14, ref payY, "Cancellation Charge:", $"INR {Math.Max(0, req.Price - refundAmount):0.00}", redBrush, redBrush, false);
            DrawPdfPayRow(gfx, midX + 14, right - 14, ref payY, "Total Refund Amount:", $"INR {refundAmount:0.00}", greenTextBrush, greenTextBrush, true, borderPen);
        }

        y += sec4H;

        // =====================================================================
        // 5. IMPORTANT INFORMATION
        // =====================================================================
        double sec5H = 42;
        gfx.DrawRectangle(XBrushes.White, left, y, contentW, sec5H);
        gfx.DrawLine(borderPen, left, y + sec5H, right, y + sec5H);

        gfx.DrawString("IMPORTANT INFORMATION", new XFont("Arial", 8, XFontStyle.Bold), darkBrush, left + 14, y + 12);
        if (isCancelled)
        {
            gfx.DrawString("• This ticket has been cancelled. Seat is released and not valid for boarding.", new XFont("Arial", 7, XFontStyle.Regular), grayBrush, left + 14, y + 24);
            gfx.DrawString("• For refund inquiries or assistance, visit https://picknbook.in/contact", new XFont("Arial", 7, XFontStyle.Regular), grayBrush, left + 14, y + 34);
        }
        else
        {
            gfx.DrawString("• Please reach the boarding point by reporting time (at least 15 mins prior to scheduled departure).", new XFont("Arial", 7, XFontStyle.Regular), grayBrush, left + 14, y + 24);
            gfx.DrawString("• Chat with us for live tracking & trip updates at https://picknbook.in/contact", new XFont("Arial", 7, XFontStyle.Regular), grayBrush, left + 14, y + 34);
        }

        y += sec5H;

        // =====================================================================
        // 6. TERMS & CONDITIONS AND CANCELLATION POLICY
        // =====================================================================
        var parsedPolicies = ParseBusCancellationPolicies(req);
        int policyCount = parsedPolicies.Count > 0 ? parsedPolicies.Count : 1;
        double sec6H = Math.Max(125, 42 + (policyCount * 14) + 26);

        gfx.DrawRectangle(cardBgBrush, left, y, contentW, sec6H);
        gfx.DrawLine(borderPen, left, y + sec6H, right, y + sec6H);

        double col6MidX = left + (contentW * 0.48);
        gfx.DrawLine(borderPen, col6MidX, y, col6MidX, y + sec6H);

        // Left: Terms
        gfx.DrawString("TERMS AND CONDITIONS", new XFont("Arial", 8, XFontStyle.Bold), darkBrush, left + 14, y + 14);
        gfx.DrawString("- The arrival and departure times mentioned are tentative.", new XFont("Arial", 6.8, XFontStyle.Regular), grayBrush, left + 14, y + 26);
        gfx.DrawString("- Arrive at the boarding point at least 15 mins prior.", new XFont("Arial", 6.8, XFontStyle.Regular), grayBrush, left + 14, y + 38);
        gfx.DrawString("- Pick&book is not responsible for loss of belongings.", new XFont("Arial", 6.8, XFontStyle.Regular), grayBrush, left + 14, y + 50);
        gfx.DrawString("- Pick&book is not responsible for delays beyond control.", new XFont("Arial", 6.8, XFontStyle.Regular), grayBrush, left + 14, y + 62);
        gfx.DrawString("- Cancellation charges are applicable on Original fare.", new XFont("Arial", 6.8, XFontStyle.Regular), grayBrush, left + 14, y + 74);
        gfx.DrawString("- For detailed terms, visit https://picknbook.in/contact", new XFont("Arial", 6.8, XFontStyle.Regular), grayBrush, left + 14, y + 86);

        // Right: Cancellation Policy
        gfx.DrawString("CANCELLATION POLICY", new XFont("Arial", 8, XFontStyle.Bold), darkBrush, col6MidX + 14, y + 14);
        gfx.DrawString("Cancellation Time", new XFont("Arial", 7, XFontStyle.Bold), grayBrush, col6MidX + 14, y + 25);
        gfx.DrawString("Refund (%)", new XFont("Arial", 7, XFontStyle.Bold), grayBrush, new XRect(col6MidX + 130, y + 18, 50, 10), XStringFormats.TopCenter);
        gfx.DrawString("Refund Amount", new XFont("Arial", 7, XFontStyle.Bold), grayBrush, new XRect(right - 90, y + 18, 76, 10), XStringFormats.TopRight);
        gfx.DrawLine(borderPen, col6MidX + 14, y + 28, right - 14, y + 28);

        double cpy = y + 38;
        if (parsedPolicies.Count > 0)
        {
            foreach (var cp in parsedPolicies)
            {
                gfx.DrawString(cp.TimeWindow, new XFont("Arial", 6.8, XFontStyle.Regular), darkBrush, col6MidX + 14, cpy);
                gfx.DrawString(cp.RefundPct, new XFont("Arial", 6.8, XFontStyle.Regular), darkBrush, new XRect(col6MidX + 130, cpy - 7, 50, 10), XStringFormats.TopCenter);
                gfx.DrawString(cp.RefundAmt, new XFont("Arial", 6.8, XFontStyle.Bold), darkBrush, new XRect(right - 90, cpy - 7, 76, 10), XStringFormats.TopRight);
                cpy += 14;
            }

            gfx.DrawString("*Refund amount is indicative", new XFont("Arial", 6.5, XFontStyle.Italic), grayBrush, col6MidX + 14, cpy + 4);
        }

        y += sec6H;

        // =====================================================================
        // 7. FOOTER
        // =====================================================================
        double sec7H = 22;
        gfx.DrawRectangle(XBrushes.White, left, y, contentW, sec7H);
        gfx.DrawString($"Copyright {DateTime.UtcNow.Year} Pick&book Travel Services. All rights reserved. | https://picknbook.in", new XFont("Arial", 7, XFontStyle.Regular), grayBrush, new XRect(left, y + 5, contentW, 14), XStringFormats.Center);
        y += sec7H;

        // =====================================================================
        // OUTER CONTAINER BORDER
        // =====================================================================
        gfx.DrawRectangle(borderPen, left, startY, contentW, y - startY);

        // Watermark if Cancelled
        if (isCancelled)
        {
            var voidBrush = new XSolidBrush(XColor.FromArgb(40, 220, 38, 38));
            gfx.DrawString("CANCELLED", new XFont("Arial", 46, XFontStyle.Bold), voidBrush, new XRect(left, 140, contentW, 300), XStringFormats.Center);
        }
    }

    private static void DrawPdfPayRow(
        XGraphics gfx,
        double leftX,
        double rightX,
        ref double curY,
        string label,
        string value,
        XBrush labelBrush,
        XBrush valBrush,
        bool isTotal,
        XPen? borderPen = null)
    {
        if (isTotal && borderPen != null)
        {
            gfx.DrawLine(borderPen, leftX, curY - 2, rightX, curY - 2);
            curY += 3;
        }

        var fLabel = isTotal ? new XFont("Arial", 8.5, XFontStyle.Bold) : new XFont("Arial", 7.5, XFontStyle.Regular);
        var fVal = isTotal ? new XFont("Arial", 9.5, XFontStyle.Bold) : new XFont("Arial", 8, XFontStyle.Bold);

        gfx.DrawString(label, fLabel, labelBrush, leftX, curY + 8);
        gfx.DrawString(value, fVal, valBrush, new XRect(leftX, curY, rightX - leftX, 12), XStringFormats.TopRight);

        curY += isTotal ? 16 : 13;
    }

    private static List<(string TimeWindow, string RefundPct, string RefundAmt)> ParseBusCancellationPolicies(SendBusTicketEmailRequest req)
    {
        var result = new List<(string TimeWindow, string RefundPct, string RefundAmt)>();
        if (string.IsNullOrWhiteSpace(req.CancellationPoliciesJson)) return result;

        try
        {
            var jsonStr = req.CancellationPoliciesJson.Trim();
            if (jsonStr.StartsWith("\"") && jsonStr.EndsWith("\""))
            {
                try
                {
                    var unescaped = System.Text.Json.JsonSerializer.Deserialize<string>(jsonStr);
                    if (!string.IsNullOrWhiteSpace(unescaped)) jsonStr = unescaped.Trim();
                }
                catch { }
            }

            var options = new System.Text.Json.JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
            };

            List<SrdvCancellationPolicyDto>? policies = null;
            if (jsonStr.StartsWith("["))
            {
                policies = System.Text.Json.JsonSerializer.Deserialize<List<SrdvCancellationPolicyDto>>(jsonStr, options);
            }
            else if (jsonStr.StartsWith("{"))
            {
                var single = System.Text.Json.JsonSerializer.Deserialize<SrdvCancellationPolicyDto>(jsonStr, options);
                if (single != null) policies = new List<SrdvCancellationPolicyDto> { single };
            }

            if (policies != null && policies.Any())
            {
                foreach (var p in policies)
                {
                    string timeText;
                    if (!string.IsNullOrWhiteSpace(p.PolicyString))
                    {
                        var mBetween = System.Text.RegularExpressions.Regex.Match(p.PolicyString, @"between\s+(\d+\s+to\s+\d+\s+hours)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        var mBefore = System.Text.RegularExpressions.Regex.Match(p.PolicyString, @"anytime\s+before\s+(\d+\s+hours)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (mBetween.Success)
                            timeText = mBetween.Groups[1].Value.Replace("hours", "hrs");
                        else if (mBefore.Success)
                            timeText = $"Before {mBefore.Groups[1].Value.Replace("hours", "hrs")}";
                        else if (p.TimeBeforeDept == "-1")
                            timeText = "Before departure";
                        else
                            timeText = $"{p.TimeBeforeDept} hrs before dept";
                    }
                    else if (p.TimeBeforeDept == "-1")
                    {
                        timeText = "Before departure";
                    }
                    else
                    {
                        timeText = $"{p.TimeBeforeDept} hrs before dept";
                    }

                    decimal chargeVal = 0m;
                    decimal.TryParse(p.CancellationCharge, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out chargeVal);
                    var isPercentage = p.CancellationChargeType?.Contains("percentage", StringComparison.OrdinalIgnoreCase) == true;
                    var refundPct = isPercentage ? Math.Max(0, 100 - (int)chargeVal) : 0;
                    var refundAmt = isPercentage ? (req.Price * refundPct / 100m) : Math.Max(0, req.Price - chargeVal);
                    var refundAmtStr = $"INR {refundAmt:0.00}";
                    var pctDisplay = isPercentage ? $"{refundPct}%" : (refundAmt > 0 ? $"INR {refundAmt:0.00}" : "0%");

                    result.Add((timeText, pctDisplay, refundAmtStr));
                }
            }
        }
        catch { }

        return result;
    }
    

    public byte[] GenerateHotelTicketPdf(HotelReservation reservation)
    {
        var isCancelled = reservation.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase);
        var isFailed = reservation.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase);
        using var document = new PdfDocument();
        document.Info.Title = isCancelled 
            ? $"Hotel Voucher (CANCELLED) - {reservation.BookingReference}" 
            : (isFailed ? $"Hotel Voucher (FAILED) - {reservation.BookingReference}" : $"Hotel Confirmation Voucher - {reservation.BookingReference}");

        BuildHotelTicketPage(document, reservation, isCancelled);

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    private static void BuildHotelTicketPage(PdfDocument document, HotelReservation reservation, bool isCancelled)
    {
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(595);  // A4 width
        page.Height = XUnit.FromPoint(842); // A4 height

        using var gfx = XGraphics.FromPdfPage(page);

        // Brushes & Pens
        var darkBrush = new XSolidBrush(XColor.FromArgb(15, 23, 42));       // #0f172a
        var slateBrush = new XSolidBrush(XColor.FromArgb(51, 65, 85));      // #334155
        var grayBrush = new XSolidBrush(XColor.FromArgb(100, 116, 139));    // #64748b
        var redBrush = new XSolidBrush(XColor.FromArgb(220, 38, 38));       // #dc2626
        var greenTextBrush = new XSolidBrush(XColor.FromArgb(21, 128, 61)); // #15803d
        var greenBgBrush = new XSolidBrush(XColor.FromArgb(220, 252, 231)); // #dcfce7
        var redTextBrush = new XSolidBrush(XColor.FromArgb(185, 28, 28));   // #b91c1c
        var redBgBrush = new XSolidBrush(XColor.FromArgb(254, 226, 226));   // #fee2e2
        var cardBgBrush = new XSolidBrush(XColor.FromArgb(248, 250, 252));  // #f8fafc
        var amberBgBrush = new XSolidBrush(XColor.FromArgb(255, 251, 235)); // #fffbeb
        var amberTextBrush = new XSolidBrush(XColor.FromArgb(146, 64, 14)); // #92400e
        var amberBorderPen = new XPen(XColor.FromArgb(254, 243, 199), 1.0); // #fef3c7
        var borderPen = new XPen(XColor.FromArgb(226, 232, 240), 1.0);      // #e2e8f0
        var dashedPen = new XPen(XColor.FromArgb(203, 213, 225), 1.0) { DashStyle = XDashStyle.Dash };

        double left = 28;
        double contentW = page.Width - 56; // 539 pt
        double right = left + contentW;
        double y = 28;
        double startY = y;

        // =====================================================================
        // 1. HEADER & META BAR
        // =====================================================================
        // Red top stripe
        gfx.DrawRectangle(redBrush, left, y, contentW, 4);
        y += 4;

        double headerH = 68;
        gfx.DrawRectangle(XBrushes.White, left, y, contentW, headerH);
        gfx.DrawLine(borderPen, left, y + headerH, right, y + headerH);

        // Logo
        string[] candidateLogoPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "picknbook-logo.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "picknbook-logo.png"),
            Path.Combine(AppContext.BaseDirectory, "picknbook-logo.png")
        };
        string? foundLogo = candidateLogoPaths.FirstOrDefault(File.Exists);
        if (foundLogo != null)
        {
            try
            {
                using var logoImg = XImage.FromFile(foundLogo);
                gfx.DrawImage(logoImg, left + 14, y + 8, 112, 22);
            }
            catch { }
        }

        var isFailed = reservation.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase);
        var isPending = reservation.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase);

        // Title
        string voucherTitle = isCancelled 
            ? "HOTEL BOOKING CANCELLATION" 
            : (isFailed ? "HOTEL BOOKING FAILED" : (isPending ? "HOTEL BOOKING PENDING" : "HOTEL CONFIRMATION VOUCHER"));
        gfx.DrawString(voucherTitle, new XFont("Arial", 11, XFontStyle.Bold), redBrush, left + 14, y + 46);

        // Booking Date in IST
        var datePrefix = isCancelled ? "Cancelled on " : (isFailed ? "Failed on " : "Booked on ");
        var effectiveDate = isCancelled && reservation.CancelledAt.HasValue
            ? IndiaTimeHelper.ToIst(reservation.CancelledAt.Value)
            : IndiaTimeHelper.ToIst(reservation.CreatedAt);
        var bookingDateStr = $"{datePrefix}{effectiveDate:dd MMM yyyy, hh:mm tt}";
        gfx.DrawString(bookingDateStr, new XFont("Arial", 8, XFontStyle.Regular), grayBrush, left + 14, y + 58);

        // Right side: Booking ID, Hotel Conf No, Status Badge
        double metaBoxW = 230;
        double metaLeft = right - metaBoxW - 14;

        string displayBookingId = !string.IsNullOrWhiteSpace(reservation.ProviderBookingId)
            ? reservation.ProviderBookingId
            : (!string.IsNullOrWhiteSpace(reservation.SrdvBookingId) ? reservation.SrdvBookingId : reservation.BookingReference);

        gfx.DrawString("Booking ID:", new XFont("Arial", 8.5, XFontStyle.Regular), grayBrush, metaLeft, y + 16);
        gfx.DrawString(displayBookingId, new XFont("Arial", 9.5, XFontStyle.Bold), redBrush, new XRect(metaLeft, y + 6, metaBoxW, 14), XStringFormats.TopRight);

        string confNo = !string.IsNullOrWhiteSpace(reservation.ConfirmationNo) 
            ? reservation.ConfirmationNo 
            : (!string.IsNullOrWhiteSpace(reservation.BookingReference) ? reservation.BookingReference : displayBookingId);

        gfx.DrawString("Hotel Conf No:", new XFont("Arial", 8.5, XFontStyle.Regular), grayBrush, metaLeft, y + 30);
        gfx.DrawString(confNo, new XFont("Arial", 9, XFontStyle.Bold), darkBrush, new XRect(metaLeft, y + 20, metaBoxW, 14), XStringFormats.TopRight);

        string statusBadge = isCancelled ? "CANCELLED" : (isFailed ? "FAILED" : (isPending ? "PENDING" : "CONFIRMED"));
        XBrush badgeBg = (isCancelled || isFailed) ? redBgBrush : (isPending ? amberBgBrush : greenBgBrush);
        XBrush badgeTextBrush = (isCancelled || isFailed) ? redTextBrush : (isPending ? amberTextBrush : greenTextBrush);
        gfx.DrawRoundedRectangle(badgeBg, right - 95, y + 40, 75, 16, 3, 3);
        gfx.DrawString(statusBadge, new XFont("Arial", 8, XFontStyle.Bold), badgeTextBrush, new XRect(right - 95, y + 40, 75, 16), XStringFormats.Center);

        y += headerH;

        // =====================================================================
        // 2. PROPERTY & ROOM DETAILS (Flexible height for long names)
        // =====================================================================
        int nights = Math.Max(1, (reservation.CheckOutDate.Date - reservation.CheckInDate.Date).Days);
        string roomTypeName = !string.IsNullOrWhiteSpace(reservation.RoomTypeName)
            ? reservation.RoomTypeName.Trim()
            : GetRoomCategory(reservation.OfferId);

        // Measure and adapt section height if names are long
        double sec2H = 52;
        gfx.DrawRectangle(cardBgBrush, left, y, contentW, sec2H);
        gfx.DrawLine(borderPen, left, y + sec2H, right, y + sec2H);

        // Left: Hotel Name, Room Type with Nights Badge, Standard times (Flexible layout)
        double availableNameW = contentW - 200;
        double hotelNameFontSize = reservation.HotelName.Length > 44 ? 9.5 : (reservation.HotelName.Length > 30 ? 10.5 : 12);
        gfx.DrawString(reservation.HotelName, new XFont("Arial", hotelNameFontSize, XFontStyle.Bold), darkBrush, new XRect(left + 14, y + 6, availableNameW, 14), XStringFormats.TopLeft);
        string roomDurationText = $"{roomTypeName}  •  [ {nights} NIGHTS STAY ]";
        double roomFontSize = roomDurationText.Length > 44 ? 7.5 : 8.5;
        gfx.DrawString(roomDurationText, new XFont("Arial", roomFontSize, XFontStyle.Bold), slateBrush, new XRect(left + 14, y + 21, availableNameW, 12), XStringFormats.TopLeft);
        gfx.DrawString("Standard Check-in: 12:00 PM | Standard Check-out: 11:00 AM", new XFont("Arial", 8, XFontStyle.Regular), grayBrush, new XRect(left + 14, y + 36, availableNameW, 12), XStringFormats.TopLeft);

        // Right: Voucher Ref & Location
        gfx.DrawString($"Voucher # {confNo}", new XFont("Arial", 8.5, XFontStyle.Bold), darkBrush, new XRect(right - 200, y + 8, 186, 12), XStringFormats.TopRight);
        string cityLocation = !string.IsNullOrWhiteSpace(reservation.CityCode) ? $"Location: {reservation.CityCode}" : "Hotel Stay";
        gfx.DrawString(cityLocation, new XFont("Arial", 8, XFontStyle.Regular), grayBrush, new XRect(right - 200, y + 24, 186, 12), XStringFormats.TopRight);

        y += sec2H;

        // =====================================================================
        // 3. STAY TIMELINE & QR CODE (No ASCII arrows)
        // =====================================================================
        double sec3H = 114;
        gfx.DrawRectangle(XBrushes.White, left, y, contentW, sec3H);
        gfx.DrawLine(borderPen, left, y + sec3H, right, y + sec3H);

        // Column 1: CHECK-IN
        var checkInIst = IndiaTimeHelper.ToIst(reservation.CheckInDate);
        var checkOutIst = IndiaTimeHelper.ToIst(reservation.CheckOutDate);

        gfx.DrawString("CHECK-IN", new XFont("Arial", 7.5, XFontStyle.Bold), grayBrush, left + 14, y + 14);
        string cityDisplay = !string.IsNullOrWhiteSpace(reservation.CityCode) ? reservation.CityCode : "Hotel Destination";
        gfx.DrawString(cityDisplay, new XFont("Arial", 12, XFontStyle.Bold), darkBrush, left + 14, y + 30);
        gfx.DrawString(checkInIst.ToString("ddd, dd MMM yyyy"), new XFont("Arial", 8.5, XFontStyle.Bold), slateBrush, left + 14, y + 44);
        gfx.DrawString("From 12:00 PM", new XFont("Arial", 7.5, XFontStyle.Regular), grayBrush, left + 14, y + 56);
        gfx.DrawString("Standard Hotel Check-In", new XFont("Arial", 7, XFontStyle.Italic), grayBrush, left + 14, y + 68);

        // Column 2: CHECK-OUT
        gfx.DrawString("CHECK-OUT", new XFont("Arial", 7.5, XFontStyle.Bold), grayBrush, left + 205, y + 14);
        gfx.DrawString(cityDisplay, new XFont("Arial", 12, XFontStyle.Bold), darkBrush, left + 205, y + 30);
        gfx.DrawString(checkOutIst.ToString("ddd, dd MMM yyyy"), new XFont("Arial", 8.5, XFontStyle.Bold), slateBrush, left + 205, y + 44);
        gfx.DrawString("Until 11:00 AM", new XFont("Arial", 7.5, XFontStyle.Regular), grayBrush, left + 205, y + 56);
        gfx.DrawString("Standard Hotel Check-Out", new XFont("Arial", 7, XFontStyle.Italic), grayBrush, left + 205, y + 68);

        // Column 3: QR CODE (Contains full voucher details)
        gfx.DrawLine(dashedPen, right - 110, y + 6, right - 110, y + 84);
        try
        {
            var qrText = $"PICK&BOOK HOTEL VOUCHER\n" +
                         $"Booking ID: {reservation.BookingReference}\n" +
                         $"Conf No: {confNo}\n" +
                         $"Hotel: {reservation.HotelName}\n" +
                         $"Room: {roomTypeName} ({nights} Nights)\n" +
                         $"Check-in: {checkInIst:dd MMM yyyy}\n" +
                         $"Check-out: {checkOutIst:dd MMM yyyy}\n" +
                         $"Guest: {reservation.GuestName} ({reservation.Rooms} Rm, {reservation.Adults} Ad)\n" +
                         $"Total Paid: INR {reservation.TotalPrice:0.00}\n" +
                         $"Status: {statusBadge}\n" +
                         $"Support: https://picknbook.in/contact";

            using var qrGen = new QRCodeGenerator();
            var qrData = qrGen.CreateQrCode(qrText, QRCodeGenerator.ECCLevel.M);
            using var qrCode = new PngByteQRCode(qrData);
            byte[] qrBytes = qrCode.GetGraphic(3);
            var qrImg = XImage.FromStream(() => new MemoryStream(qrBytes));
            gfx.DrawImage(qrImg, right - 95, y + 8, 68, 68);
            gfx.DrawString("SCAN FOR DETAILS", new XFont("Arial", 6.2, XFontStyle.Bold), grayBrush, new XRect(right - 105, y + 78, 88, 10), XStringFormats.Center);
        }
        catch { }

        // Notice banner at bottom of Section 2
        if (isCancelled)
        {
            gfx.DrawRoundedRectangle(redBgBrush, left + 14, y + 90, contentW - 28, 16, 3, 3);
            gfx.DrawString("• Cancellation Confirmation: This hotel reservation is cancelled. Booking is invalid for check-in.", new XFont("Arial", 7.5, XFontStyle.Bold), redTextBrush, left + 22, y + 101);
        }
        else
        {
            gfx.DrawRoundedRectangle(amberBgBrush, left + 14, y + 90, contentW - 28, 16, 3, 3);
            gfx.DrawRoundedRectangle(amberBorderPen, left + 14, y + 90, contentW - 28, 16, 3, 3);
            gfx.DrawString("* Standard Hotel Check-in: 12:00 PM • Mandatory Govt Photo ID Proof Required for All Guests", new XFont("Arial", 7.5, XFontStyle.Bold), amberTextBrush, left + 22, y + 101);
        }

        y += sec3H;

        // =====================================================================
        // 4. GUEST DETAILS & PAYMENT BREAKDOWN (No Convenience Fee)
        // =====================================================================
        double sec4H = 104;
        gfx.DrawRectangle(cardBgBrush, left, y, contentW, sec4H);
        gfx.DrawLine(borderPen, left, y + sec4H, right, y + sec4H);

        double midX = left + (contentW * 0.52);
        gfx.DrawLine(borderPen, midX, y, midX, y + sec4H);

        // Left: Guest Details
        gfx.DrawString("GUEST DETAILS", new XFont("Arial", 8, XFontStyle.Bold), darkBrush, left + 14, y + 14);
        double gy = y + 27;
        gfx.DrawString($"Primary Guest: {reservation.GuestName}", new XFont("Arial", 7.5, XFontStyle.Bold), darkBrush, left + 14, gy);
        gy += 14;
        gfx.DrawString($"Email: {reservation.GuestEmail}", new XFont("Arial", 7.2, XFontStyle.Regular), slateBrush, left + 14, gy);
        gy += 13;
        gfx.DrawString($"Mobile: {reservation.GuestPhone}", new XFont("Arial", 7.2, XFontStyle.Regular), slateBrush, left + 14, gy);
        gy += 13;
        gfx.DrawString($"Occupancy: {reservation.Rooms} Room(s) • {reservation.Adults} Adult(s), {reservation.Children} Child(ren)", new XFont("Arial", 7.2, XFontStyle.Regular), slateBrush, left + 14, gy);
        gy += 13;
        string nationalityText = reservation.GuestNationality == "IN" ? "Indian (IN)" : (reservation.GuestNationality ?? "Indian (IN)");
        gfx.DrawString($"Room Category: {roomTypeName} | Nationality: {nationalityText}", new XFont("Arial", 7, XFontStyle.Regular), grayBrush, left + 14, gy);

        // Right: Payment Details (No Convenience Fee)
        gfx.DrawString(isCancelled ? "PAYMENT & REFUND BREAKDOWN" : "PAYMENT DETAILS", new XFont("Arial", 8, XFontStyle.Bold), darkBrush, midX + 14, y + 14);
        double payY = y + 26;

        if (isCancelled)
        {
            DrawPdfPayRow(gfx, midX + 14, right - 14, ref payY, "Original Total Paid:", $"INR {reservation.TotalPrice:0.00}", grayBrush, darkBrush, false);
            DrawPdfPayRow(gfx, midX + 14, right - 14, ref payY, "Cancellation Charges:", $"INR {reservation.CancellationCharges:0.00}", redBrush, redBrush, false);
            DrawPdfPayRow(gfx, midX + 14, right - 14, ref payY, "Total Refund Amount:", $"INR {reservation.RefundAmount:0.00}", greenTextBrush, greenTextBrush, true, borderPen);
        }
        else
        {
            decimal basePrice = reservation.BasePrice > 0 ? reservation.BasePrice : reservation.Price;
            DrawPdfPayRow(gfx, midX + 14, right - 14, ref payY, "Room Base Charges:", $"INR {basePrice:0.00}", grayBrush, darkBrush, false);

            if (reservation.SrdvGstAmount > 0)
            {
                DrawPdfPayRow(gfx, midX + 14, right - 14, ref payY, "Hotel Taxes & Service GST:", $"INR {reservation.SrdvGstAmount:0.00}", grayBrush, darkBrush, false);
            }

            if (reservation.CouponDiscount > 0)
            {
                string couponLabel = !string.IsNullOrWhiteSpace(reservation.CouponCode) ? $"Discount ({reservation.CouponCode}):" : "Offer / Coupon Discount:";
                DrawPdfPayRow(gfx, midX + 14, right - 14, ref payY, couponLabel, $"- INR {reservation.CouponDiscount:0.00}", greenTextBrush, greenTextBrush, false);
            }

            DrawPdfPayRow(gfx, midX + 14, right - 14, ref payY, "Total Amount Paid:", $"INR {reservation.TotalPrice:0.00}", darkBrush, redBrush, true, borderPen);
        }

        y += sec4H;

        // =====================================================================
        // 5. IMPORTANT INFORMATION
        // =====================================================================
        double sec5H = 44;
        gfx.DrawRectangle(XBrushes.White, left, y, contentW, sec5H);
        gfx.DrawLine(borderPen, left, y + sec5H, right, y + sec5H);

        gfx.DrawString("IMPORTANT INFORMATION", new XFont("Arial", 8, XFontStyle.Bold), darkBrush, left + 14, y + 12);
        if (isCancelled)
        {
            gfx.DrawString("• This reservation has been cancelled. Hotel rooms have been released back to hotel inventory.", new XFont("Arial", 7, XFontStyle.Regular), grayBrush, left + 14, y + 24);
            gfx.DrawString("• For refund inquiries, concierge assistance, or booking help, visit https://picknbook.in/contact", new XFont("Arial", 7, XFontStyle.Regular), grayBrush, left + 14, y + 34);
        }
        else
        {
            gfx.DrawString("• Mandatory Govt Photo ID Proof (Aadhaar, Passport, DL, Voter ID) required for all adult guests (PAN card not accepted).", new XFont("Arial", 7, XFontStyle.Regular), grayBrush, left + 14, y + 24);
            gfx.DrawString("• Early check-in and late check-out are subject to availability and may incur additional charges directly at reception.", new XFont("Arial", 7, XFontStyle.Regular), grayBrush, left + 14, y + 34);
        }

        y += sec5H;

        // =====================================================================
        // 6. TERMS & CANCELLATION POLICY (Pure dynamic - Zero fallbacks)
        // =====================================================================
        var parsedPolicies = ParseHotelCancellationPolicies(reservation);
        int policyRowsCount = isCancelled ? 1 : Math.Max(1, parsedPolicies.Count);
        double sec6H = Math.Max(95, 34 + (policyRowsCount * 14) + 16);

        gfx.DrawRectangle(cardBgBrush, left, y, contentW, sec6H);
        gfx.DrawLine(borderPen, left, y + sec6H, right, y + sec6H);

        double col6MidX = left + (contentW * 0.48);
        gfx.DrawLine(borderPen, col6MidX, y, col6MidX, y + sec6H);

        if (isCancelled)
        {
            // Left: Cancellation Notice
            gfx.DrawString("CANCELLATION SUMMARY", new XFont("Arial", 8, XFontStyle.Bold), darkBrush, left + 14, y + 14);
            gfx.DrawString("- The booking is cancelled in accordance with supplier terms.", new XFont("Arial", 6.8, XFontStyle.Regular), grayBrush, left + 14, y + 26);
            gfx.DrawString("- Refund has been processed to source payment method / wallet.", new XFont("Arial", 6.8, XFontStyle.Regular), grayBrush, left + 14, y + 38);
            gfx.DrawString("- Bank settlement timeline: 3-7 business days if applicable.", new XFont("Arial", 6.8, XFontStyle.Regular), grayBrush, left + 14, y + 50);
            gfx.DrawString("- For inquiries, visit https://picknbook.in/contact", new XFont("Arial", 6.8, XFontStyle.Regular), grayBrush, left + 14, y + 62);

            // Right: Dynamic Policy Applied
            var applied = EvaluateAppliedHotelCancellationPolicy(reservation);
            gfx.DrawString("CANCELLATION POLICY APPLIED", new XFont("Arial", 8, XFontStyle.Bold), darkBrush, col6MidX + 14, y + 14);
            gfx.DrawString("Window / Status", new XFont("Arial", 7, XFontStyle.Bold), grayBrush, col6MidX + 14, y + 25);
            gfx.DrawString("Refund (%)", new XFont("Arial", 7, XFontStyle.Bold), grayBrush, new XRect(col6MidX + 130, y + 18, 50, 10), XStringFormats.TopCenter);
            gfx.DrawString("Refund Amount", new XFont("Arial", 7, XFontStyle.Bold), grayBrush, new XRect(right - 90, y + 18, 76, 10), XStringFormats.TopRight);
            gfx.DrawLine(borderPen, col6MidX + 14, y + 28, right - 14, y + 28);

            double cpy = y + 38;
            gfx.DrawString(applied.Window, new XFont("Arial", 6.8, XFontStyle.Regular), darkBrush, new XRect(col6MidX + 14, cpy - 7, 115, 12), XStringFormats.TopLeft);
            gfx.DrawString(applied.RefundPct, new XFont("Arial", 6.8, XFontStyle.Regular), darkBrush, new XRect(col6MidX + 130, cpy - 7, 50, 10), XStringFormats.TopCenter);
            gfx.DrawString(applied.RefundAmt, new XFont("Arial", 6.8, XFontStyle.Bold), greenTextBrush, new XRect(right - 90, cpy - 7, 76, 10), XStringFormats.TopRight);

            gfx.DrawString($"*{applied.Note}", new XFont("Arial", 6.5, XFontStyle.Italic), grayBrush, new XRect(col6MidX + 14, cpy + 14, right - col6MidX - 28, 12), XStringFormats.TopLeft);
        }
        else
        {
            // Left: Hotel Terms (Duplicate check-in line removed)
            gfx.DrawString("HOTEL TERMS AND CONDITIONS", new XFont("Arial", 8, XFontStyle.Bold), darkBrush, left + 14, y + 14);
            gfx.DrawString("- Primary guest must be at least 18 years of age with photo ID.", new XFont("Arial", 6.8, XFontStyle.Regular), grayBrush, left + 14, y + 26);
            gfx.DrawString("- Unmarried couples & local ID rules are governed by hotel policies.", new XFont("Arial", 6.8, XFontStyle.Regular), grayBrush, left + 14, y + 38);
            gfx.DrawString("- Incidentals, mini-bar, and room service are payable at the hotel.", new XFont("Arial", 6.8, XFontStyle.Regular), grayBrush, left + 14, y + 50);
            gfx.DrawString("- Extra bed and child charges are payable directly at reception.", new XFont("Arial", 6.8, XFontStyle.Regular), grayBrush, left + 14, y + 62);
            gfx.DrawString("- For full booking policies, visit https://picknbook.in/terms", new XFont("Arial", 6.8, XFontStyle.Regular), grayBrush, left + 14, y + 74);

            // Right: Dynamic Cancellation Policy (Strictly from JSON)
            gfx.DrawString("CANCELLATION POLICY", new XFont("Arial", 8, XFontStyle.Bold), darkBrush, col6MidX + 14, y + 14);
            gfx.DrawString("Cancellation Time", new XFont("Arial", 7, XFontStyle.Bold), grayBrush, col6MidX + 14, y + 25);
            gfx.DrawString("Refund (%)", new XFont("Arial", 7, XFontStyle.Bold), grayBrush, new XRect(col6MidX + 130, y + 18, 50, 10), XStringFormats.TopCenter);
            gfx.DrawString("Refund Amount", new XFont("Arial", 7, XFontStyle.Bold), grayBrush, new XRect(right - 90, y + 18, 76, 10), XStringFormats.TopRight);
            gfx.DrawLine(borderPen, col6MidX + 14, y + 28, right - 14, y + 28);

            double cpy = y + 38;
            if (parsedPolicies.Count > 0)
            {
                foreach (var cp in parsedPolicies)
                {
                    gfx.DrawString(cp.TimeWindow, new XFont("Arial", 6.8, XFontStyle.Regular), darkBrush, new XRect(col6MidX + 14, cpy - 7, 115, 12), XStringFormats.TopLeft);
                    gfx.DrawString(cp.RefundPct, new XFont("Arial", 6.8, XFontStyle.Regular), darkBrush, new XRect(col6MidX + 130, cpy - 7, 50, 10), XStringFormats.TopCenter);
                    gfx.DrawString(cp.RefundAmt, new XFont("Arial", 6.8, XFontStyle.Bold), darkBrush, new XRect(right - 90, cpy - 7, 76, 10), XStringFormats.TopRight);
                    cpy += 14;
                }
                gfx.DrawString("*Calculated per hotel cancellation policy", new XFont("Arial", 6.5, XFontStyle.Italic), grayBrush, col6MidX + 14, cpy + 2);
            }
            else
            {
                gfx.DrawString("Cancellation terms subject to hotel property rules.", new XFont("Arial", 6.8, XFontStyle.Regular), darkBrush, col6MidX + 14, cpy);
            }
        }

        y += sec6H;

        // Outer container border
        gfx.DrawRectangle(borderPen, left, startY, contentW, y - startY);

        // Watermark if Cancelled
        if (isCancelled)
        {
            var voidBrush = new XSolidBrush(XColor.FromArgb(40, 220, 38, 38));
            gfx.DrawString("CANCELLED", new XFont("Arial", 46, XFontStyle.Bold), voidBrush, new XRect(left, 140, contentW, 300), XStringFormats.Center);
        }
    }

    private static List<(string TimeWindow, string RefundPct, string RefundAmt, decimal DeductionAmount)> ParseHotelCancellationPolicies(HotelReservation reservation)
    {
        var result = new List<(string TimeWindow, string RefundPct, string RefundAmt, decimal DeductionAmount)>();
        if (string.IsNullOrWhiteSpace(reservation.CancellationPolicyJson)) return result;

        try
        {
            var jsonStr = reservation.CancellationPolicyJson.Trim();
            if (jsonStr.StartsWith("\"") && jsonStr.EndsWith("\""))
            {
                try
                {
                    var unescaped = System.Text.Json.JsonSerializer.Deserialize<string>(jsonStr);
                    if (!string.IsNullOrWhiteSpace(unescaped)) jsonStr = unescaped.Trim();
                }
                catch { }
            }

            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
            };

            List<HotelRoomCancellationPolicyDto>? policies = null;
            if (jsonStr.StartsWith("["))
            {
                policies = System.Text.Json.JsonSerializer.Deserialize<List<HotelRoomCancellationPolicyDto>>(jsonStr, options);
            }
            else if (jsonStr.StartsWith("{"))
            {
                var single = System.Text.Json.JsonSerializer.Deserialize<HotelRoomCancellationPolicyDto>(jsonStr, options);
                if (single != null) policies = new List<HotelRoomCancellationPolicyDto> { single };
            }

            if (policies != null && policies.Count > 0)
            {
                decimal totalPrice = reservation.TotalPrice > 0 ? reservation.TotalPrice : reservation.Price;
                foreach (var p in policies)
                {
                    string timeWindow;
                    DateTime? fromDt = null;
                    DateTime? toDt = null;
                    if (DateTime.TryParse(p.FromDate, out var f)) fromDt = f;
                    if (DateTime.TryParse(p.ToDate, out var t)) toDt = t;

                    if (fromDt.HasValue && toDt.HasValue)
                    {
                        timeWindow = $"{fromDt.Value:dd MMM} - {toDt.Value:dd MMM, hh:mm tt}";
                    }
                    else if (fromDt.HasValue)
                    {
                        timeWindow = $"From {fromDt.Value:dd MMM yyyy, hh:mm tt}";
                    }
                    else if (toDt.HasValue)
                    {
                        timeWindow = $"Until {toDt.Value:dd MMM yyyy, hh:mm tt}";
                    }
                    else
                    {
                        timeWindow = "Standard Window";
                    }

                    decimal refundPctVal = 0m;
                    decimal refundAmtVal = 0m;
                    decimal deductionVal = 0m;

                    if (p.ChargeType == 1) // Percentage fee
                    {
                        deductionVal = totalPrice * (p.Charge / 100m);
                        refundPctVal = Math.Max(0m, 100m - p.Charge);
                        refundAmtVal = Math.Max(0m, totalPrice - deductionVal);
                    }
                    else // Fixed fee
                    {
                        deductionVal = p.Charge;
                        refundAmtVal = Math.Max(0m, totalPrice - deductionVal);
                        refundPctVal = totalPrice > 0 ? Math.Round((refundAmtVal / totalPrice) * 100m, 0) : 0m;
                    }

                    result.Add((timeWindow, $"{refundPctVal:0}%", $"INR {refundAmtVal:0.00}", deductionVal));
                }
            }
        }
        catch { }

        return result;
    }

    private static (string Window, string RefundPct, string Deduction, string RefundAmt, string Note) EvaluateAppliedHotelCancellationPolicy(HotelReservation reservation)
    {
        var policies = ParseHotelCancellationPolicies(reservation);
        var cancelTimeIst = reservation.CancelledAt.HasValue
            ? IndiaTimeHelper.ToIst(reservation.CancelledAt.Value)
            : IndiaTimeHelper.ToIst(DateTime.UtcNow);

        decimal actualCharge = reservation.CancellationCharges;
        decimal actualRefund = reservation.RefundAmount;
        decimal totalPrice = reservation.TotalPrice > 0 ? reservation.TotalPrice : reservation.Price;
        decimal refundPct = totalPrice > 0 ? Math.Round((actualRefund / totalPrice) * 100m, 0) : 0m;

        string windowText = $"{cancelTimeIst:dd MMM yyyy, hh:mm tt}";
        string note = $"Cancelled on {cancelTimeIst:dd MMM yyyy, hh:mm tt}";

        if (policies.Count > 0)
        {
            var matching = policies.FirstOrDefault(p => Math.Abs(p.DeductionAmount - actualCharge) < 1.0m);
            if (matching != default)
            {
                return (matching.TimeWindow, matching.RefundPct, $"INR {actualCharge:0.00} (Fee)", $"INR {actualRefund:0.00} (Refunded)", $"Cancelled within {matching.TimeWindow}");
            }
        }

        return (windowText, $"{refundPct:0}% Refund", $"INR {actualCharge:0.00} (Fee)", $"INR {actualRefund:0.00} (Refunded)", note);
    }

    private static string GetRoomCategory(string offerId)
    {
        if (string.IsNullOrWhiteSpace(offerId)) return "Standard Room";
        if (offerId.Contains("suite", StringComparison.OrdinalIgnoreCase)) return "Executive Suite";
        if (offerId.Contains("deluxe", StringComparison.OrdinalIgnoreCase)) return "Deluxe Room";
        return "Standard Room";
    }

    private static DateTime ToIst(DateTime utc)
    {
        return DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(5.5);
    }

    private static void DrawDashedLine(
        XGraphics gfx,
        XPen pen,
        double x1,
        double y,
        double x2)
    {
        double dash = 5;
        double gap = 3;
        double x = x1;

        while (x < x2)
        {
            double end = Math.Min(x + dash, x2);
            gfx.DrawLine(pen, x, y, end, y);
            x += dash + gap;
        }
    }
}


