using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Helpers;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using QRCoder;

namespace PickNBook.Api.Services;

public class TicketEmailService : ITicketEmailService
{
    private readonly IEmailService _emailService;
    private readonly ITicketPdfService _ticketPdfService;
    private readonly AppDbContext _context;

    public TicketEmailService(
        IEmailService emailService,
        ITicketPdfService ticketPdfService,
        AppDbContext context)
    {
        _emailService = emailService;
        _ticketPdfService = ticketPdfService;
        _context = context;
    }


    public async Task SendFlightTicketAsync(
        SendFlightTicketEmailRequest request)
    {
        var agentReservationToCheck = await _context.FlightReservations
            .FirstOrDefaultAsync(r => r.BookingReference == request.BookingReference);

        if (agentReservationToCheck != null && !string.IsNullOrEmpty(agentReservationToCheck.UserId))
        {
            var userObj = await _context.Users.FirstOrDefaultAsync(u => u.Id.ToString() == agentReservationToCheck.UserId);
            if (userObj != null && userObj.Role == AuthRoles.Agent)
            {
                request.AgentCompanyName = userObj.CompanyName;
                request.AgentLogoUrl = userObj.AgentLogoUrl;
            }
        }

        var pdfs = _ticketPdfService.GenerateFlightTicketPdf(request, isCancelled: false);

        var attachments = pdfs.Select(p => new EmailAttachment
        {
            FileName = p.FileName,
            ContentType = "application/pdf",
            Content = p.Content
        }).ToList();

        var subject = $"Your Pick&book Ticket - {request.BookingReference}";

        var reservation = await _context.FlightReservations
            .FirstOrDefaultAsync(r => r.BookingReference == request.BookingReference);

        var passengersList = new List<FlightPassengerTicketDto>();
        string flightNumber = "N/A";
        string airline = request.Airline;

        if (reservation != null)
        {
            flightNumber = reservation.FlightNumber;
            airline = reservation.Airline;

            var dbPassengers = await _context.FlightReservationPassengers
                .Where(p => p.FlightReservationId == reservation.Id)
                .ToListAsync();

            if (dbPassengers.Count > 0)
            {
                passengersList = dbPassengers.Select(p => new FlightPassengerTicketDto
                {
                    FullName = p.FullName,
                    SeatNumber = p.SeatNumber,
                    TicketNumber = p.TicketNumber,
                    Status = p.Status
                }).ToList();
            }
        }

        if (passengersList.Count == 0)
        {
            passengersList.Add(new FlightPassengerTicketDto
            {
                FullName = request.PassengerName,
                SeatNumber = request.SeatNumber ?? "12A"
            });
        }

        if (string.IsNullOrWhiteSpace(flightNumber) || flightNumber == "N/A")
        {
            flightNumber = !string.IsNullOrWhiteSpace(request.Pnr) ? request.Pnr : "6E-437";
        }

        var body = BuildFlightTicketEmailHtml(request, passengersList, airline, flightNumber);

        await _emailService.SendEmailWithAttachmentsAsync(
            request.ToEmail,
            subject,
            body,
            attachments);
    }

    private static string GetAirlineCode(string airline, string flightNumber)
    {
        if (airline.Contains("Fly91", StringComparison.OrdinalIgnoreCase)) return "IC";
        if (airline.Contains("Vistara", StringComparison.OrdinalIgnoreCase)) return "UK";
        if (airline.Contains("IndiGo", StringComparison.OrdinalIgnoreCase)) return "6E";
        if (airline.Contains("Air India", StringComparison.OrdinalIgnoreCase)) return "AI";
        if (airline.Contains("SpiceJet", StringComparison.OrdinalIgnoreCase)) return "SG";
        if (airline.Contains("Akasa", StringComparison.OrdinalIgnoreCase)) return "QP";
        if (!string.IsNullOrWhiteSpace(flightNumber) && flightNumber.Length >= 2)
        {
            var prefix = flightNumber.Substring(0, 2).ToUpper();
            if (char.IsLetter(prefix[0]) && (char.IsLetter(prefix[1]) || char.IsDigit(prefix[1]))) return prefix;
        }
        return "6E";
    }

    private static string BuildFlightTicketEmailHtml(
        SendFlightTicketEmailRequest req,
        List<FlightPassengerTicketDto> passengers,
        string airline,
        string flightNumber)
    {
        var depIst = ToIst(req.DepartureTime);
        var arrIst = ToIst(req.ArrivalTime);
        var depDateStr = depIst.ToString("ddd, dd MMMM yyyy");
        var originCity = GetCityName(req.Origin);
        var destCity = GetCityName(req.Destination);

        var segList = req.Segments != null && req.Segments.Any()
            ? req.Segments
            : new List<FlightTicketSegmentDto>
            {
                new FlightTicketSegmentDto
                {
                    Airline = airline,
                    FlightNumber = flightNumber,
                    FromCity = req.Origin,
                    ToCity = req.Destination,
                    DepartureTime = req.DepartureTime,
                    ArrivalTime = req.ArrivalTime,
                    Pnr = req.Pnr
                }
            };

        var firstSeg = segList.First();
        string airCode = GetAirlineCode(firstSeg.Airline, firstSeg.FlightNumber);
        string logoUrl = "https://images.kiwi.com/airlines/64/" + airCode + ".png";
        string fallbackLogoUrl = "https://imgak.mmtcdn.com/flights/assets/media/dt/common/icons/" + airCode + ".png";
        string pnrVal = !string.IsNullOrWhiteSpace(req.Pnr) ? req.Pnr : (firstSeg.Pnr ?? "BLKT23");

        var sbSegments = new System.Text.StringBuilder();
        foreach (var seg in segList)
        {
            var sDep = ToIst(seg.DepartureTime);
            var sArr = ToIst(seg.ArrivalTime);
            var sDur = sArr > sDep ? $"{(int)(sArr - sDep).TotalHours}h {(sArr - sDep).Minutes}m" : "1h 01m";
            string sCode = GetAirlineCode(seg.Airline, seg.FlightNumber);
            string sLogo = "https://images.kiwi.com/airlines/64/" + sCode + ".png";
            string sFb = "https://imgak.mmtcdn.com/flights/assets/media/dt/common/icons/" + sCode + ".png";
            string segPnr = !string.IsNullOrWhiteSpace(seg.Pnr) ? seg.Pnr : pnrVal;
            string segOriginCity = GetCityName(seg.FromCity);
            string segDestCity = GetCityName(seg.ToCity);
            string segOriginAirport = GetAirportName(seg.FromCity);
            string segDestAirport = GetAirportName(seg.ToCity);

            sbSegments.Append(
                "<div style=\"border: 1px solid #e2e8f0; border-radius: 14px; margin-bottom: 16px; background-color: #ffffff; overflow: hidden; box-shadow: 0 1px 4px rgba(0,0,0,0.03);\">" +
                
                "<!-- Top Airline Strip (Full Width) -->" +
                "<div style=\"background-color: #f8fafc; border-bottom: 1px solid #e2e8f0; padding: 12px 14px;\">" +
                "<table style=\"width: 100%; border-collapse: collapse;\"><tr>" +
                "<td style=\"width: 44px; vertical-align: middle;\">" +
                "<div style=\"width: 40px; height: 40px; border-radius: 10px; background: #ffffff; border: 1px solid #e2e8f0; text-align: center; line-height: 40px; overflow: hidden;\">" +
                "<img src=\"" + sLogo + "\" alt=\"" + seg.Airline + "\" style=\"width: 32px; height: 32px; vertical-align: middle; object-fit: contain;\" onerror=\"this.onerror=null; this.src='" + sFb + "';\" />" +
                "</div></td>" +
                "<td style=\"padding-left: 10px; vertical-align: middle;\">" +
                "<div style=\"font-size: 13px; font-weight: 900; color: #091a3e; text-transform: uppercase;\">" + seg.Airline + "</div>" +
                "<div style=\"font-size: 11px; color: #64748b; font-weight: 600;\">" + seg.FlightNumber + " &bull; <span style=\"color: #94a3b8;\">Operated by " + seg.Airline + "</span></div>" +
                "</td>" +
                "<td style=\"text-align: right; vertical-align: middle;\">" +
                "<div style=\"display: inline-block; font-size: 10px; border: 1px solid #cbd5e1; border-radius: 6px; overflow: hidden;\">" +
                "<span style=\"background: #091a3e; color: #ffffff; font-weight: 800; padding: 2px 6px;\">PNR</span>" +
                "<span style=\"background: #ffffff; color: #091a3e; font-weight: 900; padding: 2px 8px; font-family: monospace;\">" + segPnr + "</span>" +
                "</div></td></tr></table></div>" +

                "<!-- Route Times Strip (Full Width, Mobile Responsive) -->" +
                "<div style=\"padding: 16px 14px;\">" +
                "<table style=\"width: 100%; border-collapse: collapse;\"><tr>" +
                "<td style=\"width: 42%; vertical-align: top;\">" +
                "<div style=\"font-size: 10px; color: #64748b; font-weight: 800; text-transform: uppercase; letter-spacing: 0.5px;\">DEPARTURE</div>" +
                "<div style=\"font-size: 18px; font-weight: 900; color: #091a3e; margin: 2px 0;\">" + sDep.ToString("HH:mm") + " <span style=\"font-size: 11px; font-weight: 700; color: #64748b;\">hrs</span></div>" +
                "<div style=\"font-size: 13px; font-weight: 800; color: #0f172a;\">" + segOriginCity + " (" + seg.FromCity + ")</div>" +
                "<div style=\"font-size: 10px; color: #94a3b8; margin-top: 1px;\">" + segOriginAirport + "</div>" +
                "</td>" +
                "<td style=\"width: 16%; text-align: center; vertical-align: middle;\">" +
                "<div style=\"font-size: 9px; color: #64748b; font-weight: 700;\">" + sDur + "</div>" +
                "<div style=\"color: #dc2626; font-size: 16px; font-weight: bold; margin: 2px 0;\">&rarr;</div>" +
                "<div style=\"font-size: 8px; color: #059669; font-weight: 800; text-transform: uppercase;\">Non-Stop</div>" +
                "</td>" +
                "<td style=\"width: 42%; text-align: right; vertical-align: top;\">" +
                "<div style=\"font-size: 10px; color: #64748b; font-weight: 800; text-transform: uppercase; letter-spacing: 0.5px;\">ARRIVAL</div>" +
                "<div style=\"font-size: 18px; font-weight: 900; color: #091a3e; margin: 2px 0;\">" + sArr.ToString("HH:mm") + " <span style=\"font-size: 11px; font-weight: 700; color: #64748b;\">hrs</span></div>" +
                "<div style=\"font-size: 13px; font-weight: 800; color: #0f172a;\">" + segDestCity + " (" + seg.ToCity + ")</div>" +
                "<div style=\"font-size: 10px; color: #94a3b8; margin-top: 1px;\">" + segDestAirport + "</div>" +
                "</td>" +
                "</tr></table></div>" +

                "<!-- Baggage & Fare Strip -->" +
                "<div style=\"background-color: #fafbfc; border-top: 1px solid #f1f5f9; padding: 10px 14px; font-size: 11px;\">" +
                "<table style=\"width: 100%; border-collapse: collapse;\"><tr>" +
                "<td style=\"color: #091a3e; font-weight: 700;\">" +
                "<span style=\"background: #e2e8f0; color: #1e293b; padding: 2px 7px; border-radius: 5px; font-size: 10px; margin-right: 4px;\">ECONOMY</span> " +
                "<span style=\"background: #fee2e2; color: #991b1b; padding: 2px 7px; border-radius: 5px; font-size: 10px;\">Regular Fare</span>" +
                "</td>" +
                "<td style=\"text-align: right; color: #475569; font-size: 10px; font-weight: 600;\">" +
                "Check-in: <b>15 Kgs (1 PC)</b> &bull; Cabin: <b>7 Kgs</b> per Adult" +
                "</td>" +
                "</tr></table></div>" +

                "</div>");
        }

        var sbPassengers = new System.Text.StringBuilder();
        int tktIdx = 7054;
        foreach (var p in passengers)
        {
            string seatVal = !string.IsNullOrWhiteSpace(p.SeatNumber) ? p.SeatNumber : "12A";
            string ticketVal = !string.IsNullOrWhiteSpace(p.TicketNumber) ? p.TicketNumber : ("228-380931" + (tktIdx++));
            string pType = !string.IsNullOrWhiteSpace(p.PassengerType) ? p.PassengerType : "Adult";

            sbPassengers.Append(
                "<div style=\"background-color: #ffffff; border: 1px solid #e2e8f0; border-radius: 12px; padding: 12px 14px; margin-bottom: 8px; box-shadow: 0 1px 3px rgba(0,0,0,0.02);\">" +
                "<table style=\"width: 100%; border-collapse: collapse;\"><tr>" +
                "<td style=\"vertical-align: middle;\">" +
                "<div style=\"font-size: 13px; font-weight: 800; color: #0f172a;\">" + p.FullName + "</div>" +
                "<div style=\"font-size: 11px; color: #64748b; margin-top: 2px;\">" + pType + " &bull; Standard Snack &bull; E-Ticket: <span style=\"font-family: monospace; color: #334155; font-weight: 700;\">" + ticketVal + "</span></div>" +
                "</td>" +
                "<td style=\"text-align: right; vertical-align: middle; width: 85px;\">" +
                "<div style=\"display: inline-block; background-color: #fff1f2; color: #dc2626; font-weight: 900; font-size: 12px; padding: 4px 10px; border-radius: 6px; border: 1px solid #fecaca; text-align: center;\">" +
                "SEAT " + seatVal +
                "</div>" +
                "</td></tr></table></div>");
        }

        decimal ref1 = Math.Max(0, req.Price - 1750);
        decimal ref2 = Math.Max(0, req.Price - 2250);

        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
               "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">" +
               "<meta name=\"x-apple-disable-message-reformatting\">" +
               "<title>Pick&amp;book Flight Ticket</title>" +
               "<style>" +
               "@media only screen and (max-width: 520px) {" +
               "  .email-container { width: 100% !important; margin: 0 !important; border-radius: 0 !important; }" +
               "  .header-padding { padding: 18px 14px !important; }" +
               "  .content-padding { padding: 16px 12px !important; }" +
               "}" +
               "</style>" +
               "</head>" +
               "<body style=\"font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f1f5f9; margin: 0; padding: 0; color: #0f172a;\">" +
               "<div class=\"email-container\" style=\"max-width: 600px; margin: 15px auto; background-color: #ffffff; border-radius: 16px; overflow: hidden; box-shadow: 0 4px 16px rgba(0,0,0,0.06); border: 1px solid #e2e8f0;\">" +
               
               "<!-- Header Banner -->" +
               "<div class=\"header-padding\" style=\"background: linear-gradient(135deg, #059669 0%, #047857 100%); color: #ffffff; padding: 20px; border-top: 4px solid #dc2626;\">" +
               "<table style=\"width: 100%; border-collapse: collapse;\"><tr>" +
               "<td style=\"vertical-align: middle;\">" +
               "<div style=\"background: #ffffff; border-radius: 10px; padding: 5px 10px; display: inline-block; margin-bottom: 10px;\">" +
               "<img src=\"https://www.picknbook.in/assets/picknbook-login-q5fv1iRs.png\" alt=\"Pick&amp;book\" style=\"height: 22px; width: auto; display: block; border: 0;\" />" +
               "</div>" +
               "<div style=\"font-size: 10px; font-weight: 800; text-transform: uppercase; letter-spacing: 0.8px; color: #a7f3d0;\">BOOKING CONFIRMED &bull; E-TICKET ISSUED</div>" +
               "<div style=\"font-size: 17px; font-weight: 900; margin-top: 3px; color: #ffffff; line-height: 1.3;\">Your " + originCity + " - " + destCity + " flight is booked for travel on " + depDateStr + "</div>" +
               "<div style=\"font-size: 11px; color: #d1fae5; margin-top: 4px;\">Thank you for choosing Pick&amp;book. We wish you a safe and pleasant journey!</div>" +
               "</td></tr></table></div>" +

               "<div class=\"content-padding\" style=\"padding: 18px 16px;\">" +
               
               "<!-- Trip details badge -->" +
               "<div style=\"margin-bottom: 14px;\">" +
               "<span style=\"display: inline-block; background-color: #091a3e; color: #ffffff; font-weight: 800; font-size: 10px; text-transform: uppercase; letter-spacing: 0.5px; padding: 6px 14px; border-radius: 6px;\">Your trip details</span>" +
               "</div>" +

               "<!-- Segments -->" +
               "<div style=\"margin-bottom: 16px;\">" +
               "<div style=\"font-size: 17px; font-weight: 900; color: #091a3e;\">" + originCity + " &rarr; " + destCity + "</div>" +
               "<div style=\"font-size: 11px; font-weight: 600; color: #64748b; margin-top: 2px; margin-bottom: 12px;\">" + depDateStr + " &bull; <span style=\"color: #059669; font-weight: 800;\">Non-stop</span> &bull; 1h 01m duration</div>" +
               sbSegments.ToString() +
               "</div>" +

               "<!-- Passenger Details -->" +
               "<div style=\"margin-bottom: 20px;\">" +
               "<div style=\"font-size: 12px; font-weight: 800; text-transform: uppercase; color: #091a3e; margin-bottom: 8px; letter-spacing: 0.5px;\">Passenger Details</div>" +
               sbPassengers.ToString() +
               "</div>" +

               "<!-- Dynamic Cancellation Policy (Mobile Clean Card Format) -->" +
               "<div style=\"background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 14px; padding: 16px; margin-bottom: 20px;\">" +
               "<div style=\"font-size: 12px; font-weight: 800; text-transform: uppercase; color: #091a3e; margin-bottom: 10px; letter-spacing: 0.5px;\">Dynamic Cancellation &amp; Refund Policy</div>" +

               "<!-- Tier 1 -->" +
               "<div style=\"background: #ffffff; border: 1px solid #e2e8f0; border-radius: 10px; padding: 10px 12px; margin-bottom: 8px;\">" +
               "<table style=\"width: 100%; border-collapse: collapse;\"><tr>" +
               "<td>" +
               "<div style=\"font-size: 11px; font-weight: 700; color: #0f172a;\">More than 72 hours before departure</div>" +
               "<div style=\"font-size: 10px; color: #64748b; margin-top: 2px;\">Airline Fee: &#8377;1,500.00 / pax &bull; Pick&amp;book Fee: &#8377;250.00</div>" +
               "</td>" +
               "<td style=\"text-align: right; vertical-align: middle;\">" +
               "<div style=\"font-size: 9px; color: #059669; font-weight: 700; text-transform: uppercase;\">EST. REFUND</div>" +
               "<div style=\"font-size: 14px; font-weight: 900; color: #059669;\">&#8377;" + ref1.ToString("0.00") + "</div>" +
               "</td></tr></table></div>" +

               "<!-- Tier 2 -->" +
               "<div style=\"background: #ffffff; border: 1px solid #e2e8f0; border-radius: 10px; padding: 10px 12px; margin-bottom: 8px;\">" +
               "<table style=\"width: 100%; border-collapse: collapse;\"><tr>" +
               "<td>" +
               "<div style=\"font-size: 11px; font-weight: 700; color: #0f172a;\">Between 24 to 72 hours before departure</div>" +
               "<div style=\"font-size: 10px; color: #64748b; margin-top: 2px;\">Airline Fee: &#8377;2,000.00 / pax &bull; Pick&amp;book Fee: &#8377;250.00</div>" +
               "</td>" +
               "<td style=\"text-align: right; vertical-align: middle;\">" +
               "<div style=\"font-size: 9px; color: #059669; font-weight: 700; text-transform: uppercase;\">EST. REFUND</div>" +
               "<div style=\"font-size: 14px; font-weight: 900; color: #059669;\">&#8377;" + ref2.ToString("0.00") + "</div>" +
               "</td></tr></table></div>" +

               "<!-- Tier 3 -->" +
               "<div style=\"background: #ffffff; border: 1px solid #e2e8f0; border-radius: 10px; padding: 10px 12px;\">" +
               "<table style=\"width: 100%; border-collapse: collapse;\"><tr>" +
               "<td>" +
               "<div style=\"font-size: 11px; font-weight: 700; color: #0f172a;\">0 to 24 hours / Flight No-Show</div>" +
               "<div style=\"font-size: 10px; color: #dc2626; margin-top: 2px;\">Base Fare Non-Refundable &bull; Pick&amp;book Fee: &#8377;250.00</div>" +
               "</td>" +
               "<td style=\"text-align: right; vertical-align: middle;\">" +
               "<div style=\"font-size: 11px; font-weight: 800; color: #091a3e;\">Taxes Only</div>" +
               "<div style=\"font-size: 10px; color: #64748b;\">Airport Taxes</div>" +
               "</td></tr></table></div>" +

               "<div style=\"font-size: 10px; color: #64748b; margin-top: 10px; line-height: 1.4;\">" +
               "&bull; Passenger Service Fee (PSF) and airport charges are 100% refundable per DGCA regulations.<br/>" +
               "&bull; Net refund is processed back to the original payment source within 3-5 business days." +
               "</div></div>" +

               "<!-- Instructions -->" +
               "<div style=\"background-color: #ffffff; border: 1px solid #e2e8f0; border-radius: 12px; padding: 14px; margin-bottom: 20px; font-size: 11px; color: #64748b; line-height: 1.5;\">" +
               "<div style=\"font-weight: 800; color: #091a3e; margin-bottom: 4px;\">Important Travel Instructions</div>" +
               "<div>&bull; Carry a valid government photo ID (Aadhaar, Passport, Voter ID) to the airport.</div>" +
               "<div>&bull; Check-in closes 45 mins and boarding gates close 20 mins prior to departure.</div>" +
               "<div>&bull; Your official PDF flight ticket is attached to this email for offline travel.</div></div>" +

               "</div>" +

               "<!-- Footer -->" +
               "<div class=\"content-padding\" style=\"background-color: #091a3e; color: #ffffff; padding: 18px 20px; border-top: 3px solid #dc2626;\">" +
               "<table style=\"width: 100%; border-collapse: collapse;\"><tr>" +
               "<td style=\"vertical-align: middle;\">" +
               "<div style=\"font-size: 9px; color: #94a3b8; text-transform: uppercase; font-weight: 800; letter-spacing: 0.5px;\">TOTAL FARE PAID (ALL TAXES INCLUDED)</div>" +
               "<div style=\"font-size: 20px; font-weight: 900; color: #ffffff; margin-top: 2px;\">&#8377;" + req.Price.ToString("0.00") + " <span style=\"font-size: 11px; color: #f87171;\">INR</span></div>" +
               "<div style=\"font-size: 10px; color: #94a3b8; margin-top: 2px;\">Instant Confirmation via Cashfree PG</div></td>" +
               "<td style=\"text-align: right; vertical-align: middle;\">" +
               "<div style=\"font-size: 10px; color: #94a3b8;\">Issued by</div>" +
               "<div style=\"background: #ffffff; border-radius: 6px; padding: 3px 8px; display: inline-block; margin-top: 2px;\">" +
               "<img src=\"https://www.picknbook.in/assets/picknbook-login-q5fv1iRs.png\" alt=\"Pick&amp;book\" style=\"height: 16px; display: block; border: 0;\" />" +
               "</div>" +
               "<div style=\"font-size: 9px; color: #94a3b8; margin-top: 4px;\">Ref: <b style=\"color: #ffffff; font-family: monospace;\">" + req.BookingReference + "</b></div></td>" +
               "</tr></table></div></div></body></html>";
    }

    public async Task SendBusTicketAsync(
        SendBusTicketEmailRequest request)
    {
        var pdfBytes =
            _ticketPdfService.GenerateBusTicketPdf(request);

        var attachment = new EmailAttachment
        {
            FileName =
                $"bus-ticket-{request.BookingReference}.pdf",

            ContentType = "application/pdf",

            Content = pdfBytes
        };

        var subject =
            $"Your Bus Ticket - {request.BookingReference}";

        // =========================================
        // PASSENGER HTML ROWS
        // =========================================
        var passengerHtmlRows = request.Passengers.Count > 0
            ? string.Join("", request.Passengers.Select(p => $@"
                <tr style=""border-bottom: 1px solid #e2e8f0; color: #1e293b;"">
                  <td style=""padding: 7px 4px; font-weight: 600;"">{p.FullName}</td>
                  <td style=""padding: 7px 4px; color: #64748b;"">{(string.IsNullOrWhiteSpace(p.Gender) ? "-" : p.Gender)}</td>
                  <td style=""padding: 7px 4px; font-weight: 700; text-align: right; color: #0284c7;"">{p.SeatNumber}</td>
                </tr>"))
            : $@"
                <tr style=""border-bottom: 1px solid #e2e8f0; color: #1e293b;"">
                  <td style=""padding: 7px 4px; font-weight: 600;"">{request.PassengerName}</td>
                  <td style=""padding: 7px 4px; color: #64748b;"">-</td>
                  <td style=""padding: 7px 4px; font-weight: 700; text-align: right; color: #0284c7;"">{request.SeatNumber}</td>
                </tr>";

        // =========================================
        // DISCOUNT & GST BREAKDOWN
        // =========================================
        var totalDiscount = request.AutoDiscountAmount + request.CouponDiscountAmount;
        if (totalDiscount == 0 && request.DiscountAmount.GetValueOrDefault() > 0)
        {
            totalDiscount = request.DiscountAmount.Value;
        }

        var discountRow = totalDiscount > 0
            ? $@"
            <tr>
              <td style=""padding: 4px 0; color: #16a34a;"">Offer / Coupon Discount:</td>
              <td style=""padding: 4px 0; text-align: right; color: #16a34a; font-weight: 600;"">- &#8377;{totalDiscount:0.00}</td>
            </tr>"
            : string.Empty;

        var gstRow = request.GstAmount > 0 
            ? $@"
            <tr>
              <td style=""padding: 4px 0; color: #64748b;"">Bus Partner GST:</td>
              <td style=""padding: 4px 0; text-align: right; color: #0f172a; font-weight: 600;"">&#8377;{request.GstAmount:0.00}</td>
            </tr>"
            : string.Empty;

        // =========================================
        // DYNAMIC CANCELLATION POLICY ROWS
        // =========================================
        var cancellationPolicyRows = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.CancellationPoliciesJson))
        {
            try
            {
                var jsonStr = request.CancellationPoliciesJson.Trim();
                if (jsonStr.StartsWith("\"") && jsonStr.EndsWith("\""))
                {
                    try
                    {
                        var unescaped = System.Text.Json.JsonSerializer.Deserialize<string>(jsonStr);
                        if (!string.IsNullOrWhiteSpace(unescaped))
                        {
                            jsonStr = unescaped.Trim();
                        }
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
                            {
                                timeText = mBetween.Groups[1].Value.Replace("hours", "hrs");
                            }
                            else if (mBefore.Success)
                            {
                                timeText = $"Before {mBefore.Groups[1].Value.Replace("hours", "hrs")}";
                            }
                            else if (p.TimeBeforeDept == "-1")
                            {
                                timeText = "Before departure";
                            }
                            else
                            {
                                timeText = $"{p.TimeBeforeDept} hrs before dept";
                            }
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
                        var refundAmt = isPercentage ? (request.Price * refundPct / 100m) : Math.Max(0, request.Price - chargeVal);
                        var refundAmtStr = $"&#8377;{refundAmt:0.00}";
                        var pctDisplay = isPercentage ? $"{refundPct}%" : (refundAmt > 0 ? $"&#8377;{refundAmt:0.00}" : "0%");

                        cancellationPolicyRows.Append($@"
                        <tr style=""border-bottom: 1px solid #e2e8f0;"">
                          <td style=""padding: 4px 2px; color: #334155;"">{timeText}</td>
                          <td style=""padding: 4px 2px; text-align: center; color: {(refundPct >= 50 ? "#16a34a" : "#dc2626")}; font-weight: 600;"">{pctDisplay}</td>
                          <td style=""padding: 4px 2px; text-align: right; color: #0f172a; font-weight: 600;"">{refundAmtStr}</td>
                        </tr>");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TicketEmailService] Failed to parse bus CancellationPoliciesJson: {ex.Message}");
            }
        }



        // =========================================
        // TIMINGS & LABELS
        // =========================================
        var boardingTime = request.BoardingPointTime != default ? request.BoardingPointTime : request.DepartureTime;
        var droppingTime = request.ArrivalPointTime != default ? request.ArrivalPointTime : request.ArrivalTime;
        var departureTime = request.DepartureTime != default ? request.DepartureTime : boardingTime;
        var boardingTimeStr = boardingTime != default ? boardingTime.ToString("ddd, dd MMM yyyy hh:mm tt") : "-";
        var droppingTimeStr = droppingTime != default ? droppingTime.ToString("ddd, dd MMM yyyy hh:mm tt") : "-";
        var departureTimeStr = departureTime != default ? departureTime.ToString("hh:mm tt") : "-";
        var reportingTimeStr = boardingTime != default ? boardingTime.AddMinutes(-15).ToString("hh:mm tt") : "-";
        var pListSummary = request.Passengers.Count > 0
            ? string.Join(", ", request.Passengers.Select(p => $"{p.FullName} (Seat {p.SeatNumber})"))
            : $"{request.PassengerName} (Seat {request.SeatNumber})";

        var effectiveBookedDate = request.BookedAtUtc != default ? request.BookedAtUtc : DateTime.UtcNow;
        var bookingDateStr = effectiveBookedDate.ToString("dd MMM yyyy, hh:mm tt");
        var grossFare = request.TotalFareBeforeDiscount > 0
            ? request.TotalFareBeforeDiscount
            : (request.Price + totalDiscount);

        // =========================================
        // EMAIL BODY
        // =========================================
        var body = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>Your Bus Ticket - {request.BookingReference}</title>
  <style>
    @media only screen and (max-width: 600px) {{
      .mobile-container {{
        width: 100% !important;
        max-width: 100% !important;
        border-radius: 0 !important;
      }}
      .stack-col {{
        display: block !important;
        width: 100% !important;
        box-sizing: border-box !important;
      }}
      .stack-col-border {{
        border-right: none !important;
        border-bottom: 1px solid #e2e8f0 !important;
      }}
      .route-arrow {{
        display: block !important;
        width: 100% !important;
        text-align: center !important;
        transform: rotate(90deg) !important;
        margin: 8px 0 !important;
      }}
      .text-mobile-left {{
        text-align: left !important;
        padding-top: 4px !important;
      }}
      .text-mobile-center {{
        text-align: center !important;
        margin-top: 10px !important;
        border-left: none !important;
        padding-left: 0 !important;
      }}
    }}
  </style>
</head>
<body style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f1f5f9; margin: 0; padding: 20px 10px; color: #1e293b;"">
  
  <div class=""mobile-container"" style=""max-width: 620px; margin: 0 auto; background-color: #ffffff; border: 1px solid #e2e8f0; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 20px rgba(0,0,0,0.06);"">
    
    <!-- 1. TICKET HEADER & META BAR -->
    <table style=""width: 100%; border-collapse: collapse; background-color: #ffffff; border-top: 4px solid #dc2626; border-bottom: 1px solid #e2e8f0;"">
      <tr>
        <td class=""stack-col"" style=""padding: 18px 20px; vertical-align: middle;"">
          <img src=""https://www.picknbook.in/assets/picknbook-login-q5fv1iRs.png"" alt=""Pick&amp;book"" height=""34"" style=""height: 34px; max-width: 140px; display: block; border: 0;"" />
          <div style=""font-weight: 800; font-size: 16px; letter-spacing: 0.5px; color: #dc2626; text-transform: uppercase; margin-top: 6px;"">Bus Ticket</div>
          <div style=""color: #64748b; font-size: 11px; margin-top: 3px;"">Booked on <b style=""color: #1e293b;"">{bookingDateStr}</b></div>
        </td>
        <td class=""stack-col text-mobile-left"" style=""padding: 18px 20px; text-align: right; vertical-align: middle; font-size: 12px;"">
          <div><span style=""color: #64748b;"">Booking ID:</span> <b style=""color: #dc2626; font-size: 13px;"">{request.BookingReference}</b></div>
          <div style=""margin-top: 4px;""><span style=""color: #64748b; font-size: 11px;"">Bus Partner PNR:</span> <b style=""color: #1e293b; font-size: 12px;"">{(!string.IsNullOrWhiteSpace(request.TicketNo) ? request.TicketNo : request.Pnr)}</b></div>
          <div style=""margin-top: 6px;""><span style=""display: inline-block; background-color: #dcfce7; color: #15803d; font-size: 10px; font-weight: 800; padding: 2px 8px; border-radius: 4px; text-transform: uppercase;"">CONFIRMED</span></div>
        </td>
      </tr>
    </table>

    <!-- 2. OPERATOR & SERVICE DETAILS -->
    <table style=""width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 1px solid #e2e8f0;"">
      <tr>
        <td class=""stack-col"" style=""padding: 16px 20px; vertical-align: top;"">
          <div style=""font-size: 18px; font-weight: 800; color: #0f172a;"">{request.OperatorName}</div>
          <div style=""font-size: 12px; color: #475569; margin-top: 3px; font-weight: 500;"">{request.BusType}</div>
          <div style=""font-size: 11px; color: #64748b; margin-top: 4px;"">Bus Start Time: <b style=""color: #0f172a;"">{departureTimeStr}</b></div>
        </td>
        <td class=""stack-col text-mobile-left"" style=""padding: 16px 20px; text-align: right; vertical-align: top; font-size: 12px;"">
          <div style=""font-weight: 700; color: #0f172a;"">Service # {request.Pnr}</div>
          <div style=""color: #64748b; font-size: 11px; margin-top: 3px;"">{request.OperatorName}</div>
        </td>
      </tr>
    </table>

    <!-- 3. JOURNEY ROUTE & TIMELINE -->
    <div style=""padding: 18px 20px; border-bottom: 1px solid #e2e8f0; background-color: #ffffff;"">
      <table style=""width: 100%; border-collapse: collapse;"">
        <tr>
          <td class=""stack-col"" style=""width: 44%; vertical-align: top;"">
            <div style=""font-size: 11px; text-transform: uppercase; color: #64748b; font-weight: 700; letter-spacing: 0.5px;"">DEPARTURE</div>
            <div style=""font-size: 17px; font-weight: 800; color: #0f172a; margin-top: 2px;"">{request.Origin}</div>
            <div style=""font-size: 12px; color: #334155; font-weight: 600; margin-top: 2px;"">{boardingTimeStr}</div>
            <div style=""margin-top: 8px; font-size: 11px; color: #64748b; line-height: 1.4;"">
              <b style=""color: #334155;"">Boarding Point:</b><br/>{request.BoardingPoint}
            </div>
          </td>
          <td class=""route-arrow"" style=""width: 12%; text-align: center; vertical-align: middle;"">
            <div style=""color: #dc2626; font-size: 22px; font-weight: bold; line-height: 1;"">&#10142;</div>
          </td>
          <td class=""stack-col"" style=""width: 44%; vertical-align: top;"">
            <div style=""font-size: 11px; text-transform: uppercase; color: #64748b; font-weight: 700; letter-spacing: 0.5px;"">ARRIVAL</div>
            <div style=""font-size: 17px; font-weight: 800; color: #0f172a; margin-top: 2px;"">{request.Destination}</div>
            <div style=""font-size: 12px; color: #334155; font-weight: 600; margin-top: 2px;"">{droppingTimeStr}</div>
            <div style=""margin-top: 8px; font-size: 11px; color: #64748b; line-height: 1.4;"">
              <b style=""color: #334155;"">Dropping Point:</b><br/>{request.ArrivalPoint}
            </div>
          </td>
        </tr>
      </table>
      <div style=""margin-top: 14px; padding: 10px 14px; background-color: #fffbeb; border: 1px solid #fef3c7; border-radius: 6px; font-size: 11px; color: #92400e; line-height: 1.4;"">
        * Reporting Time at the boarding point: <b style=""color: #78350f;"">{reportingTimeStr}</b> (15 minutes prior to departure)
      </div>
    </div>

    <!-- 4. PASSENGER & PAYMENT BOX -->
    <table style=""width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 1px solid #e2e8f0;"">
      <tr>
        <!-- PASSENGERS -->
        <td class=""stack-col stack-col-border"" style=""width: 52%; vertical-align: top; padding: 16px 14px 16px 20px; border-right: 1px solid #e2e8f0;"">
          <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 8px; text-transform: uppercase; letter-spacing: 0.5px;"">Passenger Details</div>
          <table style=""width: 100%; border-collapse: collapse; font-size: 11px;"">
            <thead>
              <tr style=""border-bottom: 1px solid #cbd5e1; color: #64748b; text-align: left;"">
                <th style=""padding: 6px 4px;"">Name</th>
                <th style=""padding: 6px 4px;"">Gender</th>
                <th style=""padding: 6px 4px; text-align: right;"">Seat NO</th>
              </tr>
            </thead>
            <tbody>
              {passengerHtmlRows}
            </tbody>
          </table>
        </td>
        <!-- PAYMENT -->
        <td class=""stack-col"" style=""width: 48%; vertical-align: top; padding: 16px 20px 16px 14px;"">
          <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 8px; text-transform: uppercase; letter-spacing: 0.5px;"">Payment Details</div>
          <table style=""width: 100%; border-collapse: collapse; font-size: 12px;"">
            <tr>
              <td style=""padding: 4px 0; color: #64748b;"">Total Fare:</td>
              <td style=""padding: 4px 0; text-align: right; color: #0f172a; font-weight: 600;"">&#8377;{grossFare:0.00}</td>
            </tr>
            {discountRow}
            <tr style=""border-top: 1px solid #cbd5e1;"">
              <td style=""padding: 8px 0 0 0; font-weight: 700; color: #0f172a; font-size: 13px;"">Amount Paid:</td>
              <td style=""padding: 8px 0 0 0; text-align: right; font-weight: 900; color: #dc2626; font-size: 16px;"">&#8377;{request.Price:0.00}</td>
            </tr>
          </table>
        </td>
      </tr>
    </table>

    <!-- 5. IMPORTANT INFORMATION -->
    <div style=""padding: 14px 20px; border-bottom: 1px solid #e2e8f0; background-color: #ffffff;"">
      <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 6px; text-transform: uppercase; letter-spacing: 0.5px;"">Important Information</div>
      <ul style=""margin: 0; padding-left: 18px; font-size: 11px; color: #475569; line-height: 1.6;"">
        <li>Please reach the boarding point by the reporting time (at least 15 mins prior to scheduled departure).</li>
        <li>Chat with us for live tracking &amp; trip updates at <a href=""https://picknbook.in/contact"" style=""color: #0284c7; text-decoration: none; font-weight: 600;"">https://picknbook.in/contact</a></li>
      </ul>
    </div>

    <!-- 6. TERMS & CONDITIONS AND CANCELLATION POLICY -->
    <table style=""width: 100%; border-collapse: collapse; background-color: #f8fafc;"">
      <tr>
        <td class=""stack-col stack-col-border"" style=""width: 50%; vertical-align: top; padding: 16px 14px 16px 20px; border-right: 1px solid #e2e8f0;"">
          <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 6px; text-transform: uppercase; letter-spacing: 0.5px;"">Terms and Conditions</div>
          <div style=""font-size: 10px; color: #64748b; line-height: 1.5;"">
            &bull; The arrival and departure times mentioned on the ticket are tentative.<br/>
            &bull; Passengers are requested to arrive at the boarding point at least 15 mins prior.<br/>
            &bull; Pick&amp;book is not responsible for any accidents or loss of passenger belongings.<br/>
            &bull; Pick&amp;book is not responsible for any delay or vehicle breakdown beyond control.<br/>
            &bull; Cancellation charges are applicable on Original fare but not on the Discounted Fare.<br/>
            &bull; For detailed terms, please visit <a href=""https://picknbook.in/contact"" style=""color: #0284c7; text-decoration: none; font-weight: 600;"">https://picknbook.in/contact</a>
          </div>
        </td>
        <td class=""stack-col"" style=""width: 50%; vertical-align: top; padding: 16px 20px 16px 14px;"">
          <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 6px; text-transform: uppercase; letter-spacing: 0.5px;"">Cancellation Policy</div>
          <table style=""width: 100%; border-collapse: collapse; font-size: 10px; color: #334155;"">
            <thead>
              <tr style=""border-bottom: 1px solid #cbd5e1; color: #64748b;"">
                <th style=""padding: 4px 2px; text-align: left;"">Cancellation Time</th>
                <th style=""padding: 4px 2px; text-align: center;"">Refund (%)</th>
                <th style=""padding: 4px 2px; text-align: right;"">Refund Amount</th>
              </tr>
            </thead>
            <tbody>
              {cancellationPolicyRows}
            </tbody>
          </table>
          <div style=""font-size: 9px; color: #64748b; margin-top: 6px; line-height: 1.4;"">
            &bull; *Refund amount is indicative
          </div>
        </td>
      </tr>
    </table>

  </div>
</body>
</html>";

        var emailAttachments = new List<EmailAttachment> { attachment };

        await _emailService.SendEmailWithAttachmentsAsync(
            request.ToEmail,
            subject,
            body,
            emailAttachments);
    }

    public async Task SendBusCancellationAsync(
        SendBusTicketEmailRequest request,
        decimal refundAmount)
    {
        var subject =
            $"Bus Ticket Cancelled - {request.BookingReference}";

        // =========================================
        // CANCELLED PASSENGER HTML ROWS
        // =========================================
        var passengerHtmlRows = request.Passengers?.Count > 0
            ? string.Join("", request.Passengers.Select(p => $@"
                <tr style=""border-bottom: 1px solid #e2e8f0; color: #1e293b;"">
                  <td style=""padding: 7px 4px; font-weight: 600;"">{p.FullName}</td>
                  <td style=""padding: 7px 4px; color: #64748b;"">{(string.IsNullOrWhiteSpace(p.Gender) ? "-" : p.Gender)}</td>
                  <td style=""padding: 7px 4px; text-align: right;"">
                    <span style=""color: #94a3b8; text-decoration: line-through; margin-right: 4px; font-weight: 600;"">{p.SeatNumber}</span>
                    <span style=""display: inline-block; background-color: #fee2e2; color: #dc2626; font-size: 9px; font-weight: 800; padding: 1px 5px; border-radius: 3px;"">CANCELLED</span>
                  </td>
                </tr>"))
            : $@"
                <tr style=""border-bottom: 1px solid #e2e8f0; color: #1e293b;"">
                  <td style=""padding: 7px 4px; font-weight: 600;"">{request.PassengerName}</td>
                  <td style=""padding: 7px 4px; color: #64748b;"">-</td>
                  <td style=""padding: 7px 4px; text-align: right;"">
                    <span style=""color: #94a3b8; text-decoration: line-through; margin-right: 4px; font-weight: 600;"">{request.SeatNumber}</span>
                    <span style=""display: inline-block; background-color: #fee2e2; color: #dc2626; font-size: 9px; font-weight: 800; padding: 1px 5px; border-radius: 3px;"">CANCELLED</span>
                  </td>
                </tr>";

        // =========================================
        // FARE, CHARGES & REFUND CALCULATION
        // =========================================
        var cancellationCharge = Math.Max(0m, request.Price - refundAmount);
        var totalDiscount = request.AutoDiscountAmount + request.CouponDiscountAmount;
        if (totalDiscount == 0 && request.DiscountAmount.GetValueOrDefault() > 0)
        {
            totalDiscount = request.DiscountAmount.Value;
        }

        var grossFare = request.TotalFareBeforeDiscount > 0
            ? request.TotalFareBeforeDiscount
            : (request.Price + totalDiscount);

        var discountRow = totalDiscount > 0
            ? $@"
            <tr>
              <td style=""padding: 3px 0; color: #16a34a;"">Offer / Coupon Discount:</td>
              <td style=""padding: 3px 0; text-align: right; color: #16a34a; font-weight: 600;"">- &#8377;{totalDiscount:0.00}</td>
            </tr>"
            : string.Empty;

        // =========================================
        // TIMINGS
        // =========================================
        var boardingTime = request.BoardingPointTime != default ? request.BoardingPointTime : request.DepartureTime;
        var droppingTime = request.ArrivalPointTime != default ? request.ArrivalPointTime : request.ArrivalTime;
        var departureTime = request.DepartureTime != default ? request.DepartureTime : boardingTime;
        var boardingTimeStr = boardingTime != default ? boardingTime.ToString("ddd, dd MMM yyyy hh:mm tt") : "-";
        var droppingTimeStr = droppingTime != default ? droppingTime.ToString("ddd, dd MMM yyyy hh:mm tt") : "-";
        var departureTimeStr = departureTime != default ? departureTime.ToString("hh:mm tt") : "-";
        var effectiveCancelDate = request.CancelledAtUtc.HasValue 
            ? request.CancelledAtUtc.Value 
            : (request.BookedAtUtc != default ? request.BookedAtUtc : DateTime.UtcNow);
        var cancellationDateStr = effectiveCancelDate.ToString("dd MMM yyyy, hh:mm tt");

        // =========================================
        // DYNAMIC CANCELLATION POLICY ROWS
        // =========================================
        var cancellationPolicyRows = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.CancellationPoliciesJson))
        {
            try
            {
                var jsonStr = request.CancellationPoliciesJson.Trim();
                if (jsonStr.StartsWith("\"") && jsonStr.EndsWith("\""))
                {
                    try
                    {
                        var unescaped = System.Text.Json.JsonSerializer.Deserialize<string>(jsonStr);
                        if (!string.IsNullOrWhiteSpace(unescaped))
                        {
                            jsonStr = unescaped.Trim();
                        }
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
                            {
                                timeText = mBetween.Groups[1].Value.Replace("hours", "hrs");
                            }
                            else if (mBefore.Success)
                            {
                                timeText = $"Before {mBefore.Groups[1].Value.Replace("hours", "hrs")}";
                            }
                            else if (p.TimeBeforeDept == "-1")
                            {
                                timeText = "Before departure";
                            }
                            else
                            {
                                timeText = $"{p.TimeBeforeDept} hrs before dept";
                            }
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
                        var refundAmtVal = isPercentage ? (request.Price * refundPct / 100m) : Math.Max(0, request.Price - chargeVal);
                        var refundAmtStr = $"&#8377;{refundAmtVal:0.00}";
                        var pctDisplay = isPercentage ? $"{refundPct}%" : (refundAmtVal > 0 ? $"&#8377;{refundAmtVal:0.00}" : "0%");

                        cancellationPolicyRows.Append($@"
                        <tr style=""border-bottom: 1px solid #e2e8f0;"">
                          <td style=""padding: 4px 2px; color: #334155;"">{timeText}</td>
                          <td style=""padding: 4px 2px; text-align: center; color: {(refundPct >= 50 ? "#16a34a" : "#dc2626")}; font-weight: 600;"">{pctDisplay}</td>
                          <td style=""padding: 4px 2px; text-align: right; color: #0f172a; font-weight: 600;"">{refundAmtStr}</td>
                        </tr>");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TicketEmailService] Failed to parse bus CancellationPoliciesJson: {ex.Message}");
            }
        }



        // =========================================
        // EMAIL BODY
        // =========================================
        var body = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>Bus Ticket Cancelled - {request.BookingReference}</title>
  <style>
    @media only screen and (max-width: 600px) {{
      .mobile-container {{
        width: 100% !important;
        max-width: 100% !important;
        border-radius: 0 !important;
      }}
      .stack-col {{
        display: block !important;
        width: 100% !important;
        box-sizing: border-box !important;
      }}
      .stack-col-border {{
        border-right: none !important;
        border-bottom: 1px solid #e2e8f0 !important;
      }}
      .route-arrow {{
        display: block !important;
        width: 100% !important;
        text-align: center !important;
        transform: rotate(90deg) !important;
        margin: 8px 0 !important;
      }}
      .text-mobile-left {{
        text-align: left !important;
        padding-top: 4px !important;
      }}
      .text-mobile-center {{
        text-align: center !important;
        margin-top: 10px !important;
        border-left: none !important;
        padding-left: 0 !important;
      }}
    }}
  </style>
</head>
<body style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f1f5f9; margin: 0; padding: 20px 10px; color: #1e293b;"">
  
  <div class=""mobile-container"" style=""max-width: 620px; margin: 0 auto; background-color: #ffffff; border: 1px solid #e2e8f0; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 20px rgba(0,0,0,0.06);"">
    
    <!-- 1. TICKET HEADER & META BAR -->
    <table style=""width: 100%; border-collapse: collapse; background-color: #ffffff; border-top: 4px solid #dc2626; border-bottom: 1px solid #e2e8f0;"">
      <tr>
        <td class=""stack-col"" style=""padding: 18px 20px; vertical-align: middle;"">
          <img src=""https://www.picknbook.in/assets/picknbook-login-q5fv1iRs.png"" alt=""Pick&amp;book"" height=""34"" style=""height: 34px; max-width: 140px; display: block; border: 0;"" />
          <div style=""font-weight: 800; font-size: 16px; letter-spacing: 0.5px; color: #dc2626; text-transform: uppercase; margin-top: 6px;"">Bus Ticket Cancellation</div>
          <div style=""color: #64748b; font-size: 11px; margin-top: 3px;"">Cancelled on <b style=""color: #1e293b;"">{cancellationDateStr}</b></div>
        </td>
        <td class=""stack-col text-mobile-left"" style=""padding: 18px 20px; text-align: right; vertical-align: middle; font-size: 12px;"">
          <div><span style=""color: #64748b;"">Booking ID:</span> <b style=""color: #dc2626; font-size: 13px;"">{request.BookingReference}</b></div>
          <div style=""margin-top: 4px;""><span style=""color: #64748b; font-size: 11px;"">Bus Partner PNR:</span> <b style=""color: #1e293b; font-size: 12px;"">{(!string.IsNullOrWhiteSpace(request.TicketNo) ? request.TicketNo : request.Pnr)}</b></div>
          <div style=""margin-top: 6px;""><span style=""display: inline-block; background-color: #fee2e2; color: #dc2626; font-size: 10px; font-weight: 800; padding: 2px 8px; border-radius: 4px; text-transform: uppercase;"">CANCELLED</span></div>
        </td>
      </tr>
    </table>

    <!-- 2. OPERATOR & SERVICE DETAILS -->
    <table style=""width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 1px solid #e2e8f0;"">
      <tr>
        <td class=""stack-col"" style=""padding: 16px 20px; vertical-align: top;"">
          <div style=""font-size: 18px; font-weight: 800; color: #0f172a;"">{request.OperatorName}</div>
          <div style=""font-size: 12px; color: #475569; margin-top: 3px; font-weight: 500;"">{request.BusType}</div>
          <div style=""font-size: 11px; color: #64748b; margin-top: 4px;"">Bus Start Time: <b style=""color: #0f172a;"">{departureTimeStr}</b></div>
        </td>
        <td class=""stack-col text-mobile-left"" style=""padding: 16px 20px; text-align: right; vertical-align: top; font-size: 12px;"">
          <div style=""font-weight: 700; color: #0f172a;"">Service # {request.Pnr}</div>
          <div style=""color: #64748b; font-size: 11px; margin-top: 3px;"">{request.OperatorName}</div>
        </td>
      </tr>
    </table>

    <!-- 3. JOURNEY ROUTE & TIMELINE -->
    <div style=""padding: 18px 20px; border-bottom: 1px solid #e2e8f0; background-color: #ffffff;"">
      <table style=""width: 100%; border-collapse: collapse;"">
        <tr>
          <td class=""stack-col"" style=""width: 44%; vertical-align: top;"">
            <div style=""font-size: 11px; text-transform: uppercase; color: #64748b; font-weight: 700; letter-spacing: 0.5px;"">DEPARTURE</div>
            <div style=""font-size: 17px; font-weight: 800; color: #0f172a; margin-top: 2px;"">{request.Origin}</div>
            <div style=""font-size: 12px; color: #334155; font-weight: 600; margin-top: 2px;"">{boardingTimeStr}</div>
            <div style=""margin-top: 8px; font-size: 11px; color: #64748b; line-height: 1.4;"">
              <b style=""color: #334155;"">Boarding Point:</b><br/>{request.BoardingPoint ?? "-"}
            </div>
          </td>
          <td class=""route-arrow"" style=""width: 12%; text-align: center; vertical-align: middle;"">
            <div style=""color: #94a3b8; font-size: 22px; font-weight: bold; line-height: 1;"">&#10142;</div>
          </td>
          <td class=""stack-col"" style=""width: 44%; vertical-align: top;"">
            <div style=""font-size: 11px; text-transform: uppercase; color: #64748b; font-weight: 700; letter-spacing: 0.5px;"">ARRIVAL</div>
            <div style=""font-size: 17px; font-weight: 800; color: #0f172a; margin-top: 2px;"">{request.Destination}</div>
            <div style=""font-size: 12px; color: #334155; font-weight: 600; margin-top: 2px;"">{droppingTimeStr}</div>
            <div style=""margin-top: 8px; font-size: 11px; color: #64748b; line-height: 1.4;"">
              <b style=""color: #334155;"">Dropping Point:</b><br/>{request.ArrivalPoint ?? "-"}
            </div>
          </td>
        </tr>
      </table>
      <div style=""margin-top: 14px; padding: 10px 14px; background-color: #fef2f2; border: 1px solid #fee2e2; border-radius: 6px; font-size: 11px; color: #991b1b; line-height: 1.4;"">
        &bull; <b>Cancellation Confirmation:</b> This bus booking has been successfully cancelled. The reserved seats have been released and this ticket is no longer valid for travel.
      </div>
    </div>

    <!-- 4. PASSENGER & REFUND BREAKDOWN BOX -->
    <table style=""width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 1px solid #e2e8f0;"">
      <tr>
        <!-- CANCELLED PASSENGERS -->
        <td class=""stack-col stack-col-border"" style=""width: 52%; vertical-align: top; padding: 16px 14px 16px 20px; border-right: 1px solid #e2e8f0;"">
          <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 8px; text-transform: uppercase; letter-spacing: 0.5px;"">Cancelled Passenger(s)</div>
          <table style=""width: 100%; border-collapse: collapse; font-size: 11px;"">
            <thead>
              <tr style=""border-bottom: 1px solid #cbd5e1; color: #64748b; text-align: left;"">
                <th style=""padding: 6px 4px;"">Name</th>
                <th style=""padding: 6px 4px;"">Gender</th>
                <th style=""padding: 6px 4px; text-align: right;"">Seat NO</th>
              </tr>
            </thead>
            <tbody>
              {passengerHtmlRows}
            </tbody>
          </table>
        </td>
        
        <!-- REFUND & FARE BREAKDOWN -->
        <td class=""stack-col"" style=""width: 48%; vertical-align: top; padding: 16px 20px 16px 14px;"">
          <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 8px; text-transform: uppercase; letter-spacing: 0.5px;"">Refund &amp; Fare Breakdown</div>
          <table style=""width: 100%; border-collapse: collapse; font-size: 12px;"">
            <tr>
              <td style=""padding: 3px 0; color: #64748b;"">Total Fare:</td>
              <td style=""padding: 3px 0; text-align: right; color: #0f172a; font-weight: 600;"">&#8377;{grossFare:0.00}</td>
            </tr>
            {discountRow}
            <tr>
              <td style=""padding: 3px 0; color: #64748b;"">Original Paid:</td>
              <td style=""padding: 3px 0; text-align: right; color: #0f172a; font-weight: 600;"">&#8377;{request.Price:0.00}</td>
            </tr>
            <tr>
              <td style=""padding: 3px 0; color: #dc2626;"">Cancellation Charges:</td>
              <td style=""padding: 3px 0; text-align: right; color: #dc2626; font-weight: 600;"">- &#8377;{cancellationCharge:0.00}</td>
            </tr>
            <tr style=""border-top: 1px solid #cbd5e1; background-color: #f0fdf4;"">
              <td style=""padding: 6px 4px; font-weight: 700; color: #166534; font-size: 13px;"">Net Refund Amount:</td>
              <td style=""padding: 6px 4px; text-align: right; font-weight: 900; color: #16a34a; font-size: 16px;"">&#8377;{refundAmount:0.00}</td>
            </tr>
            <tr style=""border-top: 1px solid #e2e8f0;"">
              <td style=""padding: 6px 0 2px 0; color: #64748b; font-size: 11px;"">Refund Mode:</td>
              <td style=""padding: 6px 0 2px 0; text-align: right; color: #0f172a; font-size: 11px; font-weight: 600;"">Original Payment Method</td>
            </tr>
            <tr>
              <td style=""padding: 2px 0 0 0; color: #64748b; font-size: 11px;"">Crediting Timeline:</td>
              <td style=""padding: 2px 0 0 0; text-align: right; color: #15803d; font-size: 11px; font-weight: 700;"">3 &ndash; 5 Working Days</td>
            </tr>
          </table>
        </td>
      </tr>
    </table>

    <!-- 5. IMPORTANT CANCELLATION INFORMATION -->
    <div style=""padding: 14px 20px; border-bottom: 1px solid #e2e8f0; background-color: #ffffff;"">
      <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 6px; text-transform: uppercase; letter-spacing: 0.5px;"">Important Cancellation Information</div>
      <ul style=""margin: 0; padding-left: 18px; font-size: 11px; color: #475569; line-height: 1.6;"">
        <li><b>Refund Crediting:</b> Your net refund of <b>&#8377;{refundAmount:0.00}</b> has been initiated to your original payment mode (Bank Account / UPI / Card / Wallet). Please allow <b>3&ndash;5 working days</b> for your bank to complete the settlement.</li>
        <li><b>Void E-Ticket Attachment:</b> A copy of your cancelled e-ticket marked with <b>VOID</b> status is attached for your accounting and reimbursement records.</li>
        <li><b>Need Help?</b> If you did not initiate this cancellation or have queries regarding the refund deduction, reach out to our 24x7 support desk at <a href=""https://picknbook.in/contact"" style=""color: #0284c7; text-decoration: none; font-weight: 600;"">https://picknbook.in/contact</a>.</li>
      </ul>
    </div>

    <!-- 6. TERMS & CONDITIONS AND CANCELLATION POLICY -->
    <table style=""width: 100%; border-collapse: collapse; background-color: #f8fafc;"">
      <tr>
        <td class=""stack-col stack-col-border"" style=""width: 50%; vertical-align: top; padding: 16px 14px 16px 20px; border-right: 1px solid #e2e8f0;"">
          <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 6px; text-transform: uppercase; letter-spacing: 0.5px;"">Cancellation &amp; Refund Terms</div>
          <div style=""font-size: 10px; color: #64748b; line-height: 1.5;"">
            &bull; Cancellation charges are levied in accordance with the operator's policy at the time of request.<br/>
            &bull; Convenience fees, gateway charges, and promotional voucher discounts are strictly non-refundable.<br/>
            &bull; Once cancelled, seats are released into inventory and tickets cannot be reinstated.<br/>
            &bull; Actual time taken for refund reflection depends upon your card issuer or acquiring bank.<br/>
            &bull; For comprehensive terms and conditions, visit <a href=""https://picknbook.in/contact"" style=""color: #0284c7; text-decoration: none; font-weight: 600;"">https://picknbook.in/contact</a>
          </div>
        </td>
        <td class=""stack-col"" style=""width: 50%; vertical-align: top; padding: 16px 20px 16px 14px;"">
          <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 6px; text-transform: uppercase; letter-spacing: 0.5px;"">Applied Cancellation Policy</div>
          <table style=""width: 100%; border-collapse: collapse; font-size: 10px; color: #334155;"">
            <thead>
              <tr style=""border-bottom: 1px solid #cbd5e1; color: #64748b;"">
                <th style=""padding: 4px 2px; text-align: left;"">Cancellation Time</th>
                <th style=""padding: 4px 2px; text-align: center;"">Refund (%)</th>
                <th style=""padding: 4px 2px; text-align: right;"">Refund Amount</th>
              </tr>
            </thead>
            <tbody>
              {cancellationPolicyRows}
            </tbody>
          </table>
          <div style=""font-size: 9px; color: #64748b; margin-top: 6px; line-height: 1.4;"">
            &bull; Cancellation charges are calculated on base fare per operator policy.
          </div>
        </td>
      </tr>
    </table>

  </div>
</body>
</html>";

        // =========================================
        // PDF (Voided / Cancelled)
        // =========================================

        var pdfBytes =
            _ticketPdfService.GenerateBusTicketPdf(request, isCancelled: true, refundAmount: refundAmount);

        var attachment = new EmailAttachment
        {
            FileName =
                $"bus-cancelled-{request.BookingReference}.pdf",

            ContentType = "application/pdf",

            Content = pdfBytes
        };

        var cancelAttachments = new List<EmailAttachment> { attachment };

        await _emailService.SendEmailWithAttachmentsAsync(
            request.ToEmail,
            subject,
            body,
            cancelAttachments);
    }

    public async Task SendFlightCancellationAsync(
        SendFlightTicketEmailRequest request,
        decimal refundAmount)
    {
        var subject = $"Flight Ticket Cancelled - {request.BookingReference}";

        // Generate CANCELLED PDF voucher and attach
        var pdfs = _ticketPdfService.GenerateFlightTicketPdf(request, isCancelled: true, refundAmount: refundAmount);
        var attachments = pdfs.Select(p => new EmailAttachment
        {
            FileName = $"Cancellation-Advice-{request.BookingReference}.pdf",
            ContentType = "application/pdf",
            Content = p.Content
        }).ToList();

        var depIst = ToIst(request.DepartureTime);
        var depDateStr = depIst.ToString("ddd, dd MMMM yyyy");
        var originCity = GetCityName(request.Origin);
        var destCity = GetCityName(request.Destination);

        string firstFlightNo = request.Segments?.FirstOrDefault()?.FlightNumber ?? "IC-3301";
        string airCode = GetAirlineCode(request.Airline, firstFlightNo);
        string logoUrl = "https://images.kiwi.com/airlines/64/" + airCode + ".png";
        string fallbackLogoUrl = "https://imgak.mmtcdn.com/flights/assets/media/dt/common/icons/" + airCode + ".png";
        string pnrVal = !string.IsNullOrWhiteSpace(request.Pnr) ? request.Pnr : (request.Segments?.FirstOrDefault()?.Pnr ?? "BLKT23");
        decimal cancellationCharges = Math.Max(0, request.Price - refundAmount);

        var body =
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">" +
            "<meta name=\"x-apple-disable-message-reformatting\">" +
            "<title>Flight Booking Cancelled</title>" +
            "<style>" +
            "@media only screen and (max-width: 520px) {" +
            "  .email-container { width: 100% !important; margin: 0 !important; border-radius: 0 !important; }" +
            "  .header-padding { padding: 18px 14px !important; }" +
            "  .content-padding { padding: 16px 12px !important; }" +
            "}" +
            "</style>" +
            "</head>" +
            "<body style=\"font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f1f5f9; margin: 0; padding: 0; color: #0f172a;\">" +
            "<div class=\"email-container\" style=\"max-width: 600px; margin: 15px auto; background-color: #ffffff; border-radius: 16px; overflow: hidden; box-shadow: 0 4px 16px rgba(0,0,0,0.06); border: 1px solid #fee2e2;\">" +

            "<!-- Header Banner (Mobile-Optimized Crimson) -->" +
            "<div class=\"header-padding\" style=\"background: linear-gradient(135deg, #991b1b 0%, #7f1d1d 100%); color: #ffffff; padding: 20px; border-top: 4px solid #dc2626;\">" +
            "<table style=\"width: 100%; border-collapse: collapse;\"><tr>" +
            "<td style=\"vertical-align: middle;\">" +
            "<div style=\"background: #ffffff; border-radius: 10px; padding: 5px 10px; display: inline-block; margin-bottom: 10px;\">" +
            "<img src=\"https://www.picknbook.in/assets/picknbook-login-q5fv1iRs.png\" alt=\"Pick&amp;book\" style=\"height: 22px; width: auto; display: block; border: 0;\" />" +
            "</div>" +
            "<div style=\"font-size: 10px; font-weight: 800; text-transform: uppercase; letter-spacing: 0.8px; color: #fecaca;\">BOOKING CANCELLED &bull; REFUND INITIATED</div>" +
            "<div style=\"font-size: 17px; font-weight: 900; margin-top: 3px; color: #ffffff; line-height: 1.3;\">Flight Booking Cancelled &mdash; " + request.BookingReference + "</div>" +
            "<div style=\"font-size: 11px; color: #fee2e2; margin-top: 4px;\">Your booking for " + originCity + " - " + destCity + " has been successfully cancelled.</div>" +
            "</td></tr></table></div>" +

            "<div class=\"content-padding\" style=\"padding: 18px 16px;\">" +

            "<!-- Flight Details Summary Card -->" +
            "<div style=\"background-color: #fef2f2; border: 1px solid #fecaca; border-radius: 14px; padding: 14px; margin-bottom: 16px;\">" +
            "<table style=\"width: 100%; border-collapse: collapse;\"><tr>" +
            "<td style=\"width: 44px; vertical-align: middle;\">" +
            "<div style=\"width: 40px; height: 40px; border-radius: 10px; background: #ffffff; border: 1px solid #fecaca; text-align: center; line-height: 40px; overflow: hidden;\">" +
            "<img src=\"" + logoUrl + "\" alt=\"" + request.Airline + "\" style=\"width: 32px; height: 32px; vertical-align: middle; object-fit: contain;\" onerror=\"this.onerror=null; this.src='" + fallbackLogoUrl + "';\" />" +
            "</div></td>" +
            "<td style=\"padding-left: 10px; vertical-align: middle;\">" +
            "<div style=\"font-size: 13px; font-weight: 900; color: #991b1b; text-transform: uppercase;\">" + request.Airline + " (" + firstFlightNo + ")</div>" +
            "<div style=\"font-size: 11px; color: #7f1d1d; font-weight: 600;\">" + originCity + " &rarr; " + destCity + " &bull; " + depDateStr + "</div>" +
            "</td>" +
            "<td style=\"text-align: right; vertical-align: middle;\">" +
            "<div style=\"display: inline-block; font-size: 10px; border: 1px solid #fecaca; border-radius: 6px; overflow: hidden;\">" +
            "<span style=\"background: #991b1b; color: #ffffff; font-weight: 800; padding: 2px 6px;\">PNR</span>" +
            "<span style=\"background: #ffffff; color: #991b1b; font-weight: 900; padding: 2px 8px; font-family: monospace;\">" + pnrVal + "</span>" +
            "</div></td></tr></table></div>" +

            "<!-- Refund Breakdown Card -->" +
            "<div style=\"margin-bottom: 18px;\">" +
            "<div style=\"font-size: 12px; font-weight: 800; text-transform: uppercase; color: #091a3e; margin-bottom: 8px; letter-spacing: 0.5px;\">Refund Breakdown</div>" +
            "<div style=\"background-color: #ffffff; border: 1px solid #e2e8f0; border-radius: 12px; overflow: hidden;\">" +
            "<table style=\"width: 100%; border-collapse: collapse; font-size: 12px;\">" +
            "<tr style=\"border-bottom: 1px solid #f1f5f9;\">" +
            "<td style=\"padding: 10px 14px; color: #64748b; font-weight: 600;\">Original Total Paid</td>" +
            "<td style=\"padding: 10px 14px; color: #0f172a; font-weight: 800; text-align: right;\">&#8377;" + request.Price.ToString("0.00") + "</td></tr>" +
            "<tr style=\"border-bottom: 1px solid #f1f5f9; background-color: #fafbfc;\">" +
            "<td style=\"padding: 10px 14px; color: #dc2626; font-weight: 600;\">Total Cancellation &amp; Airline Charges</td>" +
            "<td style=\"padding: 10px 14px; color: #dc2626; font-weight: 800; text-align: right;\">- &#8377;" + cancellationCharges.ToString("0.00") + "</td></tr>" +
            "<tr style=\"background-color: #f0fdf4;\">" +
            "<td style=\"padding: 12px 14px; color: #059669; font-weight: 900; font-size: 13px;\">Net Refund Processed</td>" +
            "<td style=\"padding: 12px 14px; color: #059669; font-weight: 900; font-size: 16px; text-align: right;\">&#8377;" + refundAmount.ToString("0.00") + "</td></tr>" +
            "</table></div></div>" +

            "<!-- Processing Timeline Alert -->" +
            "<div style=\"background-color: #eff6ff; border: 1px solid #bfdbfe; border-radius: 12px; padding: 12px 14px; margin-bottom: 18px; font-size: 12px; color: #1e40af; line-height: 1.5;\">" +
            "<b>Refund Timeline:</b> The net refund of <b>&#8377;" + refundAmount.ToString("0.00") + "</b> has been initiated and will reflect in your original payment method / wallet within <b>3&ndash;5 business days</b>." +
            "</div>" +

            "<!-- Official PDF Advice Attached Note -->" +
            "<div style=\"background-color: #ffffff; border: 1px solid #e2e8f0; border-radius: 12px; padding: 14px; margin-bottom: 18px; font-size: 11px; color: #64748b; line-height: 1.5;\">" +
            "<div style=\"font-weight: 800; color: #091a3e; margin-bottom: 4px;\">Official Cancellation Advice PDF Attached</div>" +
            "<div>A detailed cancellation statement PDF with transaction reference and breakdown has been attached to this email. For any queries, reach us at support@picknbook.in.</div>" +
            "</div>" +

            "</div>" +

            "<!-- Footer -->" +
            "<div class=\"content-padding\" style=\"background-color: #091a3e; color: #ffffff; padding: 16px 20px; border-top: 3px solid #dc2626;\">" +
            "<table style=\"width: 100%; border-collapse: collapse;\"><tr>" +
            "<td style=\"vertical-align: middle;\">" +
            "<div style=\"font-size: 11px; color: #94a3b8;\">&copy; 2026 Pick&amp;book Travel Services. All rights reserved.</div></td>" +
            "<td style=\"text-align: right; vertical-align: middle;\">" +
            "<div style=\"font-size: 10px; color: #94a3b8;\">Booking ID: <b style=\"color: #ffffff; font-family: monospace;\">" + request.BookingReference + "</b></div></td>" +
            "</tr></table></div></div></body></html>";

        await _emailService.SendEmailWithAttachmentsAsync(
            request.ToEmail,
            subject,
            body,
            attachments);
    }

    private static DateTime ToIst(DateTime dt)
    {
        return IndiaTimeHelper.ToIst(dt);
    }

    private static string GetCityName(string airportCode)
    {
        if (string.IsNullOrWhiteSpace(airportCode)) return string.Empty;
        return airportCode.Trim().ToUpper() switch
        {
            "DEL" => "DELHI",
            "BOM" => "MUMBAI",
            "BLR" => "BENGALURU",
            "MAA" => "CHENNAI",
            "CCU" => "KOLKATA",
            "HYD" => "HYDERABAD",
            "VGA" => "VIJAYAWADA",
            "AMD" => "AHMEDABAD",
            "PNQ" => "PUNE",
            "COK" => "KOCHI",
            "GOI" => "GOA",
            "GOX" => "GOA (MOPA)",
            "JAI" => "JAIPUR",
            "IXC" => "CHANDIGARH",
            "TRV" => "THIRUVANANTHAPURAM",
            "LON" => "LONDON",
            "PAR" => "PARIS",
            "NYC" => "NEW YORK",
            "MAD" => "MADRID",
            "BCN" => "BARCELONA",
            "BER" => "BERLIN",
            "ROM" => "ROME",
            "SFO" => "SAN FRANCISCO",
            "MUC" => "MUNICH",
            "NCE" => "NICE",
            "DXB" => "DUBAI",
            "SIN" => "SINGAPORE",
            _ => airportCode.Trim().ToUpper()
        };
    }

    private static string GetAirportName(string airportCode)
    {
        if (string.IsNullOrWhiteSpace(airportCode)) return string.Empty;
        return airportCode.Trim().ToUpper() switch
        {
            "DEL" => "Indira Gandhi Intl Airport, T3",
            "BOM" => "Chhatrapati Shivaji Maharaj Intl Airport, T2",
            "BLR" => "Kempegowda Intl Airport, T2",
            "MAA" => "Chennai Intl Airport, T1",
            "CCU" => "Netaji Subhash Chandra Bose Intl Airport",
            "HYD" => "Rajiv Gandhi Intl Airport, T1",
            "VGA" => "Vijayawada Intl Airport",
            "AMD" => "Sardar Vallabhbhai Patel Intl Airport",
            "PNQ" => "Pune Intl Airport",
            "COK" => "Cochin Intl Airport",
            "GOI" => "Dabolim Intl Airport",
            "GOX" => "Manohar Intl Airport (MOPA)",
            "JAI" => "Jaipur Intl Airport",
            "IXC" => "Shaheed Bhagat Singh Intl Airport",
            "TRV" => "Thiruvananthapuram Intl Airport",
            "LON" => "Heathrow Airport, Terminal 2",
            "PAR" => "Charles de Gaulle Airport, Terminal 2E",
            "NYC" => "John F. Kennedy Intl Airport, Terminal 4",
            "DXB" => "Dubai Intl Airport, Terminal 3",
            "SIN" => "Singapore Changi Airport, Terminal 3",
            _ => airportCode.Trim().ToUpper() + " Airport"
        };
    }

    public async Task SendHotelTicketAsync(HotelReservation reservation)
    {
        var pdfBytes = _ticketPdfService.GenerateHotelTicketPdf(reservation);

        string displayBookingId = !string.IsNullOrWhiteSpace(reservation.ProviderBookingId)
            ? reservation.ProviderBookingId
            : (!string.IsNullOrWhiteSpace(reservation.SrdvBookingId) ? reservation.SrdvBookingId : reservation.BookingReference);

        var attachment = new EmailAttachment
        {
            FileName = $"hotel-ticket-{displayBookingId}.pdf",
            ContentType = "application/pdf",
            Content = pdfBytes
        };

        var subject = $"Your Hotel Confirmation Voucher - {displayBookingId} - {reservation.HotelName}";

        int nights = Math.Max(1, (reservation.CheckOutDate.Date - reservation.CheckInDate.Date).Days);
        string roomTypeName = !string.IsNullOrWhiteSpace(reservation.RoomTypeName)
            ? reservation.RoomTypeName.Trim()
            : GetRoomCategory(reservation.OfferId);

        string confNo = !string.IsNullOrWhiteSpace(reservation.ConfirmationNo)
            ? reservation.ConfirmationNo
            : (!string.IsNullOrWhiteSpace(reservation.BookingReference) ? reservation.BookingReference : displayBookingId);

        string bookingDateStr = IndiaTimeHelper.FormatIst(reservation.CreatedAt, "dd MMM yyyy, hh:mm tt");
        string checkInDateStr = IndiaTimeHelper.FormatIst(reservation.CheckInDate, "ddd, dd MMM yyyy");
        string checkOutDateStr = IndiaTimeHelper.FormatIst(reservation.CheckOutDate, "ddd, dd MMM yyyy");
        string cityDisplay = !string.IsNullOrWhiteSpace(reservation.CityCode) ? reservation.CityCode : "Hotel Stay";

        decimal basePrice = reservation.BasePrice > 0 ? reservation.BasePrice : reservation.Price;
        decimal totalPrice = reservation.TotalPrice > 0 ? reservation.TotalPrice : reservation.Price;
        string nationalityText = reservation.GuestNationality == "IN" ? "Indian (IN)" : (reservation.GuestNationality ?? "Indian (IN)");

        // Dynamic Cancellation Policies (Zero fallbacks)
        var parsedPolicies = ParseHotelCancellationPolicies(reservation);
        var policyRowsHtml = new System.Text.StringBuilder();
        if (parsedPolicies.Count > 0)
        {
            foreach (var cp in parsedPolicies)
            {
                policyRowsHtml.Append($@"
                <tr style=""border-bottom: 1px solid #e2e8f0;"">
                  <td style=""padding: 5px 4px; color: #334155;"">{cp.TimeWindow}</td>
                  <td style=""padding: 5px 4px; text-align: center; color: #16a34a; font-weight: 600;"">{cp.RefundPct}</td>
                  <td style=""padding: 5px 4px; text-align: right; color: #0f172a; font-weight: 600;"">{cp.RefundAmt}</td>
                </tr>");
            }
        }
        else
        {
            policyRowsHtml.Append(@"
                <tr>
                  <td colspan=""3"" style=""padding: 6px 4px; color: #64748b; font-style: italic;"">Cancellation terms subject to hotel property rules.</td>
                </tr>");
        }

        // Taxes Row
        var taxesRowHtml = reservation.SrdvGstAmount > 0
            ? $@"
            <tr>
              <td style=""padding: 3px 0; color: #64748b;"">Hotel Taxes &amp; Service GST:</td>
              <td style=""padding: 3px 0; text-align: right; color: #0f172a; font-weight: 600;"">INR {reservation.SrdvGstAmount:0.00}</td>
            </tr>"
            : string.Empty;

        // Discount Row
        var discountRowHtml = reservation.CouponDiscount > 0
            ? $@"
            <tr>
              <td style=""padding: 3px 0; color: #16a34a;"">{(!string.IsNullOrWhiteSpace(reservation.CouponCode) ? $"Discount ({reservation.CouponCode}):" : "Offer / Coupon Discount:")}</td>
              <td style=""padding: 3px 0; text-align: right; color: #16a34a; font-weight: 600;"">- INR {reservation.CouponDiscount:0.00}</td>
            </tr>"
            : string.Empty;

        var body = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>Your Hotel Voucher - {reservation.BookingReference}</title>
  <style>
    @media only screen and (max-width: 600px) {{
      .mobile-container {{
        width: 100% !important;
        max-width: 100% !important;
        border-radius: 0 !important;
      }}
      .stack-col {{
        display: block !important;
        width: 100% !important;
        box-sizing: border-box !important;
      }}
      .stack-col-border {{
        border-right: none !important;
        border-bottom: 1px solid #e2e8f0 !important;
      }}
      .text-mobile-left {{
        text-align: left !important;
        padding-top: 6px !important;
      }}
    }}
  </style>
</head>
<body style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f1f5f9; margin: 0; padding: 20px 10px; color: #1e293b;"">
  
  <div class=""mobile-container"" style=""max-width: 620px; margin: 0 auto; background-color: #ffffff; border: 1px solid #e2e8f0; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 20px rgba(0,0,0,0.06);"">
    
    <!-- 1. HEADER & META BAR -->
    <table style=""width: 100%; border-collapse: collapse; background-color: #ffffff; border-top: 4px solid #dc2626; border-bottom: 1px solid #e2e8f0;"">
      <tr>
        <td class=""stack-col"" style=""padding: 18px 20px; vertical-align: middle;"">
          <img src=""https://www.picknbook.in/assets/picknbook-login-q5fv1iRs.png"" alt=""Pick&amp;book"" height=""34"" style=""height: 34px; max-width: 140px; display: block; border: 0;"" />
          <div style=""font-weight: 800; font-size: 15px; letter-spacing: 0.5px; color: #dc2626; text-transform: uppercase; margin-top: 6px;"">HOTEL CONFIRMATION VOUCHER</div>
          <div style=""color: #64748b; font-size: 11px; margin-top: 3px;"">Booked on <b style=""color: #1e293b;"">{bookingDateStr}</b></div>
        </td>
        <td class=""stack-col text-mobile-left"" style=""padding: 18px 20px; text-align: right; vertical-align: middle; font-size: 12px;"">
          <div><span style=""color: #64748b;"">Booking ID:</span> <b style=""color: #dc2626; font-size: 13px;"">{displayBookingId}</b></div>
          <div style=""margin-top: 4px;""><span style=""color: #64748b; font-size: 11px;"">Booking Ref:</span> <b style=""color: #1e293b; font-size: 12px;"">{reservation.BookingReference}</b> &bull; <span style=""color: #64748b; font-size: 11px;"">Hotel Conf No:</span> <b style=""color: #1e293b; font-size: 12px;"">{confNo}</b></div>
          <div style=""margin-top: 6px;""><span style=""display: inline-block; background-color: #dcfce7; color: #15803d; font-size: 10px; font-weight: 800; padding: 2px 8px; border-radius: 4px; text-transform: uppercase;"">CONFIRMED</span></div>
        </td>
      </tr>
    </table>

    <!-- 2. PROPERTY & ROOM DETAILS -->
    <table style=""width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 1px solid #e2e8f0;"">
      <tr>
        <td class=""stack-col"" style=""padding: 16px 20px; vertical-align: top;"">
          <div style=""font-size: 17px; font-weight: 800; color: #0f172a; word-break: break-word;"">{reservation.HotelName}</div>
          <div style=""font-size: 12px; color: #334155; margin-top: 4px; font-weight: 700;"">{roomTypeName} &nbsp;&bull;&nbsp; <span style=""display: inline-block; background-color: #e2e8f0; color: #0f172a; font-size: 10px; font-weight: 800; padding: 1px 6px; border-radius: 3px;"">[ {nights} NIGHTS STAY ]</span></div>
          <div style=""font-size: 11px; color: #64748b; margin-top: 5px;"">Standard Check-in: 12:00 PM | Standard Check-out: 11:00 AM</div>
        </td>
        <td class=""stack-col text-mobile-left"" style=""padding: 16px 20px; text-align: right; vertical-align: top; font-size: 12px;"">
          <div style=""font-weight: 700; color: #0f172a;"">Voucher # {confNo}</div>
          <div style=""color: #64748b; font-size: 11px; margin-top: 3px;"">Location: {cityDisplay}</div>
        </td>
      </tr>
    </table>

    <!-- 3. STAY TIMELINE -->
    <div style=""padding: 18px 20px; border-bottom: 1px solid #e2e8f0; background-color: #ffffff;"">
      <table style=""width: 100%; border-collapse: collapse;"">
        <tr>
          <td class=""stack-col"" style=""width: 48%; vertical-align: top;"">
            <div style=""font-size: 10px; text-transform: uppercase; color: #64748b; font-weight: 700; letter-spacing: 0.5px;"">CHECK-IN</div>
            <div style=""font-size: 16px; font-weight: 800; color: #0f172a; margin-top: 2px;"">{cityDisplay}</div>
            <div style=""font-size: 12px; color: #334155; font-weight: 700; margin-top: 3px;"">{checkInDateStr}</div>
            <div style=""font-size: 11px; color: #64748b; margin-top: 3px;"">From 12:00 PM (Standard Check-In)</div>
          </td>
          <td class=""stack-col text-mobile-left"" style=""width: 48%; vertical-align: top; text-align: right;"">
            <div style=""font-size: 10px; text-transform: uppercase; color: #64748b; font-weight: 700; letter-spacing: 0.5px;"">CHECK-OUT</div>
            <div style=""font-size: 16px; font-weight: 800; color: #0f172a; margin-top: 2px;"">{cityDisplay}</div>
            <div style=""font-size: 12px; color: #334155; font-weight: 700; margin-top: 3px;"">{checkOutDateStr}</div>
            <div style=""font-size: 11px; color: #64748b; margin-top: 3px;"">Until 11:00 AM (Standard Check-Out)</div>
          </td>
        </tr>
      </table>
      <div style=""margin-top: 14px; padding: 10px 14px; background-color: #fffbeb; border: 1px solid #fef3c7; border-radius: 6px; font-size: 11px; color: #92400e; line-height: 1.4;"">
        &bull; <b>Notice:</b> Standard check-in starts at 12:00 PM. Mandatory government-issued photo ID is required for all adult guests.
      </div>
    </div>

    <!-- 4. GUEST & PAYMENT BREAKDOWN -->
    <table style=""width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 1px solid #e2e8f0;"">
      <tr>
        <!-- GUEST DETAILS -->
        <td class=""stack-col stack-col-border"" style=""width: 52%; vertical-align: top; padding: 16px 14px 16px 20px; border-right: 1px solid #e2e8f0;"">
          <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 8px; text-transform: uppercase; letter-spacing: 0.5px;"">Guest Details</div>
          <table style=""width: 100%; border-collapse: collapse; font-size: 11px; color: #334155;"">
            <tr><td style=""padding: 3px 0; color: #64748b;"">Primary Guest:</td><td style=""padding: 3px 0; font-weight: 700; color: #0f172a; text-align: right;"">{reservation.GuestName}</td></tr>
            <tr><td style=""padding: 3px 0; color: #64748b;"">Email:</td><td style=""padding: 3px 0; text-align: right;"">{reservation.GuestEmail}</td></tr>
            <tr><td style=""padding: 3px 0; color: #64748b;"">Mobile:</td><td style=""padding: 3px 0; text-align: right;"">{reservation.GuestPhone}</td></tr>
            <tr><td style=""padding: 3px 0; color: #64748b;"">Occupancy:</td><td style=""padding: 3px 0; text-align: right;"">{reservation.Rooms} Room(s) &bull; {reservation.Adults} Ad, {reservation.Children} Ch</td></tr>
            <tr><td style=""padding: 3px 0; color: #64748b;"">Room Category:</td><td style=""padding: 3px 0; text-align: right;"">{roomTypeName}</td></tr>
            <tr><td style=""padding: 3px 0; color: #64748b;"">Nationality:</td><td style=""padding: 3px 0; text-align: right;"">{nationalityText}</td></tr>
          </table>
        </td>
        
        <!-- PAYMENT DETAILS -->
        <td class=""stack-col"" style=""width: 48%; vertical-align: top; padding: 16px 20px 16px 14px;"">
          <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 8px; text-transform: uppercase; letter-spacing: 0.5px;"">Payment Details</div>
          <table style=""width: 100%; border-collapse: collapse; font-size: 11px;"">
            <tr>
              <td style=""padding: 3px 0; color: #64748b;"">Room Base Charges:</td>
              <td style=""padding: 3px 0; text-align: right; color: #0f172a; font-weight: 600;"">INR {basePrice:0.00}</td>
            </tr>
            {taxesRowHtml}
            {discountRowHtml}
            <tr style=""border-top: 1px dashed #cbd5e1;"">
              <td style=""padding: 6px 0 2px 0; font-weight: 700; color: #0f172a;"">Total Amount Paid:</td>
              <td style=""padding: 6px 0 2px 0; text-align: right; font-weight: 800; color: #dc2626; font-size: 13px;"">INR {totalPrice:0.00}</td>
            </tr>
          </table>
        </td>
      </tr>
    </table>

    <!-- 5. IMPORTANT INFORMATION -->
    <div style=""padding: 14px 20px; border-bottom: 1px solid #e2e8f0; background-color: #ffffff;"">
      <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 6px; text-transform: uppercase; letter-spacing: 0.5px;"">Important Information</div>
      <ul style=""margin: 0; padding-left: 18px; font-size: 11px; color: #475569; line-height: 1.5;"">
        <li>Mandatory Govt Photo ID Proof (Aadhaar, Passport, DL, Voter ID) required for all adult guests (PAN card not accepted).</li>
        <li>Early check-in and late check-out are subject to availability and may incur additional charges directly at reception.</li>
        <li>For concierge assistance, special requests, or queries, visit <a href=""https://picknbook.in/contact"" style=""color: #0284c7; text-decoration: none; font-weight: 600;"">https://picknbook.in/contact</a></li>
      </ul>
    </div>

    <!-- 6. TERMS & CANCELLATION POLICY -->
    <table style=""width: 100%; border-collapse: collapse; background-color: #f8fafc;"">
      <tr>
        <td class=""stack-col stack-col-border"" style=""width: 50%; vertical-align: top; padding: 16px 14px 16px 20px; border-right: 1px solid #e2e8f0;"">
          <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 6px; text-transform: uppercase; letter-spacing: 0.5px;"">Hotel Terms and Conditions</div>
          <div style=""font-size: 10.5px; color: #64748b; line-height: 1.5;"">
            &bull; Primary guest must be at least 18 years of age with valid photo ID.<br/>
            &bull; Unmarried couples &amp; local ID rules are governed by hotel policies.<br/>
            &bull; Incidentals, mini-bar, and room service are payable directly at the hotel.<br/>
            &bull; Extra bed and child charges are payable directly at reception.<br/>
            &bull; For full booking policies, visit <a href=""https://picknbook.in/terms"" style=""color: #0284c7; text-decoration: none; font-weight: 600;"">https://picknbook.in/terms</a>
          </div>
        </td>
        <td class=""stack-col"" style=""width: 50%; vertical-align: top; padding: 16px 20px 16px 14px;"">
          <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 6px; text-transform: uppercase; letter-spacing: 0.5px;"">Cancellation Policy</div>
          <table style=""width: 100%; border-collapse: collapse; font-size: 10px; color: #334155;"">
            <thead>
              <tr style=""border-bottom: 1px solid #cbd5e1; color: #64748b;"">
                <th style=""padding: 4px 2px; text-align: left;"">Cancellation Time</th>
                <th style=""padding: 4px 2px; text-align: center;"">Refund (%)</th>
                <th style=""padding: 4px 2px; text-align: right;"">Refund Amount</th>
              </tr>
            </thead>
            <tbody>
              {policyRowsHtml}
            </tbody>
          </table>
          <div style=""font-size: 9px; color: #64748b; margin-top: 6px; line-height: 1.4;"">
            &bull; Calculated per hotel cancellation policy terms.
          </div>
        </td>
      </tr>
    </table>

  </div>
</body>
</html>";

        await _emailService.SendEmailWithAttachmentsAsync(
            reservation.GuestEmail,
            subject,
            body,
            new List<EmailAttachment> { attachment });
    }

    public async Task SendHotelCancellationAsync(HotelReservation reservation)
    {
        reservation.Status = "Cancelled";
        var pdfBytes = _ticketPdfService.GenerateHotelTicketPdf(reservation);

        string displayBookingId = !string.IsNullOrWhiteSpace(reservation.ProviderBookingId)
            ? reservation.ProviderBookingId
            : (!string.IsNullOrWhiteSpace(reservation.SrdvBookingId) ? reservation.SrdvBookingId : reservation.BookingReference);

        var attachment = new EmailAttachment
        {
            FileName = $"hotel-cancelled-{displayBookingId}.pdf",
            ContentType = "application/pdf",
            Content = pdfBytes
        };

        var subject = $"Hotel Booking Cancelled - {displayBookingId} - {reservation.HotelName}";

        int nights = Math.Max(1, (reservation.CheckOutDate.Date - reservation.CheckInDate.Date).Days);
        string roomTypeName = !string.IsNullOrWhiteSpace(reservation.RoomTypeName)
            ? reservation.RoomTypeName.Trim()
            : GetRoomCategory(reservation.OfferId);

        string confNo = !string.IsNullOrWhiteSpace(reservation.ConfirmationNo)
            ? reservation.ConfirmationNo
            : (!string.IsNullOrWhiteSpace(reservation.BookingReference) ? reservation.BookingReference : displayBookingId);

        string cancelDateStr = reservation.CancelledAt.HasValue
            ? IndiaTimeHelper.FormatIst(reservation.CancelledAt.Value, "dd MMM yyyy, hh:mm tt")
            : IndiaTimeHelper.FormatIst(DateTime.UtcNow, "dd MMM yyyy, hh:mm tt");

        string checkInDateStr = IndiaTimeHelper.FormatIst(reservation.CheckInDate, "ddd, dd MMM yyyy");
        string checkOutDateStr = IndiaTimeHelper.FormatIst(reservation.CheckOutDate, "ddd, dd MMM yyyy");
        string cityDisplay = !string.IsNullOrWhiteSpace(reservation.CityCode) ? reservation.CityCode : "Hotel Stay";

        decimal totalPrice = reservation.TotalPrice > 0 ? reservation.TotalPrice : reservation.Price;
        var applied = EvaluateAppliedHotelCancellationPolicy(reservation);

        var body = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>Hotel Booking Cancelled - {reservation.BookingReference}</title>
  <style>
    @media only screen and (max-width: 600px) {{
      .mobile-container {{
        width: 100% !important;
        max-width: 100% !important;
        border-radius: 0 !important;
      }}
      .stack-col {{
        display: block !important;
        width: 100% !important;
        box-sizing: border-box !important;
      }}
      .stack-col-border {{
        border-right: none !important;
        border-bottom: 1px solid #e2e8f0 !important;
      }}
      .text-mobile-left {{
        text-align: left !important;
        padding-top: 6px !important;
      }}
    }}
  </style>
</head>
<body style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f1f5f9; margin: 0; padding: 20px 10px; color: #1e293b;"">
  
  <div class=""mobile-container"" style=""max-width: 620px; margin: 0 auto; background-color: #ffffff; border: 1px solid #e2e8f0; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 20px rgba(0,0,0,0.06);"">
    
    <!-- 1. HEADER & META BAR -->
    <table style=""width: 100%; border-collapse: collapse; background-color: #ffffff; border-top: 4px solid #dc2626; border-bottom: 1px solid #e2e8f0;"">
      <tr>
        <td class=""stack-col"" style=""padding: 18px 20px; vertical-align: middle;"">
          <img src=""https://www.picknbook.in/assets/picknbook-login-q5fv1iRs.png"" alt=""Pick&amp;book"" height=""34"" style=""height: 34px; max-width: 140px; display: block; border: 0;"" />
          <div style=""font-weight: 800; font-size: 15px; letter-spacing: 0.5px; color: #dc2626; text-transform: uppercase; margin-top: 6px;"">HOTEL BOOKING CANCELLATION</div>
          <div style=""color: #64748b; font-size: 11px; margin-top: 3px;"">Cancelled on <b style=""color: #1e293b;"">{cancelDateStr}</b></div>
        </td>
        <td class=""stack-col text-mobile-left"" style=""padding: 18px 20px; text-align: right; vertical-align: middle; font-size: 12px;"">
          <div><span style=""color: #64748b;"">Booking ID:</span> <b style=""color: #dc2626; font-size: 13px;"">{displayBookingId}</b></div>
          <div style=""margin-top: 4px;""><span style=""color: #64748b; font-size: 11px;"">Booking Ref:</span> <b style=""color: #1e293b; font-size: 12px;"">{reservation.BookingReference}</b> &bull; <span style=""color: #64748b; font-size: 11px;"">Hotel Conf No:</span> <b style=""color: #1e293b; font-size: 12px;"">{confNo}</b></div>
          <div style=""margin-top: 6px;""><span style=""display: inline-block; background-color: #fee2e2; color: #dc2626; font-size: 10px; font-weight: 800; padding: 2px 8px; border-radius: 4px; text-transform: uppercase;"">CANCELLED</span></div>
        </td>
      </tr>
    </table>

    <!-- 2. PROPERTY & ROOM DETAILS -->
    <table style=""width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 1px solid #e2e8f0;"">
      <tr>
        <td class=""stack-col"" style=""padding: 16px 20px; vertical-align: top;"">
          <div style=""font-size: 17px; font-weight: 800; color: #0f172a; word-break: break-word;"">{reservation.HotelName}</div>
          <div style=""font-size: 12px; color: #334155; margin-top: 4px; font-weight: 700;"">{roomTypeName} &nbsp;&bull;&nbsp; <span style=""display: inline-block; background-color: #e2e8f0; color: #0f172a; font-size: 10px; font-weight: 800; padding: 1px 6px; border-radius: 3px;"">[ {nights} NIGHTS STAY ]</span></div>
          <div style=""font-size: 11px; color: #64748b; margin-top: 5px;"">Standard Check-in: 12:00 PM | Standard Check-out: 11:00 AM</div>
        </td>
        <td class=""stack-col text-mobile-left"" style=""padding: 16px 20px; text-align: right; vertical-align: top; font-size: 12px;"">
          <div style=""font-weight: 700; color: #0f172a;"">Voucher # {confNo}</div>
          <div style=""color: #64748b; font-size: 11px; margin-top: 3px;"">Location: {cityDisplay}</div>
        </td>
      </tr>
    </table>

    <!-- 3. STAY TIMELINE & CANCEL CONFIRMATION -->
    <div style=""padding: 18px 20px; border-bottom: 1px solid #e2e8f0; background-color: #ffffff;"">
      <table style=""width: 100%; border-collapse: collapse;"">
        <tr>
          <td class=""stack-col"" style=""width: 48%; vertical-align: top;"">
            <div style=""font-size: 10px; text-transform: uppercase; color: #64748b; font-weight: 700; letter-spacing: 0.5px;"">SCHEDULED CHECK-IN</div>
            <div style=""font-size: 16px; font-weight: 800; color: #0f172a; margin-top: 2px;"">{cityDisplay}</div>
            <div style=""font-size: 12px; color: #334155; font-weight: 700; margin-top: 3px;"">{checkInDateStr}</div>
          </td>
          <td class=""stack-col text-mobile-left"" style=""width: 48%; vertical-align: top; text-align: right;"">
            <div style=""font-size: 10px; text-transform: uppercase; color: #64748b; font-weight: 700; letter-spacing: 0.5px;"">SCHEDULED CHECK-OUT</div>
            <div style=""font-size: 16px; font-weight: 800; color: #0f172a; margin-top: 2px;"">{cityDisplay}</div>
            <div style=""font-size: 12px; color: #334155; font-weight: 700; margin-top: 3px;"">{checkOutDateStr}</div>
          </td>
        </tr>
      </table>
      <div style=""margin-top: 14px; padding: 10px 14px; background-color: #fef2f2; border: 1px solid #fee2e2; border-radius: 6px; font-size: 11px; color: #991b1b; line-height: 1.4;"">
        &bull; <b>Cancellation Confirmation:</b> This hotel reservation has been cancelled. Hotel rooms have been released and this voucher is no longer valid for check-in.
      </div>
    </div>

    <!-- 4. GUEST & REFUND BREAKDOWN -->
    <table style=""width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 1px solid #e2e8f0;"">
      <tr>
        <!-- GUEST DETAILS -->
        <td class=""stack-col stack-col-border"" style=""width: 52%; vertical-align: top; padding: 16px 14px 16px 20px; border-right: 1px solid #e2e8f0;"">
          <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 8px; text-transform: uppercase; letter-spacing: 0.5px;"">Cancelled Guest Details</div>
          <table style=""width: 100%; border-collapse: collapse; font-size: 11px; color: #334155;"">
            <tr><td style=""padding: 3px 0; color: #64748b;"">Primary Guest:</td><td style=""padding: 3px 0; font-weight: 700; color: #0f172a; text-align: right;"">{reservation.GuestName}</td></tr>
            <tr><td style=""padding: 3px 0; color: #64748b;"">Email:</td><td style=""padding: 3px 0; text-align: right;"">{reservation.GuestEmail}</td></tr>
            <tr><td style=""padding: 3px 0; color: #64748b;"">Mobile:</td><td style=""padding: 3px 0; text-align: right;"">{reservation.GuestPhone}</td></tr>
            <tr><td style=""padding: 3px 0; color: #64748b;"">Room Category:</td><td style=""padding: 3px 0; text-align: right;"">{roomTypeName}</td></tr>
          </table>
        </td>
        
        <!-- REFUND BREAKDOWN -->
        <td class=""stack-col"" style=""width: 48%; vertical-align: top; padding: 16px 20px 16px 14px;"">
          <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 8px; text-transform: uppercase; letter-spacing: 0.5px;"">Refund &amp; Fare Breakdown</div>
          <table style=""width: 100%; border-collapse: collapse; font-size: 11px;"">
            <tr>
              <td style=""padding: 3px 0; color: #64748b;"">Original Total Paid:</td>
              <td style=""padding: 3px 0; text-align: right; color: #0f172a; font-weight: 600;"">INR {totalPrice:0.00}</td>
            </tr>
            <tr>
              <td style=""padding: 3px 0; color: #dc2626;"">Cancellation Charges:</td>
              <td style=""padding: 3px 0; text-align: right; color: #dc2626; font-weight: 600;"">INR {reservation.CancellationCharges:0.00}</td>
            </tr>
            <tr style=""border-top: 1px dashed #cbd5e1;"">
              <td style=""padding: 6px 0 2px 0; font-weight: 700; color: #0f172a;"">Total Refund Amount:</td>
              <td style=""padding: 6px 0 2px 0; text-align: right; font-weight: 800; color: #15803d; font-size: 13px;"">INR {reservation.RefundAmount:0.00}</td>
            </tr>
          </table>
        </td>
      </tr>
    </table>

    <!-- 5. IMPORTANT INFORMATION -->
    <div style=""padding: 14px 20px; border-bottom: 1px solid #e2e8f0; background-color: #ffffff;"">
      <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 6px; text-transform: uppercase; letter-spacing: 0.5px;"">Important Information</div>
      <ul style=""margin: 0; padding-left: 18px; font-size: 11px; color: #475569; line-height: 1.5;"">
        <li>This reservation has been cancelled. Hotel rooms have been released back to hotel inventory.</li>
        <li>For refund inquiries, concierge assistance, or booking help, visit <a href=""https://picknbook.in/contact"" style=""color: #0284c7; text-decoration: none; font-weight: 600;"">https://picknbook.in/contact</a></li>
      </ul>
    </div>

    <!-- 6. CANCELLATION SUMMARY & APPLIED POLICY -->
    <table style=""width: 100%; border-collapse: collapse; background-color: #f8fafc;"">
      <tr>
        <td class=""stack-col stack-col-border"" style=""width: 50%; vertical-align: top; padding: 16px 14px 16px 20px; border-right: 1px solid #e2e8f0;"">
          <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 6px; text-transform: uppercase; letter-spacing: 0.5px;"">Cancellation Summary</div>
          <div style=""font-size: 10.5px; color: #64748b; line-height: 1.5;"">
            &bull; The booking is cancelled in accordance with supplier terms.<br/>
            &bull; Refund has been processed to source payment method / wallet.<br/>
            &bull; Bank settlement timeline: 3-7 business days if applicable.<br/>
            &bull; For inquiries, visit <a href=""https://picknbook.in/contact"" style=""color: #0284c7; text-decoration: none; font-weight: 600;"">https://picknbook.in/contact</a>
          </div>
        </td>
        <td class=""stack-col"" style=""width: 50%; vertical-align: top; padding: 16px 20px 16px 14px;"">
          <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 6px; text-transform: uppercase; letter-spacing: 0.5px;"">Cancellation Policy Applied</div>
          <table style=""width: 100%; border-collapse: collapse; font-size: 10px; color: #334155;"">
            <thead>
              <tr style=""border-bottom: 1px solid #cbd5e1; color: #64748b;"">
                <th style=""padding: 4px 2px; text-align: left;"">Window / Status</th>
                <th style=""padding: 4px 2px; text-align: center;"">Refund (%)</th>
                <th style=""padding: 4px 2px; text-align: right;"">Refund Amount</th>
              </tr>
            </thead>
            <tbody>
              <tr style=""border-bottom: 1px solid #e2e8f0;"">
                <td style=""padding: 5px 4px; color: #334155;"">{applied.Window}</td>
                <td style=""padding: 5px 4px; text-align: center; color: #16a34a; font-weight: 600;"">{applied.RefundPct}</td>
                <td style=""padding: 5px 4px; text-align: right; color: #15803d; font-weight: 700;"">{applied.RefundAmt}</td>
              </tr>
            </tbody>
          </table>
          <div style=""font-size: 9px; color: #64748b; margin-top: 6px; line-height: 1.4;"">
            &bull; *{applied.Note}
          </div>
        </td>
      </tr>
    </table>

  </div>
</body>
</html>";

        await _emailService.SendEmailWithAttachmentsAsync(
            reservation.GuestEmail,
            subject,
            body,
            new List<EmailAttachment> { attachment });
    }

    private static string GetRoomCategory(string offerId)
    {
        if (string.IsNullOrWhiteSpace(offerId)) return "Standard Room";
        if (offerId.Contains("suite", StringComparison.OrdinalIgnoreCase)) return "Executive Suite";
        if (offerId.Contains("deluxe", StringComparison.OrdinalIgnoreCase)) return "Deluxe Room";
        return "Standard Room";
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
            var matching = policies.FirstOrDefault(p => Math.Abs(p.DeductionAmount - actualCharge) < 1.0m || Math.Abs(p.DeductionAmount - (actualCharge - 35.00m)) < 1.0m);
            if (matching != default)
            {
                return (matching.TimeWindow, matching.RefundPct, $"INR {actualCharge:0.00} (Fee)", $"INR {actualRefund:0.00} (Refunded)", $"Cancelled within {matching.TimeWindow}");
            }
        }

        return (windowText, $"{refundPct:0}% Refund", $"INR {actualCharge:0.00} (Fee)", $"INR {actualRefund:0.00} (Refunded)", note);
    }

    private static string GenerateSvgBarcode(string text)
    {
        var random = new Random(text.GetHashCode());
        var sb = new System.Text.StringBuilder();
        sb.Append("<svg width='120' height='30' xmlns='http://www.w3.org/2000/svg'>");
        int x = 5;
        while (x < 115)
        {
            int w = random.Next(1, 4);
            sb.Append($"<rect x='{x}' y='0' width='{w}' height='30' fill='black' />");
            x += w + random.Next(1, 3);
        }
        sb.Append("</svg>");
        return sb.ToString();
    }
}



