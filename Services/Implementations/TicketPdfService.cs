
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
                ? "PickNBook Flight Ticket" 
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

    public byte[] GenerateBusTicketPdf(SendBusTicketEmailRequest request)
    {
        using var document = new PdfDocument();
        document.Info.Title = $"Bus Ticket - {request.BookingReference}";

        BuildBusTicketPage(document, request);
        BuildBusTermsPage(document, request);

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    private static void BuildBusTicketPage(
        PdfDocument document,
        SendBusTicketEmailRequest req)
    {
        var page = document.AddPage();

        page.Width = XUnit.FromPoint(650);

        page.Height = XUnit.FromPoint(
            Math.Max(
                500,
                360 + ((req.Passengers?.Count ?? 1) * 28)));

        using var gfx = XGraphics.FromPdfPage(page);

        var navyBrush = new XSolidBrush(XColor.FromArgb(14, 36, 89));
        var blueBrush = new XSolidBrush(XColor.FromArgb(17, 66, 173));
        var grayBrush = new XSolidBrush(XColor.FromArgb(120, 130, 155));
        var lightGrayPen = new XPen(XColor.FromArgb(210, 218, 235), 0.8);
        var dashedPen = new XPen(XColor.FromArgb(180, 195, 220), 0.8);

        double pageW = page.Width;
        double left = 28;
        double mainW = pageW - 28 - 155;
        double sideX = pageW - 145;

        var fCity = new XFont("Arial", 24, XFontStyle.Bold);
        var fTitle = new XFont("Arial", 13, XFontStyle.Bold);
        var fBold = new XFont("Arial", 10, XFontStyle.Bold);
        var fReg = new XFont("Arial", 10, XFontStyle.Regular);
        var fSmall = new XFont("Arial", 8, XFontStyle.Regular);
        var fSmallB = new XFont("Arial", 8, XFontStyle.Bold);
        var fTiny = new XFont("Arial", 7, XFontStyle.Regular);
        var fTinyB = new XFont("Arial", 7, XFontStyle.Bold);
        var fKicker = new XFont("Arial", 7, XFontStyle.Bold);

        gfx.DrawRectangle(navyBrush, 0, 0, pageW, 56);

        gfx.DrawString(
            $"{req.OperatorName.ToUpperInvariant()} - BUS TICKET",
            fTiny,
            XBrushes.LightGray,
            new XRect(left, 9, mainW - left, 13),
            XStringFormats.TopLeft);

        gfx.DrawString(
            $"{req.BusType} - {req.BookingReference}",
            fTitle,
            XBrushes.White,
            new XRect(left, 23, mainW - left, 22),
            XStringFormats.TopLeft);

        double badgeX = sideX - 4;

        gfx.DrawRectangle(
            new XPen(XBrushes.White, 1.2),
            badgeX,
            16,
            76,
            22);

        gfx.DrawString(
            "BOOKED",
            new XFont("Arial", 9, XFontStyle.Bold),
            XBrushes.White,
            new XRect(badgeX, 16, 76, 22),
            XStringFormats.Center);

        double y = 72;

        gfx.DrawString(
            req.Origin,
            fCity,
            navyBrush,
            new XRect(left, y, 165, 36),
            XStringFormats.TopLeft);

        // Sub-text for Boarding Point (below Origin)
        gfx.DrawString(
            string.IsNullOrWhiteSpace(req.BoardingPoint) ? req.Origin : req.BoardingPoint,
            fTiny,
            grayBrush,
            new XRect(left, y + 28, 165, 12),
            XStringFormats.TopLeft);

        double centerX = left + 170;

        int hrs = req.DurationMinutes / 60;
        int mins = req.DurationMinutes % 60;

        string dur = $"{hrs}h {mins:00}m";

        gfx.DrawString(
            dur,
            fTiny,
            grayBrush,
            new XRect(centerX, y + 2, 80, 13),
            XStringFormats.TopCenter);

        gfx.DrawLine(lightGrayPen, centerX + 4, y + 17, centerX + 76, y + 17);

        gfx.DrawString(
            "›",
            new XFont("Arial", 14, XFontStyle.Regular),
            grayBrush,
            new XRect(centerX + 68, y + 8, 14, 14),
            XStringFormats.TopLeft);

        gfx.DrawString(
            req.IsOvernightArrival ? "OVERNIGHT" : "SAME DAY",
            fTiny,
            grayBrush,
            new XRect(centerX, y + 22, 80, 11),
            XStringFormats.TopCenter);

        gfx.DrawString(
            req.Destination,
            fCity,
            navyBrush,
            new XRect(centerX + 85, y, 165, 36),
            XStringFormats.TopLeft);

        // Sub-text for Arrival Point (below Destination)
        gfx.DrawString(
            string.IsNullOrWhiteSpace(req.ArrivalPoint) ? req.Destination : req.ArrivalPoint,
            fTiny,
            grayBrush,
            new XRect(centerX + 85, y + 28, 165, 12),
            XStringFormats.TopLeft);

        y += 52;

        gfx.DrawLine(lightGrayPen, left, y, mainW, y);
        y += 12;

        // ─────────────────────────────────────────────────────────────
        // 4-COLUMN ROW: DEPARTURE | DATE | ARRIVAL | ARRIVAL DATE
        // ─────────────────────────────────────────────────────────────
        double col1 = left;
        double col2 = left + 115;
        double col3 = left + 250;
        double col4 = left + 365;

        gfx.DrawString("DEPARTURE", fTiny, grayBrush, col1, y);
        gfx.DrawString("DATE", fTiny, grayBrush, col2, y);
        gfx.DrawString("ARRIVAL", fTiny, grayBrush, col3, y);
        gfx.DrawString("ARRIVAL DATE", fTiny, grayBrush, col4, y);

        y += 13;

        gfx.DrawString(
            ToIst(req.BoardingPointTime).ToString("HH:mm"),
            fBold,
            navyBrush,
            col1,
            y);

        gfx.DrawString(
            ToIst(req.BoardingPointTime).ToString("ddd, dd MMM, yyyy"),
            fBold,
            navyBrush,
            col2,
            y);

        gfx.DrawString(
            ToIst(req.ArrivalPointTime).ToString("HH:mm"),
            fBold,
            navyBrush,
            col3,
            y);

        gfx.DrawString(
            ToIst(req.ArrivalPointTime).ToString("ddd, dd MMM, yyyy"),
            fBold,
            navyBrush,
            col4,
            y);

        y += 32;

        DrawDashedLine(gfx, dashedPen, left, y, mainW);
        y += 12;

        // ─────────────────────────────────────────────────────────────
        // 4-COLUMN ROW: BUS TYPE | BOARDING | ARRIVAL PLACE | TOTAL FARE
        // ─────────────────────────────────────────────────────────────
        gfx.DrawString("BUS TYPE", fTiny, grayBrush, col1, y);
        gfx.DrawString("BOARDING", fTiny, grayBrush, col2, y);
        gfx.DrawString("ARRIVAL PLACE", fTiny, grayBrush, col3, y);
        gfx.DrawString("TOTAL FARE", fTiny, grayBrush, col4, y);

        y += 13;

        gfx.DrawString(
            string.IsNullOrWhiteSpace(req.BusType) ? "–" : req.BusType,
            fBold,
            navyBrush,
            col1,
            y);

        gfx.DrawString(
            string.IsNullOrWhiteSpace(req.BoardingPoint) ? req.Origin : req.BoardingPoint,
            fBold,
            navyBrush,
            col2,
            y);

        gfx.DrawString(
            string.IsNullOrWhiteSpace(req.ArrivalPoint) ? req.Destination : req.ArrivalPoint,
            fBold,
            navyBrush,
            col3,
            y);

        gfx.DrawString(
            $"{req.Currency} {req.Price:0.##}",
            fBold,
            navyBrush,
            col4,
            y);

        y += 28;

        DrawDashedLine(gfx, dashedPen, left, y, mainW);
        y += 14;

        gfx.DrawString("PASSENGERS & SEATS", fTiny, grayBrush, left, y);

        y += 14;

        foreach (var (p, i) in
      (req.Passengers ?? new List<BusPassengerSeatDto>())
      .Select((passenger, index) => (passenger, index)))
        {
            gfx.DrawEllipse(navyBrush, left, y, 16, 16);

            gfx.DrawString(
                (i + 1).ToString(),
                fTinyB,
                XBrushes.White,
                new XRect(left, y, 16, 16),
                XStringFormats.Center);

            gfx.DrawString(
                $"{p.FullName} ({p.Gender})",
                fReg,
                navyBrush,
                new XRect(left + 22, y, mainW - left - 90, 16),
                XStringFormats.TopLeft);

            double seatBadgeX = mainW - 55;

            gfx.DrawRectangle(lightGrayPen, seatBadgeX, y - 1, 52, 17);

            gfx.DrawString(
                p.SeatNumber,
                fSmallB,
                blueBrush,
                new XRect(seatBadgeX, y - 1, 52, 17),
                XStringFormats.Center);

            y += 22;
        }

        if (!(req.Passengers?.Any() ?? false))
        {
            gfx.DrawString(
                $"Seat: {req.SeatNumber}",
                fReg,
                navyBrush,
                new XRect(left, y, mainW - left, 16),
                XStringFormats.TopLeft);

            y += 20;
        }

        for (double dy = 62; dy < 480; dy += 7)
        {
            gfx.DrawLine(
                dashedPen,
                sideX - 12,
                dy,
                sideX - 12,
                Math.Min(dy + 4, 480));
        }

        double sY = 72;
        double sw = 130;

        gfx.DrawString("PNR", fKicker, grayBrush, sideX, sY);

        sY += 13;

        var pnrFont = new XFont("Arial", 9, XFontStyle.Bold);
        var pnrBrush = new XSolidBrush(XColor.FromArgb(17, 66, 173));

        gfx.DrawString(
            req.Pnr,
            pnrFont,
            pnrBrush,
            new XRect(sideX, sY, sw, 36),
            XStringFormats.TopLeft);

        sY += 38;

        gfx.DrawString(
            "TOTAL FARE",
            fKicker,
            grayBrush,
            sideX,
            sY);

        sY += 13;

        gfx.DrawString(
            $"{req.Currency} {req.Price:0.00}",
            fBold,
            navyBrush,
            sideX,
            sY);

        sY += 22;

        gfx.DrawString(
            "GST INCLUDED",
            fKicker,
            grayBrush,
            sideX,
            sY);

        sY += 13;

        gfx.DrawString(
            $"{req.Currency} {req.GstAmount:0.00}",
            fBold,
            navyBrush,
            sideX,
            sY);

        sY += 22;

        try
        {
            using var qrGenerator = new QRCodeGenerator();

            var qrData = qrGenerator.CreateQrCode(
                req.BookingReference,
                QRCodeGenerator.ECCLevel.Q);

            using var qrCode = new PngByteQRCode(qrData);

            byte[] qrPng = qrCode.GetGraphic(4);

            var xImg = XImage.FromStream(() => new MemoryStream(qrPng));

            double qrSize = 90;

            gfx.DrawImage(xImg, sideX, sY, qrSize, qrSize);

            sY += qrSize + 6;

            gfx.DrawString(
                "Show to conductor",
                fTiny,
                grayBrush,
                new XRect(sideX, sY, sw, 12),
                XStringFormats.TopCenter);
        }
        catch
        {
            gfx.DrawRectangle(lightGrayPen, sideX, sY, 90, 90);

            gfx.DrawString(
                "QR Code",
                fSmall,
                grayBrush,
                new XRect(sideX, sY, 90, 90),
                XStringFormats.Center);
        }

        gfx.DrawLine(lightGrayPen, left, 480, pageW - 20, 480);

        gfx.DrawString(
            "Please carry a valid government ID. Report 30 minutes before departure. This is a system-generated ticket.",
            fTiny,
            grayBrush,
            new XRect(left, 483, pageW - 40, 14),
            XStringFormats.TopLeft);
    }

    private static void BuildBusTermsPage(
        PdfDocument document,
        SendBusTicketEmailRequest req)
    {
       
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(650);
        page.Height = XUnit.FromPoint(500);

        using var gfx = XGraphics.FromPdfPage(page);

        var navyBrush = new XSolidBrush(XColor.FromArgb(14, 36, 89));
        var blueBrush = new XSolidBrush(XColor.FromArgb(17, 66, 173));
        var grayBrush = new XSolidBrush(XColor.FromArgb(100, 115, 140));
        var greenBrush = new XSolidBrush(XColor.FromArgb(22, 130, 72));
        var cardBorderPen = new XPen(XColor.FromArgb(190, 205, 230), 1.0);
        var bgBrush = new XSolidBrush(XColor.FromArgb(246, 249, 255));

        var fH2 = new XFont("Arial", 9, XFontStyle.Bold);
        var fReg = new XFont("Arial", 8, XFontStyle.Regular);
        var fBold = new XFont("Arial", 8, XFontStyle.Bold);
        var fSmall = new XFont("Arial", 7, XFontStyle.Regular);
        var fTiny = new XFont("Arial", 7, XFontStyle.Bold);

        double pageW = page.Width;

        gfx.DrawRectangle(navyBrush, 0, 0, pageW, 40);
        gfx.DrawString(
            "Bus Terms & Conditions",
            new XFont("Arial", 13, XFontStyle.Bold),
            XBrushes.White,
            new XRect(20, 0, pageW - 40, 40),
            XStringFormats.CenterLeft);

        gfx.DrawRectangle(bgBrush, 0, 40, pageW, page.Height - 40);

        double leftCol = 20;
        double leftColW = pageW / 2 - 30;
        double rightCol = pageW / 2 + 10;
        double rightColW = pageW / 2 - 30;
        double y = 55;

        void TermsSection(string title, string[] bullets)
        {
            gfx.DrawString(title, fH2, navyBrush, leftCol, y);
            y += 14;

            foreach (var b in bullets)
            {
                gfx.DrawString("•", fReg, grayBrush, leftCol + 2, y);

                gfx.DrawString(
                    b,
                    fReg,
                    grayBrush,
                    new XRect(leftCol + 12, y, leftColW - 12, 11),
                    XStringFormats.TopLeft);

                y += 12;
            }

            y += 6;
        }

        TermsSection("1. BOARDING & TIMING", new[]
        {
            "Report 30 minutes before departure",
            "Valid photo ID required",
            "No entry after departure time"
        });

        TermsSection("2. LUGGAGE POLICY", new[]
        {
            "Complimentary: 20kg",
            "Excess: INR 100 per kg",
            "No bulky items allowed"
        });

        var cancellationBullets = new List<string>();
        if (!string.IsNullOrWhiteSpace(req.CancellationPoliciesJson))
        {
            try
            {
                var jsonStr = req.CancellationPoliciesJson.Trim();
                if (jsonStr.StartsWith("\"") && jsonStr.EndsWith("\"") && !jsonStr.StartsWith("\"["))
                {
                    jsonStr = jsonStr.Trim('"');
                    if (jsonStr.Length > 0 && jsonStr.Length % 4 == 0)
                    {
                        try
                        {
                            var bytes = Convert.FromBase64String(jsonStr);
                            jsonStr = System.Text.Encoding.UTF8.GetString(bytes);
                        }
                        catch { /* not valid base64 */ }
                    }
                }

                var options = new System.Text.Json.JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                };
                var policies = System.Text.Json.JsonSerializer.Deserialize<List<SrdvCancellationPolicyDto>>(jsonStr, options);
                if (policies != null && policies.Any())
                {
                    // Clean up and format the policy strings if necessary
                    cancellationBullets.AddRange(policies.Select(p => !string.IsNullOrWhiteSpace(p.PolicyString) ? p.PolicyString : $"Cancel {p.TimeBeforeDept} hrs: {p.CancellationCharge} {p.CancellationChargeType}"));
                }
            }
            catch { }
        }
        
        if (cancellationBullets.Count == 0)
        {
            cancellationBullets.Add("Cancel 12+ hours: 100% refund");
            cancellationBullets.Add("Cancel 6–12 hours: 75% refund");
            cancellationBullets.Add("Cancel <6 hours: 50% refund");
        }

        TermsSection("3. CANCELLATION TERMS", cancellationBullets.ToArray());

        double rY = 55;

        bool hasDiscount =
            req.AutoDiscountAmount > 0 ||
            req.CouponDiscountAmount > 0;

        string panelTitle =
            hasDiscount
            ? "DISCOUNTS APPLIED & FARE BREAKDOWN"
            : "FARE BREAKDOWN";

        gfx.DrawString(
            panelTitle,
            fTiny,
            blueBrush,
            new XRect(rightCol, rY, rightColW, 14),
            XStringFormats.TopCenter);

        rY += 18;

        double cardH = hasDiscount ? 175 : 100;

        gfx.DrawRectangle(
            cardBorderPen,
            XBrushes.White,
            rightCol,
            rY,
            rightColW,
            cardH);

        rY += 10;

        void FareRow(
            string label,
            string value,
            XFont font,
            XBrush brush,
            bool isTotal = false)
        {
            if (isTotal)
            {
                gfx.DrawLine(
                    new XPen(XColor.FromArgb(210, 218, 235), 0.6),
                    rightCol + 8,
                    rY - 2,
                    rightCol + rightColW - 8,
                    rY - 2);

                rY += 4;
            }

            gfx.DrawString(
                label,
                font,
                brush,
                new XRect(rightCol + 10, rY, rightColW - 20, 12),
                XStringFormats.TopLeft);

            gfx.DrawString(
                value,
                font,
                brush,
                new XRect(rightCol + 10, rY, rightColW - 20, 12),
                XStringFormats.TopRight);

            rY += 14;
        }

        FareRow(
            "Base Fare",
            $"₹ {(req.NetFare > 0 ? req.NetFare : req.Price):0.00}",
            fReg,
            grayBrush);

        if (hasDiscount)
        {
            decimal totalSavings =
                req.AutoDiscountAmount +
                req.CouponDiscountAmount;

            string promotionTitle =
                string.IsNullOrWhiteSpace(req.AppliedPromotionCode)
                ? "OFFERS APPLIED"
                : $"🎟 {req.AppliedPromotionCode.ToUpperInvariant()}";

            gfx.DrawRectangle(
                new XPen(XColor.FromArgb(22, 130, 72), 0.8),
                new XSolidBrush(XColor.FromArgb(240, 253, 244)),
                rightCol + 10,
                rY,
                rightColW - 20,
                28);

            gfx.DrawString(
                promotionTitle,
                fBold,
                greenBrush,
                new XRect(rightCol + 14, rY + 4, rightColW - 28, 10),
                XStringFormats.TopLeft);

            gfx.DrawString(
                $"YOU SAVED ₹{totalSavings:0}",
                fSmall,
                greenBrush,
                new XRect(rightCol + 14, rY + 15, rightColW - 28, 10),
                XStringFormats.TopLeft);

            rY += 34;

            if (req.AutoDiscountAmount > 0)
            {
                FareRow(
                    "Offer Discount",
                    $"- ₹ {req.AutoDiscountAmount:0.00}",
                    fReg,
                    greenBrush);
            }

            if (req.CouponDiscountAmount > 0)
            {
                FareRow(
                    "Coupon Discount",
                    $"- ₹ {req.CouponDiscountAmount:0.00}",
                    fReg,
                    greenBrush);
            }
        }

        FareRow(
            "GST",
            $"+ ₹ {req.GstAmount:0.00}",
            fReg,
            grayBrush);


        FareRow(
            "Total Fare",
            $"₹ {req.Price:0.00}",
            new XFont("Arial", 9, XFontStyle.Bold),
            navyBrush,
            isTotal: true);
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


