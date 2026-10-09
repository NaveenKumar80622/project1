
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
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
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "picknbook-logo.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "picknbook-logo.png"),
            @"c:\Users\ADMIN\Desktop\Local\PickNBook.Api\wwwroot\picknbook-logo.png",
            @"c:\Users\ADMIN\Desktop\Local\PickNBook.Api\bin\Debug\net8.0\wwwroot\picknbook-logo.png",
            @"c:\Users\ADMIN\Desktop\Local\TicketEmailTester\bin\Debug\net8.0\wwwroot\picknbook-logo.png",
            @"c:\Users\ADMIN\Desktop\Local\PickNBook.Api\publish\wwwroot\picknbook-logo.png"
        };
        string? foundLogo = candidateLogoPaths.FirstOrDefault(File.Exists);
        if (foundLogo != null)
        {
            try
            {
                using var logoImg = XImage.FromFile(foundLogo);
                gfx.DrawImage(logoImg, left + 14, y + 8, 112, 22);
            }
            catch
            {
                DrawFallbackLogo(gfx, left + 14, y + 26, redBrush, grayBrush);
            }
        }
        else
        {
            DrawFallbackLogo(gfx, left + 14, y + 26, redBrush, grayBrush);
        }

        // Title: Bus Ticket
        string ticketTitle = isCancelled ? "BUS TICKET CANCELLATION" : "BUS TICKET";
        gfx.DrawString(ticketTitle, new XFont("Arial", 11, XFontStyle.Bold), redBrush, left + 14, y + 46);

        // Booking Date
        var datePrefix = isCancelled ? "Cancelled on " : "Booked on ";
        var bookingDateStr = req.DepartureTime != default 
            ? $"{datePrefix}{DateTime.UtcNow.AddHours(5.5):dd MMM yyyy, hh:mm tt}" 
            : "";
        gfx.DrawString(bookingDateStr, new XFont("Arial", 8, XFontStyle.Regular), grayBrush, left + 14, y + 58);

        // Right side: Booking ID, PNR, Status
        gfx.DrawString("Booking ID: ", new XFont("Arial", 8.5, XFontStyle.Regular), grayBrush, right - 190, y + 16);
        gfx.DrawString(req.BookingReference, new XFont("Arial", 9.5, XFontStyle.Bold), redBrush, right - 120, y + 16);

        gfx.DrawString("Bus Partner PNR: ", new XFont("Arial", 8.5, XFontStyle.Regular), grayBrush, right - 190, y + 30);
        gfx.DrawString(req.Pnr, new XFont("Arial", 9, XFontStyle.Bold), darkBrush, right - 105, y + 30);

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
        var depTimeStr = req.DepartureTime != default ? req.DepartureTime.AddHours(5.5).ToString("hh:mm tt") : "";
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
        var bTimeStr = req.BoardingPointTime != default 
            ? req.BoardingPointTime.AddHours(5.5).ToString("ddd, dd MMM yyyy hh:mm tt") 
            : req.DepartureTime.AddHours(5.5).ToString("ddd, dd MMM yyyy hh:mm tt");
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
        var dTimeStr = req.ArrivalPointTime != default 
            ? req.ArrivalPointTime.AddHours(5.5).ToString("ddd, dd MMM yyyy hh:mm tt") 
            : req.ArrivalTime.AddHours(5.5).ToString("ddd, dd MMM yyyy hh:mm tt");
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
            var qrText = $"PICK&BOOK BUS TICKET\n" +
                         $"Booking ID: {req.BookingReference}\n" +
                         $"PNR: {req.Pnr}\n" +
                         $"Operator: {req.OperatorName} ({req.BusType})\n" +
                         $"Route: {req.Origin} -> {req.Destination}\n" +
                         $"Boarding: {req.BoardingPoint ?? "-"} ({bTimeStr})\n" +
                         $"Dropping: {req.ArrivalPoint ?? "-"} ({dTimeStr})\n" +
                         $"Passengers: {paxSummary}\n" +
                         $"Fare: INR {req.Price:0.00}\n" +
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
            var reportTimeStr = (req.BoardingPointTime != default 
                ? req.BoardingPointTime.AddHours(5.5).AddMinutes(-15) 
                : req.DepartureTime.AddHours(5.5).AddMinutes(-15)).ToString("hh:mm tt");
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
        var baseFare = req.BaseFare > 0 ? req.BaseFare : Math.Max(0, req.Price - req.GstAmount);
        DrawPdfPayRow(gfx, midX + 14, right - 14, ref payY, "Basic Fare:", $"INR {baseFare:0.00}", grayBrush, darkBrush, false);
        if (req.GstAmount > 0)
        {
            DrawPdfPayRow(gfx, midX + 14, right - 14, ref payY, "Bus Partner GST:", $"INR {req.GstAmount:0.00}", grayBrush, darkBrush, false);
        }
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
        double sec6H = Math.Max(112, 38 + (policyCount * 13));

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
                cpy += 13;
            }

            gfx.DrawString("*Refund amount is indicative", new XFont("Arial", 6.5, XFontStyle.Italic), grayBrush, col6MidX + 14, cpy + 2);
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

    private static void DrawFallbackLogo(XGraphics gfx, double x, double y, XBrush redBrush, XBrush grayBrush)
    {
        var fLogo = new XFont("Arial", 16, XFontStyle.Bold);
        gfx.DrawString("Pick", fLogo, redBrush, x, y);
        gfx.DrawString("&", fLogo, grayBrush, x + 34, y);
        gfx.DrawString("book", fLogo, redBrush, x + 46, y);
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
        using var document = new PdfDocument();
        document.Info.Title = $"Hotel Ticket - {reservation.BookingReference}";

        var page = document.AddPage();
        page.Width = XUnit.FromPoint(600);
        page.Height = XUnit.FromPoint(700);

        using var gfx = XGraphics.FromPdfPage(page);

        // Define colors
        var navyColor = XColor.FromArgb(15, 36, 89);
        var navyBrush = new XSolidBrush(navyColor);
        var greyColor = XColor.FromArgb(120, 130, 155);
        var greyBrush = new XSolidBrush(greyColor);
        var lightGreyColor = XColor.FromArgb(248, 250, 252);
        var lightGreyBrush = new XSolidBrush(lightGreyColor);
        var borderPen = new XPen(XColor.FromArgb(226, 232, 240), 1.0);

        // Fonts
        var fontTitle = new XFont("Arial", 16, XFontStyle.Bold);
        var fontSubtitle = new XFont("Arial", 9, XFontStyle.Regular);
        var fontCardTitle = new XFont("Arial", 8, XFontStyle.Bold);
        var fontCardValue = new XFont("Arial", 11, XFontStyle.Bold);
        var fontCardSub = new XFont("Arial", 9, XFontStyle.Regular);
        var fontSectionHeader = new XFont("Arial", 10, XFontStyle.Bold);
        var fontTextBold = new XFont("Arial", 9, XFontStyle.Bold);
        var fontTextReg = new XFont("Arial", 9, XFontStyle.Regular);
        var fontPill = new XFont("Arial", 8, XFontStyle.Bold);

        double left = 30;
        double pageW = page.Width;
        double right = pageW - 30;
        double width = right - left;
        double y = 45;

        // Draw Logo Box (top left)
        var logoRect = new XRect(left, y, 24, 24);
        gfx.DrawRectangle(navyBrush, logoRect);
        gfx.DrawString("H", new XFont("Arial", 14, XFontStyle.Bold), XBrushes.White, logoRect, XStringFormats.Center);

        // Draw Header Title & Reference
        gfx.DrawString(
            $"{reservation.HotelName} Ticket",
            fontTitle,
            navyBrush,
            new XRect(left + 35, y - 2, width - 150, 18),
            XStringFormats.TopLeft);

        gfx.DrawString(
            $"Reference: {reservation.BookingReference}",
            fontSubtitle,
            greyBrush,
            new XRect(left + 35, y + 16, width - 150, 12),
            XStringFormats.TopLeft);

        // Draw HOTEL Badge (top right)
        var badgeRect = new XRect(right - 60, y + 2, 60, 20);
        gfx.DrawRectangle(lightGreyBrush, badgeRect);
        gfx.DrawRectangle(borderPen, badgeRect);
        gfx.DrawString(
            "HOTEL",
            fontPill,
            navyBrush,
            badgeRect,
            XStringFormats.Center);

        y += 45;

        // -------------------------------------------------------------
        // THREE CARDS ROW: ROUTE | DEPARTURE | STATUS
        // -------------------------------------------------------------
        double cardW = (width - 20) / 3.0;
        double rowH = 65;

        // Route Card
        var r1 = new XRect(left, y, cardW, rowH);
        gfx.DrawRectangle(lightGreyBrush, r1);
        gfx.DrawRectangle(borderPen, r1);
        gfx.DrawString("STAY", fontCardTitle, greyBrush, new XRect(left + 12, y + 10, cardW - 24, 10), XStringFormats.TopLeft);
        gfx.DrawString($"{reservation.HotelName} to {reservation.CityCode}", fontCardValue, navyBrush, new XRect(left + 12, y + 22, cardW - 24, 14), XStringFormats.TopLeft);
        gfx.DrawString(GetRoomCategory(reservation.OfferId), fontCardSub, greyBrush, new XRect(left + 12, y + 40, cardW - 24, 12), XStringFormats.TopLeft);

        // Departure Card
        double x2 = left + cardW + 10;
        var r2 = new XRect(x2, y, cardW, rowH);
        gfx.DrawRectangle(lightGreyBrush, r2);
        gfx.DrawRectangle(borderPen, r2);
        gfx.DrawString("CHECK IN", fontCardTitle, greyBrush, new XRect(x2 + 12, y + 10, cardW - 24, 10), XStringFormats.TopLeft);
        gfx.DrawString(reservation.CheckInDate.ToString("dd MMM yyyy"), fontCardValue, navyBrush, new XRect(x2 + 12, y + 22, cardW - 24, 14), XStringFormats.TopLeft);
        gfx.DrawString($"Check Out: {reservation.CheckOutDate.ToString("dd MMM yyyy")}", fontCardSub, greyBrush, new XRect(x2 + 12, y + 40, cardW - 24, 12), XStringFormats.TopLeft);

        // Status Card
        double x3 = x2 + cardW + 10;
        var r3 = new XRect(x3, y, cardW, rowH);
        gfx.DrawRectangle(lightGreyBrush, r3);
        gfx.DrawRectangle(borderPen, r3);
        gfx.DrawString("STATUS", fontCardTitle, greyBrush, new XRect(x3 + 12, y + 10, cardW - 24, 10), XStringFormats.TopLeft);
        gfx.DrawString(reservation.Status, fontCardValue, navyBrush, new XRect(x3 + 12, y + 22, cardW - 24, 14), XStringFormats.TopLeft);
        
        var bookedTime = ToIst(reservation.CreatedAt).ToString("dd MMM yyyy, hh:mm tt").ToLower();
        gfx.DrawString($"Booked at {bookedTime}", fontCardSub, greyBrush, new XRect(x3 + 12, y + 40, cardW - 24, 12), XStringFormats.TopLeft);

        y += rowH + 20;

        // -------------------------------------------------------------
        // PASSENGERS SECTION
        // -------------------------------------------------------------
        gfx.DrawString("Passengers", fontSectionHeader, navyBrush, left, y);
        y += 15;

        var passRect = new XRect(left, y, width, 32);
        gfx.DrawRectangle(lightGreyBrush, passRect);
        gfx.DrawRectangle(borderPen, passRect);
        gfx.DrawString(
            $"{reservation.GuestName} - Primary Guest",
            fontTextReg,
            navyBrush,
            new XRect(left + 12, y + 10, width / 2, 12),
            XStringFormats.TopLeft);

        string bedType = "King Bed";
        gfx.DrawString(
            $"Seat {bedType}",
            fontTextBold,
            navyBrush,
            new XRect(right - 150, y + 10, 138, 12),
            XStringFormats.TopRight);

        y += 32 + 20;

        // -------------------------------------------------------------
        // CONTACT AND DELIVERY SECTION
        // -------------------------------------------------------------
        gfx.DrawString("Contact and Delivery", fontSectionHeader, navyBrush, left, y);
        y += 15;

        double tableY = y;
        double rowHeight = 22;

        var contactFields = new (string Label, string Value)[]
        {
            ("Seats", GetRoomCategory(reservation.OfferId)),
            ("Email", reservation.GuestEmail),
            ("Mobile", reservation.GuestPhone),
            ("WhatsApp", "Not selected"),
            ("Payment Method", "Wallet")
        };

        double tableH = contactFields.Length * rowHeight;
        gfx.DrawRectangle(borderPen, left, tableY, width, tableH);

        for (int i = 0; i < contactFields.Length; i++)
        {
            double ry = tableY + (i * rowHeight);
            if (i > 0)
            {
                gfx.DrawLine(borderPen, left, ry, right, ry);
            }

            gfx.DrawString(
                contactFields[i].Label,
                fontTextReg,
                greyBrush,
                new XRect(left + 12, ry + 5, width / 2, 12),
                XStringFormats.TopLeft);

            gfx.DrawString(
                contactFields[i].Value,
                fontTextBold,
                navyBrush,
                new XRect(right - 200, ry + 5, 188, 12),
                XStringFormats.TopRight);
        }

        y += tableH + 20;

        // -------------------------------------------------------------
        // CONFIRMATION DELIVERY STATUS SECTION
        // -------------------------------------------------------------
        gfx.DrawString("Confirmation Delivery Status", fontSectionHeader, navyBrush, left, y);
        y += 15;

        double deliveryY = y;
        var deliveryFields = new (string Label, string Value)[]
        {
            ("Email Confirmation", "Queued"),
            ("SMS Confirmation", "Queued"),
            ("WhatsApp Confirmation", "Skipped")
        };

        double deliveryH = deliveryFields.Length * rowHeight;
        gfx.DrawRectangle(borderPen, left, deliveryY, width, deliveryH);

        for (int i = 0; i < deliveryFields.Length; i++)
        {
            double ry = deliveryY + (i * rowHeight);
            if (i > 0)
            {
                gfx.DrawLine(borderPen, left, ry, right, ry);
            }

            gfx.DrawString(
                deliveryFields[i].Label,
                fontTextReg,
                greyBrush,
                new XRect(left + 12, ry + 5, width / 2, 12),
                XStringFormats.TopLeft);

            gfx.DrawString(
                deliveryFields[i].Value,
                fontTextBold,
                navyBrush,
                new XRect(right - 200, ry + 5, 188, 12),
                XStringFormats.TopRight);
        }

        y += deliveryH + 25;

        // -------------------------------------------------------------
        // PRICE BREAKDOWN SECTION
        // -------------------------------------------------------------
        gfx.DrawLine(borderPen, left, y, right, y);
        y += 10;

        var priceFieldsList = new List<(string Label, string Value)>();
        if (reservation.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            priceFieldsList.Add(("Original Total Paid", $"INR {reservation.TotalPrice:N2}"));
            priceFieldsList.Add(("Cancellation Charges", $"INR {reservation.CancellationCharges:N2}"));
            priceFieldsList.Add(("Refund Amount", $"INR {reservation.RefundAmount:N2}"));
        }
        else
        {
            priceFieldsList.Add(("Base Fare", $"INR {reservation.BasePrice:N2}"));
            priceFieldsList.Add(("Convenience Fee", $"INR {reservation.ConvenienceFee:N2}"));
            priceFieldsList.Add(("Discount", $"INR {reservation.CouponDiscount:N2}"));
        }

        for (int i = 0; i < priceFieldsList.Count; i++)
        {
            gfx.DrawString(
                priceFieldsList[i].Label,
                fontTextReg,
                greyBrush,
                new XRect(left, y, 200, 14),
                XStringFormats.TopLeft);

            gfx.DrawString(
                priceFieldsList[i].Value,
                fontTextReg,
                navyBrush,
                new XRect(right - 200, y, 200, 14),
                XStringFormats.TopRight);

            y += 18;
        }

        gfx.DrawLine(borderPen, left, y, right, y);
        y += 8;

        if (reservation.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            gfx.DrawString(
                "Total Refunded",
                fontCardValue,
                navyBrush,
                new XRect(left, y, 200, 18),
                XStringFormats.TopLeft);

            gfx.DrawString(
                $"INR {reservation.RefundAmount:N2}",
                fontCardValue,
                navyBrush,
                new XRect(right - 200, y, 200, 18),
                XStringFormats.TopRight);
        }
        else
        {
            gfx.DrawString(
                "Total Paid",
                fontCardValue,
                navyBrush,
                new XRect(left, y, 200, 18),
                XStringFormats.TopLeft);

            gfx.DrawString(
                $"INR {reservation.TotalPrice:N2}",
                fontCardValue,
                navyBrush,
                new XRect(right - 200, y, 200, 18),
                XStringFormats.TopRight);
        }

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
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
        return DateTime.SpecifyKind(
            utc,
            DateTimeKind.Utc).AddHours(5.5);
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


