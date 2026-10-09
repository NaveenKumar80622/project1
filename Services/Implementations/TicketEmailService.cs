using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;

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

        var pdfs = _ticketPdfService.GenerateFlightTicketPdf(request);

        var attachments = pdfs.Select(p => new EmailAttachment
        {
            FileName = p.FileName,
            ContentType = "application/pdf",
            Content = p.Content
        }).ToList();

        var subject =
            $"Your Pick&book Ticket - {request.BookingReference}";

        // Fetch flight reservation, booking details, and passengers from the database
        var reservation = await _context.FlightReservations
            .FirstOrDefaultAsync(r => r.BookingReference == request.BookingReference);

        var passengersList = new List<FlightPassengerTicketDto>();
        string flightNumber = "N/A";
        string airline = request.Airline;

        if (reservation != null)
        {
            if (true)
            {
                flightNumber = reservation.FlightNumber;
                airline = reservation.Airline;
            }

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

        // Fallback to primary passenger details if the list is empty
        if (passengersList.Count == 0)
        {
            passengersList.Add(new FlightPassengerTicketDto
            {
                FullName = request.PassengerName,
                SeatNumber = request.SeatNumber
            });
        }

        if (string.IsNullOrWhiteSpace(flightNumber) || flightNumber == "N/A")
        {
            flightNumber = !string.IsNullOrWhiteSpace(request.Pnr) ? request.Pnr : "6E-437";
        }

        var departureIst = ToIst(request.DepartureTime);
        var boardingTime = departureIst.AddMinutes(-40).ToString("hh:mm tt").ToUpper();
        var departureDateStr = departureIst.ToString("dd MMM yyyy").ToUpper();

        var originCity = GetCityName(request.Origin);
        var destinationCity = GetCityName(request.Destination);

        var boardingPassesHtml = new System.Text.StringBuilder();

        foreach (var passenger in passengersList)
        {
            var pSeat = string.IsNullOrWhiteSpace(passenger.SeatNumber) ? "10A" : passenger.SeatNumber;
            var pSeatsArray = string.IsNullOrWhiteSpace(passenger.SeatNumber) 
                ? new[] { "10A" } 
                : passenger.SeatNumber.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var barcodeSvg = GenerateSvgBarcode(request.BookingReference);

            if (request.Segments != null && request.Segments.Any())
            {
                int legIndex = 1;
                foreach (var seg in request.Segments)
                {
                    var currentLegSeat = pSeatsArray.Length >= legIndex 
                        ? pSeatsArray[legIndex - 1] 
                        : (pSeatsArray.Length > 0 ? pSeatsArray.Last() : "10A");

                    bool isCancelled = (passenger.Status == "Cancelled" || seg.Status == "Cancelled");
                    string bgStyle = isCancelled ? "background-color: #fff0f0;" : "background-color: #ffffff;";
                    string statusBadge = isCancelled ? "CANCELLED" : "ECONOMY";
                    string statusColor = isCancelled ? "#991b1b" : "#d9251c";
                    string watermarkHtml = isCancelled ? @"<div style=""position: absolute; top: 30%; left: 10%; font-size: 60px; color: rgba(255, 0, 0, 0.1); font-weight: bold; transform: rotate(-30deg); pointer-events: none; white-space: nowrap; letter-spacing: 5px; z-index: 0;"">CANCELLED</div>" : "";
                    
                    var legDepartureIst = ToIst(seg.DepartureTime);
                    var legBoardingTime = legDepartureIst.AddMinutes(-40).ToString("hh:mm tt").ToUpper();
                    var legDepartureDateStr = legDepartureIst.ToString("dd MMM yyyy").ToUpper();
                    var pnrDisplay = string.IsNullOrWhiteSpace(seg.Pnr) ? request.BookingReference : seg.Pnr;
                    var legOriginCity = GetCityName(seg.FromCity);
                    var legDestCity = GetCityName(seg.ToCity);

                    boardingPassesHtml.Append($@"
    <h4 style=""color: {statusColor}; font-size: 13px; margin: 25px 0 10px 0; font-weight: 800; letter-spacing: 1px; text-transform: uppercase;"">
        FLIGHT LEG {legIndex}: {legOriginCity} &rarr; {legDestCity} <span style=""float: right; color: #78829b;"">PNR: {pnrDisplay}</span>
    </h4>
    <div style=""position: relative; margin-bottom: 25px; {bgStyle} border: 1px solid #e2e8f0; border-radius: 16px; overflow: hidden; box-shadow: 0 4px 20px rgba(15, 36, 89, 0.08); width: 100%; display: table; border-collapse: collapse;"">
        {watermarkHtml}
        <div style=""display: table-row; position: relative; z-index: 1;"">
            
            <!-- Left Side: Main Boarding Pass -->
            <div style=""display: table-cell; width: 70%; padding: 25px; vertical-align: top;"">
                
                <!-- Header -->
                <table style=""width: 100%; border-collapse: collapse; margin-bottom: 20px;"">
                    <tr>
                        <td style=""vertical-align: middle;"">
                            <span style=""font-size: 20px; font-weight: 800; color: #0f2459; letter-spacing: 1px; text-transform: uppercase;"">{seg.Airline}</span>
                        </td>
                        <td style=""text-align: center; vertical-align: middle;"">
                            <span style=""font-size: 12px; font-weight: bold; color: #78829b; letter-spacing: 3px; text-transform: uppercase;"">BOARDING PASS</span>
                        </td>
                        <td style=""text-align: right; vertical-align: middle;"">
                            <span style=""display: inline-block; background-color: {statusColor}; color: #ffffff; font-size: 10px; font-weight: bold; padding: 4px 12px; border-radius: 5px; text-transform: uppercase; letter-spacing: 1px;"">{statusBadge}</span>
                        </td>
                    </tr>
                </table>

                <!-- Route Codes -->
                <table style=""width: 100%; border-collapse: collapse; margin-bottom: 25px;"">
                    <tr>
                        <td style=""width: 35%; vertical-align: top;"">
                            <div style=""font-size: 42px; font-weight: 900; color: #0f2459; line-height: 1; margin: 0;"">{seg.FromCity}</div>
                            <div style=""font-size: 11px; color: #78829b; font-weight: 600; margin-top: 4px; text-transform: uppercase;"">{legOriginCity}</div>
                        </td>
                        <td style=""width: 30%; text-align: center; vertical-align: middle;"">
                            <div style=""font-size: 20px; color: {statusColor}; font-weight: bold; letter-spacing: 4px;"">&bull; <span style=""font-size: 22px; vertical-align: middle;"">&#9992;</span> &bull;</div>
                        </td>
                        <td style=""width: 35%; text-align: right; vertical-align: top;"">
                            <div style=""font-size: 42px; font-weight: 900; color: #0f2459; line-height: 1; margin: 0;"">{seg.ToCity}</div>
                            <div style=""font-size: 11px; color: #78829b; font-weight: 600; margin-top: 4px; text-transform: uppercase;"">{legDestCity}</div>
                        </td>
                    </tr>
                </table>

                <!-- Flight Info Row -->
                <table style=""width: 100%; border-collapse: collapse; margin-bottom: 20px;"">
                    <tr>
                        <td style=""width: 45%; vertical-align: top; padding-right: 10px;"">
                            <div style=""font-size: 9px; color: #78829b; font-weight: bold; text-transform: uppercase; margin-bottom: 4px; letter-spacing: 0.5px;"">PASSENGER NAME</div>
                            <div style=""font-size: 13px; font-weight: 700; color: #0f2459; text-transform: uppercase;"">{passenger.FullName}</div>
                        </td>
                        <td style=""width: 25%; vertical-align: top; padding-right: 10px;"">
                            <div style=""font-size: 9px; color: #78829b; font-weight: bold; text-transform: uppercase; margin-bottom: 4px; letter-spacing: 0.5px;"">FLIGHT</div>
                            <div style=""font-size: 13px; font-weight: 700; color: #0f2459; text-transform: uppercase;"">{seg.FlightNumber}</div>
                        </td>
                        <td style=""width: 30%; vertical-align: top;"">
                            <div style=""font-size: 9px; color: #78829b; font-weight: bold; text-transform: uppercase; margin-bottom: 4px; letter-spacing: 0.5px;"">DATE</div>
                            <div style=""font-size: 13px; font-weight: 700; color: #0f2459; text-transform: uppercase;"">{legDepartureDateStr}</div>
                        </td>
                    </tr>
                </table>

                <!-- Bottom Row Box -->
                <div style=""background-color: {(isCancelled ? "#ffecec" : "#f8fafc")}; border: 1px solid #f1f5f9; border-radius: 12px; padding: 15px 20px;"">
                    <table style=""width: 100%; border-collapse: collapse;"">
                        <tr>
                            <td style=""width: 25%; vertical-align: top; padding-right: 10px;"">
                                <div style=""font-size: 9px; color: #78829b; font-weight: bold; margin-bottom: 4px; letter-spacing: 0.5px; text-transform: uppercase;"">SEAT</div>
                                <div style=""font-size: 20px; font-weight: 800; color: {statusColor}; text-transform: uppercase;"">{(isCancelled ? "--" : currentLegSeat)}</div>
                            </td>
                            <td style=""width: 25%; vertical-align: top; padding-right: 10px;"">
                                <div style=""font-size: 9px; color: #78829b; font-weight: bold; margin-bottom: 4px; letter-spacing: 0.5px; text-transform: uppercase;"">GATE</div>
                                <div style=""font-size: 20px; font-weight: 800; color: #0f2459; text-transform: uppercase;"">{(isCancelled ? "--" : (string.IsNullOrWhiteSpace(request.Terminal) ? "12A" : request.Terminal))}</div>
                            </td>
                            <td style=""width: 50%; vertical-align: top;"">
                                <div style=""font-size: 9px; color: #78829b; font-weight: bold; margin-bottom: 4px; letter-spacing: 0.5px; text-transform: uppercase;"">BOARDING TIME</div>
                                <div style=""font-size: 14px; font-weight: 800; color: {statusColor}; text-transform: uppercase;"">{(isCancelled ? "CANCELLED" : legBoardingTime)} <span style=""font-size: 10px; font-weight: normal; color: #78829b;"">{(isCancelled ? "" : "(BOARDING: 40M PRIOR)")}</span></div>
                            </td>
                        </tr>
                    </table>
                </div>

            </div>

            <!-- Separator Line -->
            <div style=""display: table-cell; width: 2px; vertical-align: middle; padding: 0;"">
                <div style=""height: 100%; width: 100%; border-left: 2px dashed #e2e8f0; font-size: 0; line-height: 0;"">&nbsp;</div>
            </div>

            <!-- Right Side: Stub -->
            <div style=""display: table-cell; width: 28%; padding: 25px; vertical-align: top; background-color: {(isCancelled ? "#ffebeb" : "#fafbfc")};"">
                
                <!-- Stub Header -->
                <table style=""width: 100%; border-collapse: collapse; margin-bottom: 15px;"">
                    <tr>
                        <td style=""vertical-align: middle;"">
                            <span style=""font-size: 14px; font-weight: 800; color: #0f2459; text-transform: uppercase;"">{seg.Airline}</span>
                        </td>
                        <td style=""text-align: right; vertical-align: middle;"">
                            <span style=""font-size: 9px; font-weight: bold; color: #78829b; text-transform: uppercase;"">{statusBadge}</span>
                        </td>
                    </tr>
                </table>

                <!-- Stub Route Code -->
                <div style=""font-size: 16px; font-weight: 800; color: #0f2459; margin-bottom: 15px;"">
                    {seg.FromCity} <span style=""color: {statusColor}; font-size: 14px; vertical-align: middle;"">&#10142;</span> {seg.ToCity}
                </div>

                <!-- Stub Passenger Details -->
                <div style=""margin-bottom: 12px;"">
                    <div style=""font-size: 8px; color: #78829b; font-weight: bold; text-transform: uppercase; margin-bottom: 2px;"">PASSENGER</div>
                    <div style=""font-size: 11px; font-weight: 700; color: #0f2459; text-transform: uppercase; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 150px;"">{passenger.FullName}</div>
                </div>

                <!-- Stub Flight/Seat info -->
                <table style=""width: 100%; border-collapse: collapse; margin-bottom: 15px;"">
                    <tr>
                        <td style=""vertical-align: top; padding-right: 10px;"">
                            <div style=""font-size: 8px; color: #78829b; font-weight: bold; text-transform: uppercase; margin-bottom: 2px;"">FLIGHT</div>
                            <div style=""font-size: 11px; font-weight: 700; color: #0f2459; text-transform: uppercase;"">{seg.FlightNumber}</div>
                        </td>
                        <td style=""vertical-align: top; text-align: right;"">
                            <div style=""font-size: 8px; color: #78829b; font-weight: bold; text-transform: uppercase; margin-bottom: 2px;"">SEAT</div>
                            <div style=""font-size: 13px; font-weight: 800; color: {statusColor}; text-transform: uppercase;"">{(isCancelled ? "--" : currentLegSeat)}</div>
                        </td>
                    </tr>
                </table>

                <!-- Barcode -->
                <div style=""text-align: center; margin-top: 15px; {(isCancelled ? "opacity: 0.3;" : "")}"">
                    {barcodeSvg}
                    <div style=""font-size: 8px; color: #78829b; font-weight: bold; letter-spacing: 1px; margin-top: 3px;"">{pnrDisplay}</div>
                </div>

            </div>

        </div>
    </div>");
                    legIndex++;
                }
            }
            else
            {
                // Fallback for single-leg / legacy data
                bool isCancelled = (passenger.Status == "Cancelled");
                string bgStyle = isCancelled ? "background-color: #fff0f0;" : "background-color: #ffffff;";
                string statusBadge = isCancelled ? "CANCELLED" : "ECONOMY";
                string statusColor = isCancelled ? "#991b1b" : "#d9251c";
                string watermarkHtml = isCancelled ? @"<div style=""position: absolute; top: 30%; left: 10%; font-size: 60px; color: rgba(255, 0, 0, 0.1); font-weight: bold; transform: rotate(-30deg); pointer-events: none; white-space: nowrap; letter-spacing: 5px; z-index: 0;"">CANCELLED</div>" : "";

                boardingPassesHtml.Append($@"
    <div style=""position: relative; margin-bottom: 25px; {bgStyle} border: 1px solid #e2e8f0; border-radius: 16px; overflow: hidden; box-shadow: 0 4px 20px rgba(15, 36, 89, 0.08); width: 100%; display: table; border-collapse: collapse;"">
        {watermarkHtml}
        <div style=""display: table-row; position: relative; z-index: 1;"">
            
            <!-- Left Side: Main Boarding Pass -->
            <div style=""display: table-cell; width: 70%; padding: 25px; vertical-align: top;"">
                
                <!-- Header -->
                <table style=""width: 100%; border-collapse: collapse; margin-bottom: 20px;"">
                    <tr>
                        <td style=""vertical-align: middle;"">
                            <span style=""font-size: 20px; font-weight: 800; color: #0f2459; letter-spacing: 1px; text-transform: uppercase;"">{airline}</span>
                        </td>
                        <td style=""text-align: center; vertical-align: middle;"">
                            <span style=""font-size: 12px; font-weight: bold; color: #78829b; letter-spacing: 3px; text-transform: uppercase;"">BOARDING PASS</span>
                        </td>
                        <td style=""text-align: right; vertical-align: middle;"">
                            <span style=""display: inline-block; background-color: {statusColor}; color: #ffffff; font-size: 10px; font-weight: bold; padding: 4px 12px; border-radius: 5px; text-transform: uppercase; letter-spacing: 1px;"">{statusBadge}</span>
                        </td>
                    </tr>
                </table>

                <!-- Route Codes -->
                <table style=""width: 100%; border-collapse: collapse; margin-bottom: 25px;"">
                    <tr>
                        <td style=""width: 35%; vertical-align: top;"">
                            <div style=""font-size: 42px; font-weight: 900; color: #0f2459; line-height: 1; margin: 0;"">{request.Origin}</div>
                            <div style=""font-size: 11px; color: #78829b; font-weight: 600; margin-top: 4px; text-transform: uppercase;"">{originCity}</div>
                        </td>
                        <td style=""width: 30%; text-align: center; vertical-align: middle;"">
                            <div style=""font-size: 20px; color: {statusColor}; font-weight: bold; letter-spacing: 4px;"">&bull; <span style=""font-size: 22px; vertical-align: middle;"">&#9992;</span> &bull;</div>
                        </td>
                        <td style=""width: 35%; text-align: right; vertical-align: top;"">
                            <div style=""font-size: 42px; font-weight: 900; color: #0f2459; line-height: 1; margin: 0;"">{request.Destination}</div>
                            <div style=""font-size: 11px; color: #78829b; font-weight: 600; margin-top: 4px; text-transform: uppercase;"">{destinationCity}</div>
                        </td>
                    </tr>
                </table>

                <!-- Flight Info Row -->
                <table style=""width: 100%; border-collapse: collapse; margin-bottom: 20px;"">
                    <tr>
                        <td style=""width: 45%; vertical-align: top; padding-right: 10px;"">
                            <div style=""font-size: 9px; color: #78829b; font-weight: bold; text-transform: uppercase; margin-bottom: 4px; letter-spacing: 0.5px;"">PASSENGER NAME</div>
                            <div style=""font-size: 13px; font-weight: 700; color: #0f2459; text-transform: uppercase;"">{passenger.FullName}</div>
                        </td>
                        <td style=""width: 25%; vertical-align: top; padding-right: 10px;"">
                            <div style=""font-size: 9px; color: #78829b; font-weight: bold; text-transform: uppercase; margin-bottom: 4px; letter-spacing: 0.5px;"">FLIGHT</div>
                            <div style=""font-size: 13px; font-weight: 700; color: #0f2459; text-transform: uppercase;"">{flightNumber}</div>
                        </td>
                        <td style=""width: 30%; vertical-align: top;"">
                            <div style=""font-size: 9px; color: #78829b; font-weight: bold; text-transform: uppercase; margin-bottom: 4px; letter-spacing: 0.5px;"">DATE</div>
                            <div style=""font-size: 13px; font-weight: 700; color: #0f2459; text-transform: uppercase;"">{departureDateStr}</div>
                        </td>
                    </tr>
                </table>

                <!-- Bottom Row Box -->
                <div style=""background-color: {(isCancelled ? "#ffecec" : "#f8fafc")}; border: 1px solid #f1f5f9; border-radius: 12px; padding: 15px 20px;"">
                    <table style=""width: 100%; border-collapse: collapse;"">
                        <tr>
                            <td style=""width: 25%; vertical-align: top; padding-right: 10px;"">
                                <div style=""font-size: 9px; color: #78829b; font-weight: bold; margin-bottom: 4px; letter-spacing: 0.5px; text-transform: uppercase;"">SEAT</div>
                                <div style=""font-size: 20px; font-weight: 800; color: {statusColor}; text-transform: uppercase;"">{(isCancelled ? "--" : pSeat)}</div>
                            </td>
                            <td style=""width: 25%; vertical-align: top; padding-right: 10px;"">
                                <div style=""font-size: 9px; color: #78829b; font-weight: bold; margin-bottom: 4px; letter-spacing: 0.5px; text-transform: uppercase;"">GATE</div>
                                <div style=""font-size: 20px; font-weight: 800; color: #0f2459; text-transform: uppercase;"">{(isCancelled ? "--" : (string.IsNullOrWhiteSpace(request.Terminal) ? "12A" : request.Terminal))}</div>
                            </td>
                            <td style=""width: 50%; vertical-align: top;"">
                                <div style=""font-size: 9px; color: #78829b; font-weight: bold; margin-bottom: 4px; letter-spacing: 0.5px; text-transform: uppercase;"">BOARDING TIME</div>
                                <div style=""font-size: 14px; font-weight: 800; color: {statusColor}; text-transform: uppercase;"">{(isCancelled ? "CANCELLED" : boardingTime)} <span style=""font-size: 10px; font-weight: normal; color: #78829b;"">{(isCancelled ? "" : "(BOARDING: 40M PRIOR)")}</span></div>
                            </td>
                        </tr>
                    </table>
                </div>

            </div>

            <!-- Separator Line -->
            <div style=""display: table-cell; width: 2px; vertical-align: middle; padding: 0;"">
                <div style=""height: 100%; width: 100%; border-left: 2px dashed #e2e8f0; font-size: 0; line-height: 0;"">&nbsp;</div>
            </div>

            <!-- Right Side: Stub -->
            <div style=""display: table-cell; width: 28%; padding: 25px; vertical-align: top; background-color: {(isCancelled ? "#ffebeb" : "#fafbfc")};"">
                
                <!-- Stub Header -->
                <table style=""width: 100%; border-collapse: collapse; margin-bottom: 15px;"">
                    <tr>
                        <td style=""vertical-align: middle;"">
                            <span style=""font-size: 14px; font-weight: 800; color: #0f2459; text-transform: uppercase;"">{airline}</span>
                        </td>
                        <td style=""text-align: right; vertical-align: middle;"">
                            <span style=""font-size: 9px; font-weight: bold; color: #78829b; text-transform: uppercase;"">{statusBadge}</span>
                        </td>
                    </tr>
                </table>

                <!-- Stub Route Code -->
                <div style=""font-size: 16px; font-weight: 800; color: #0f2459; margin-bottom: 15px;"">
                    {request.Origin} <span style=""color: {statusColor}; font-size: 14px; vertical-align: middle;"">&#10142;</span> {request.Destination}
                </div>

                <!-- Stub Passenger Details -->
                <div style=""margin-bottom: 12px;"">
                    <div style=""font-size: 8px; color: #78829b; font-weight: bold; text-transform: uppercase; margin-bottom: 2px;"">PASSENGER</div>
                    <div style=""font-size: 11px; font-weight: 700; color: #0f2459; text-transform: uppercase; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 150px;"">{passenger.FullName}</div>
                </div>

                <!-- Stub Flight/Seat info -->
                <table style=""width: 100%; border-collapse: collapse; margin-bottom: 15px;"">
                    <tr>
                        <td style=""vertical-align: top; padding-right: 10px;"">
                            <div style=""font-size: 8px; color: #78829b; font-weight: bold; text-transform: uppercase; margin-bottom: 2px;"">FLIGHT</div>
                            <div style=""font-size: 11px; font-weight: 700; color: #0f2459; text-transform: uppercase;"">{flightNumber}</div>
                        </td>
                        <td style=""vertical-align: top; text-align: right;"">
                            <div style=""font-size: 8px; color: #78829b; font-weight: bold; text-transform: uppercase; margin-bottom: 2px;"">SEAT</div>
                            <div style=""font-size: 13px; font-weight: 800; color: {statusColor}; text-transform: uppercase;"">{(isCancelled ? "--" : pSeat)}</div>
                        </td>
                    </tr>
                </table>

                <!-- Barcode -->
                <div style=""text-align: center; margin-top: 15px; {(isCancelled ? "opacity: 0.3;" : "")}"">
                    {barcodeSvg}
                    <div style=""font-size: 8px; color: #78829b; font-weight: bold; letter-spacing: 1px; margin-top: 3px;"">{request.BookingReference}</div>
                </div>

            </div>

        </div>
    </div>");
            }
        }

    var cancellationSection = string.Empty;
    if (request.NonRefundable)
    {
        cancellationSection = @"
    <div style=""background-color: #fef2f2; border: 1px solid #fecaca; border-radius: 12px; padding: 20px; margin-top: 15px;"">
        <h4 style=""color: #991b1b; margin: 0 0 10px 0; font-size: 14px;"">Cancellation Policy</h4>
        <p style=""color: #991b1b; font-weight: bold; font-size: 13px; margin: 0;"">This ticket is NON-REFUNDABLE.</p>
    </div>";
    }
    else if (!string.IsNullOrWhiteSpace(request.CancellationCharges))
    {
        cancellationSection = $@"
    <div style=""background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 12px; padding: 20px; margin-top: 15px;"">
        <h4 style=""color: #0f2459; margin: 0 0 10px 0; font-size: 14px;"">Cancellation Policy</h4>
        <p style=""color: #5a6578; font-size: 13px; margin: 0;"">{request.CancellationCharges}</p>
    </div>";
    }

        var body = $@"
<div style=""font-family: 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f4f6fa; padding: 30px 20px; max-width: 850px; margin: 0 auto; border-radius: 12px;"">
    
    <div style=""background: linear-gradient(135deg, #0f2459 0%, #1e3a8a 100%); color: #ffffff; padding: 26px 20px; text-align: center; border-radius: 12px; margin-bottom: 25px; box-shadow: 0 4px 16px rgba(0,0,0,0.08);"">
        <div style=""background: #ffffff; display: inline-block; padding: 7px 22px; border-radius: 8px; box-shadow: 0 4px 12px rgba(0,0,0,0.18); margin-bottom: 12px;"">
            <img src=""https://www.picknbook.in/assets/picknbook-login-q5fv1iRs.png"" alt=""Pick&amp;book"" style=""height: 32px; width: auto; display: block; border: 0;"" />
        </div>
        <h2 style=""margin: 0; font-size: 22px; font-weight: 700; letter-spacing: 0.5px; color: #ffffff;"">Flight Booking Confirmed!</h2>
        <p style=""margin: 6px 0 0 0; font-size: 13px; color: #94a3b8;"">Booking Reference: <b style=""color: #f8fafc;"">{request.BookingReference}</b></p>
    </div>

    {boardingPassesHtml}

    {cancellationSection}

    <!-- Additional Helpful Details Box -->
    <div style=""background-color: #ffffff; border: 1px solid #e2e8f0; border-radius: 12px; padding: 20px; box-shadow: 0 4px 12px rgba(15, 36, 89, 0.04); margin-top: 15px;"">
        <h4 style=""color: #0f2459; margin: 0 0 10px 0; font-size: 14px;"">Important Information</h4>
        <ul style=""color: #5a6578; font-size: 12px; margin: 0; padding-left: 20px; line-height: 1.6;"">
            <li>Please carry a valid government-issued photo ID along with this boarding pass to the airport.</li>
            <li>Boarding gates close 20 minutes prior to departure. We recommend reaching the gate at least 45 minutes before departure.</li>
            <li>For domestic flights, check-in baggage counter closes 45 minutes prior to departure.</li>
            <li>This is a system-generated ticket, and a copy of your ticket PDF is attached to this email.</li>
        </ul>
    </div>

    <div style=""text-align: center; margin-top: 25px; font-size: 11px; color: #94a3b8;"">
        &copy; 2026 Pick&amp;book Travel Services. All rights reserved.
    </div>

</div>";

        await _emailService.SendEmailWithAttachmentsAsync(
            request.ToEmail,
            subject,
            body,
            attachments);
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
        var boardingTime = ToIst(request.BoardingPointTime != default ? request.BoardingPointTime : request.DepartureTime);
        var droppingTime = ToIst(request.ArrivalPointTime != default ? request.ArrivalPointTime : request.ArrivalTime);
        var boardingTimeStr = boardingTime.ToString("ddd, dd MMM yyyy hh:mm tt");
        var droppingTimeStr = droppingTime.ToString("ddd, dd MMM yyyy hh:mm tt");
        var departureTimeStr = ToIst(request.DepartureTime).ToString("hh:mm tt");
        var reportingTimeStr = boardingTime.AddMinutes(-15).ToString("hh:mm tt");
        var pListSummary = request.Passengers.Count > 0
            ? string.Join(", ", request.Passengers.Select(p => $"{p.FullName} (Seat {p.SeatNumber})"))
            : $"{request.PassengerName} (Seat {request.SeatNumber})";

        var qrPlainText = $"PICK&BOOK BUS TICKET\n" +
                          $"Booking ID: {request.BookingReference}\n" +
                          $"PNR: {request.Pnr}\n" +
                          $"Operator: {request.OperatorName} ({request.BusType})\n" +
                          $"Route: {request.Origin} -> {request.Destination}\n" +
                          $"Boarding: {request.BoardingPoint ?? "-"} ({boardingTimeStr})\n" +
                          $"Dropping: {request.ArrivalPoint ?? "-"} ({droppingTimeStr})\n" +
                          $"Passengers: {pListSummary}\n" +
                          $"Fare: INR {request.Price:0.00}\n" +
                          $"Status: CONFIRMED\n" +
                          $"Support: https://picknbook.in/contact";

        var qrImgUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=95x95&data={Uri.EscapeDataString(qrPlainText)}";
        var bookingDateStr = ToIst(DateTime.UtcNow).ToString("dd MMM yyyy, hh:mm tt");
        var baseFareVal = request.BaseFare > 0 ? request.BaseFare : Math.Max(0, request.Price - request.GstAmount);

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
          <div style=""margin-top: 4px;""><span style=""color: #64748b; font-size: 11px;"">Bus Partner PNR:</span> <b style=""color: #1e293b; font-size: 12px;"">{request.Pnr}</b></div>
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

    <!-- 3. JOURNEY ROUTE & TIMELINE WITH QR CODE -->
    <div style=""padding: 18px 20px; border-bottom: 1px solid #e2e8f0; background-color: #ffffff;"">
      <table style=""width: 100%; border-collapse: collapse;"">
        <tr>
          <td class=""stack-col"" style=""width: 36%; vertical-align: top;"">
            <div style=""font-size: 11px; text-transform: uppercase; color: #64748b; font-weight: 700; letter-spacing: 0.5px;"">DEPARTURE</div>
            <div style=""font-size: 17px; font-weight: 800; color: #0f172a; margin-top: 2px;"">{request.Origin}</div>
            <div style=""font-size: 12px; color: #334155; font-weight: 600; margin-top: 2px;"">{boardingTimeStr}</div>
            <div style=""margin-top: 8px; font-size: 11px; color: #64748b; line-height: 1.4;"">
              <b style=""color: #334155;"">Boarding Point:</b><br/>{request.BoardingPoint}
            </div>
          </td>
          <td class=""route-arrow"" style=""width: 8%; text-align: center; vertical-align: middle;"">
            <div style=""color: #dc2626; font-size: 22px; font-weight: bold; line-height: 1;"">&#10142;</div>
          </td>
          <td class=""stack-col"" style=""width: 36%; vertical-align: top;"">
            <div style=""font-size: 11px; text-transform: uppercase; color: #64748b; font-weight: 700; letter-spacing: 0.5px;"">ARRIVAL</div>
            <div style=""font-size: 17px; font-weight: 800; color: #0f172a; margin-top: 2px;"">{request.Destination}</div>
            <div style=""font-size: 12px; color: #334155; font-weight: 600; margin-top: 2px;"">{droppingTimeStr}</div>
            <div style=""margin-top: 8px; font-size: 11px; color: #64748b; line-height: 1.4;"">
              <b style=""color: #334155;"">Dropping Point:</b><br/>{request.ArrivalPoint}
            </div>
          </td>
          <td class=""stack-col text-mobile-center"" style=""width: 20%; text-align: center; vertical-align: middle; padding-left: 10px; border-left: 1px dashed #e2e8f0;"">
            <img src=""{qrImgUrl}"" width=""85"" height=""85"" alt=""Ticket QR"" style=""display: inline-block; width: 85px; height: 85px; border: 1px solid #e2e8f0; border-radius: 6px; padding: 2px; background: #ffffff;"" />
            <div style=""font-size: 9px; color: #64748b; font-weight: 700; text-transform: uppercase; margin-top: 4px; letter-spacing: 0.5px;"">Scan for Details</div>
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
              <td style=""padding: 4px 0; color: #64748b;"">Basic Fare:</td>
              <td style=""padding: 4px 0; text-align: right; color: #0f172a; font-weight: 600;"">&#8377;{baseFareVal:0.00}</td>
            </tr>
            {gstRow}
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

        await _emailService.SendEmailWithAttachmentsAsync(
            request.ToEmail,
            subject,
            body,
            [attachment]);
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

        var discountRow = totalDiscount > 0
            ? $@"
            <tr>
              <td style=""padding: 3px 0; color: #16a34a;"">Offer / Coupon Discount:</td>
              <td style=""padding: 3px 0; text-align: right; color: #16a34a; font-weight: 600;"">- &#8377;{totalDiscount:0.00}</td>
            </tr>"
            : string.Empty;

        // =========================================
        // TIMINGS & QR DATA
        // =========================================
        var boardingTime = ToIst(request.BoardingPointTime != default ? request.BoardingPointTime : request.DepartureTime);
        var droppingTime = ToIst(request.ArrivalPointTime != default ? request.ArrivalPointTime : request.ArrivalTime);
        var boardingTimeStr = boardingTime.ToString("ddd, dd MMM yyyy hh:mm tt");
        var droppingTimeStr = droppingTime.ToString("ddd, dd MMM yyyy hh:mm tt");
        var departureTimeStr = ToIst(request.DepartureTime).ToString("hh:mm tt");
        var cancellationDateStr = ToIst(DateTime.UtcNow).ToString("dd MMM yyyy, hh:mm tt");

        var qrPlainText = $"PICK&BOOK BUS TICKET [CANCELLED]\n" +
                          $"Booking ID: {request.BookingReference}\n" +
                          $"PNR: {request.Pnr}\n" +
                          $"Status: CANCELLED\n" +
                          $"Refund Amount: INR {refundAmount:0.00}\n" +
                          $"Support: https://picknbook.in/contact";

        var qrImgUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=95x95&data={Uri.EscapeDataString(qrPlainText)}";

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
          <div style=""margin-top: 4px;""><span style=""color: #64748b; font-size: 11px;"">Bus Partner PNR:</span> <b style=""color: #1e293b; font-size: 12px;"">{request.Pnr}</b></div>
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

    <!-- 3. JOURNEY ROUTE & TIMELINE WITH VOID STATUS QR -->
    <div style=""padding: 18px 20px; border-bottom: 1px solid #e2e8f0; background-color: #ffffff;"">
      <table style=""width: 100%; border-collapse: collapse;"">
        <tr>
          <td class=""stack-col"" style=""width: 36%; vertical-align: top;"">
            <div style=""font-size: 11px; text-transform: uppercase; color: #64748b; font-weight: 700; letter-spacing: 0.5px;"">DEPARTURE</div>
            <div style=""font-size: 17px; font-weight: 800; color: #0f172a; margin-top: 2px;"">{request.Origin}</div>
            <div style=""font-size: 12px; color: #334155; font-weight: 600; margin-top: 2px;"">{boardingTimeStr}</div>
            <div style=""margin-top: 8px; font-size: 11px; color: #64748b; line-height: 1.4;"">
              <b style=""color: #334155;"">Boarding Point:</b><br/>{request.BoardingPoint ?? "-"}
            </div>
          </td>
          <td class=""route-arrow"" style=""width: 8%; text-align: center; vertical-align: middle;"">
            <div style=""color: #94a3b8; font-size: 22px; font-weight: bold; line-height: 1;"">&#10142;</div>
          </td>
          <td class=""stack-col"" style=""width: 36%; vertical-align: top;"">
            <div style=""font-size: 11px; text-transform: uppercase; color: #64748b; font-weight: 700; letter-spacing: 0.5px;"">ARRIVAL</div>
            <div style=""font-size: 17px; font-weight: 800; color: #0f172a; margin-top: 2px;"">{request.Destination}</div>
            <div style=""font-size: 12px; color: #334155; font-weight: 600; margin-top: 2px;"">{droppingTimeStr}</div>
            <div style=""margin-top: 8px; font-size: 11px; color: #64748b; line-height: 1.4;"">
              <b style=""color: #334155;"">Dropping Point:</b><br/>{request.ArrivalPoint ?? "-"}
            </div>
          </td>
          <td class=""stack-col text-mobile-center"" style=""width: 20%; text-align: center; vertical-align: middle; padding-left: 10px; border-left: 1px dashed #e2e8f0;"">
            <img src=""{qrImgUrl}"" width=""85"" height=""85"" alt=""Cancellation QR"" style=""display: inline-block; width: 85px; height: 85px; border: 1px solid #fee2e2; border-radius: 6px; padding: 2px; background: #ffffff;"" />
            <div style=""font-size: 9px; color: #dc2626; font-weight: 800; text-transform: uppercase; margin-top: 4px; letter-spacing: 0.5px;"">VOID / CANCELLED</div>
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
              <td style=""padding: 3px 0; color: #64748b;"">Total Ticket Fare:</td>
              <td style=""padding: 3px 0; text-align: right; color: #0f172a; font-weight: 600;"">&#8377;{request.Price:0.00}</td>
            </tr>
            {discountRow}
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

        await _emailService.SendEmailWithAttachmentsAsync(
            request.ToEmail,
            subject,
            body,
            [attachment]);
    }

    public async Task SendFlightCancellationAsync(
        SendFlightTicketEmailRequest request,
        decimal refundAmount)
    {
        var subject = $"Flight Ticket Cancelled - {request.BookingReference}";

        var passengerLines = string.Empty;
        var cancelledPaxs = request.IsPartialCancellation && request.CancelledPassengers.Any() ? request.CancelledPassengers : request.Passengers;
        if (cancelledPaxs != null && cancelledPaxs.Count > 0)
        {
            passengerLines = "<p><b>Cancelled Passengers:</b><br/>" +
                string.Join("<br/>", cancelledPaxs.Select((p, i) =>
                    $"&nbsp;&nbsp;{i + 1}. {p.FullName} &mdash; Seat <b>{p.SeatNumber ?? "N/A"}</b>")) + "</p>";
        }
        else
        {
            passengerLines = $"<p><b>Passenger:</b> {request.PassengerName} &mdash; Seat <b>{request.SeatNumber ?? "N/A"}</b></p>";
        }

        var segmentLines = string.Empty;
        if (request.IsPartialCancellation && request.CancelledSegments.Any())
        {
            segmentLines = "<p><b>Cancelled Segments:</b><br/>" +
                string.Join("<br/>", request.CancelledSegments.Select((s, i) =>
                    $"&nbsp;&nbsp;{i + 1}. {s.FromCity} to {s.ToCity} ({s.Airline} {s.FlightNumber})")) + "</p>";
        }

        var cancellationText = request.IsPartialCancellation ? "partially <b style='color:red;'>cancelled</b> (see details below)" : "<b style='color:red;'>cancelled</b>";

        var body = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>Flight Booking Cancelled - {request.BookingReference}</title>
</head>
<body style=""font-family: 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f8fafc; margin: 0; padding: 25px 15px; color: #1e293b;"">
    <div style=""max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 16px rgba(0,0,0,0.06); border: 1px solid #fee2e2;"">
        <div style=""background: linear-gradient(135deg, #7f1d1d 0%, #991b1b 100%); color: #ffffff; padding: 26px 20px; text-align: center; border-radius: 12px 12px 0 0;"">
            <div style=""background: #ffffff; display: inline-block; padding: 7px 22px; border-radius: 8px; box-shadow: 0 4px 12px rgba(0,0,0,0.18); margin-bottom: 12px;"">
                <img src=""https://www.picknbook.in/assets/picknbook-login-q5fv1iRs.png"" alt=""Pick&amp;book"" style=""height: 32px; width: auto; display: block; border: 0;"" />
            </div>
            <h2 style=""margin: 0; font-size: 22px; font-weight: 700; letter-spacing: 0.5px; color: #ffffff;"">Pick&amp;book Flight Cancellation</h2>
            <p style=""margin: 6px 0 0 0; font-size: 13px; color: #fecaca;"">Booking Reference: <b style=""color: #ffffff;"">{request.BookingReference}</b></p>
        </div>
        <div style=""padding: 24px 20px;"">
            <p style=""font-size: 16px; margin: 0 0 16px 0; color: #991b1b;"">Hi <b>{request.PassengerName}</b>,</p>
            
            <div style=""background-color: #fef2f2; border-left: 4px solid #ef4444; padding: 14px 16px; border-radius: 4px; margin-bottom: 20px;"">
                <h3 style=""margin: 0 0 6px 0; color: #991b1b; font-size: 15px; font-weight: 700;"">Booking Successfully Cancelled</h3>
                <p style=""margin: 0; font-size: 13px; color: #7f1d1d; line-height: 1.5;"">
                    Your flight ticket for <b>{request.Origin} &rarr; {request.Destination}</b> has been {cancellationText}.
                </p>
            </div>

            {segmentLines}
            {passengerLines}

            <table style=""width: 100%; border-collapse: collapse; margin-bottom: 20px; background-color: #f8fafc; border-radius: 8px; border: 1px solid #e2e8f0; font-size: 13px;"">
                <tr style=""border-bottom: 1px solid #e2e8f0;"">
                    <td style=""padding: 10px 14px; font-weight: 600; color: #64748b; width: 40%;"">Original Fare</td>
                    <td style=""padding: 10px 14px; font-weight: 700; color: #0f2459;"">&#8377;{request.Price:0.00}</td>
                </tr>
                <tr>
                    <td style=""padding: 10px 14px; font-weight: 600; color: #64748b;"">Refund Amount</td>
                    <td style=""padding: 10px 14px; font-weight: 700; color: #16a34a; font-size: 15px;"">&#8377;{refundAmount:0.00}</td>
                </tr>
            </table>

            <div style=""background-color: #eff6ff; border: 1px solid #bfdbfe; border-radius: 8px; padding: 12px 14px; margin-bottom: 20px; font-size: 13px; color: #1e40af; line-height: 1.5;"">
                &#9432; The refund will be processed to your original payment method within <b>5&ndash;7 working days</b>.
            </div>

            <p style=""font-size: 12px; color: #64748b; margin: 0 0 16px 0;"">
                If you did not initiate this cancellation, please contact support immediately.
            </p>

            <p style=""margin: 0; font-size: 13px; color: #475569;"">
                Regards,<br/>
                <b>Team Pick&amp;book</b>
            </p>
        </div>
        <div style=""background-color: #f8fafc; border-top: 1px solid #e2e8f0; padding: 14px 20px; text-align: center; font-size: 11px; color: #94a3b8;"">
            &copy; 2026 Pick&amp;book Travel Services. All rights reserved.
        </div>
    </div>
</body>
</html>";

        await _emailService.SendEmailAsync(
            request.ToEmail,
            subject,
            body);
    }

    private static DateTime ToIst(DateTime dt)
    {
        if (dt.Kind == DateTimeKind.Local)
        {
            return dt;
        }
        return DateTime.SpecifyKind(dt, DateTimeKind.Utc).AddHours(5.5);
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
            "AMD" => "AHMEDABAD",
            "PNQ" => "PUNE",
            "COK" => "KOCHI",
            "GOI" => "GOA",
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
            _ => airportCode.Trim().ToUpper()
        };
    }

    public async Task SendHotelTicketAsync(HotelReservation reservation)
    {
        var pdfBytes = _ticketPdfService.GenerateHotelTicketPdf(reservation);

        var attachment = new EmailAttachment
        {
            FileName = $"hotel-ticket-{reservation.BookingReference}.pdf",
            ContentType = "application/pdf",
            Content = pdfBytes
        };

        var subject = $"Your Hotel Booking Confirmation - {reservation.BookingReference} - {reservation.HotelName}";

        var body = $@"
<div style=""font-family: Arial, sans-serif; background-color: #f4f6fa; padding: 30px 20px; max-width: 600px; margin: 0 auto; border-radius: 12px;"">
    <div style=""background: linear-gradient(135deg, #0f2459 0%, #1e3a8a 100%); color: #ffffff; padding: 26px 20px; text-align: center; border-radius: 12px; margin-bottom: 25px; box-shadow: 0 4px 16px rgba(0,0,0,0.08);"">
        <div style=""background: #ffffff; display: inline-block; padding: 7px 22px; border-radius: 8px; box-shadow: 0 4px 12px rgba(0,0,0,0.18); margin-bottom: 12px;"">
            <img src=""https://www.picknbook.in/assets/picknbook-login-q5fv1iRs.png"" alt=""Pick&amp;book"" style=""height: 32px; width: auto; display: block; border: 0;"" />
        </div>
        <h2 style=""margin: 0; font-size: 22px; font-weight: 700; letter-spacing: 0.5px; color: #ffffff;"">Hotel Booking Confirmed!</h2>
        <p style=""margin: 6px 0 0 0; font-size: 13px; color: #94a3b8;"">Booking Reference: <b style=""color: #f8fafc;"">{reservation.BookingReference}</b></p>
    </div>

    <div style=""background-color: #ffffff; border: 1px solid #e2e8f0; border-radius: 16px; padding: 25px; box-shadow: 0 4px 20px rgba(15, 36, 89, 0.08);"">
        <table style=""width: 100%; border-collapse: collapse; margin-bottom: 20px;"">
            <tr>
                <td style=""vertical-align: middle;"">
                    <div style=""display: inline-block; background-color: #0f2459; color: #ffffff; font-size: 14px; font-weight: bold; width: 24px; height: 24px; line-height: 24px; text-align: center; border-radius: 4px; margin-right: 8px; font-family: sans-serif;"">H</div>
                    <span style=""font-size: 16px; font-weight: bold; color: #0f2459; vertical-align: middle;"">{reservation.HotelName} Ticket</span>
                    <div style=""font-size: 11px; color: #78829b; margin-top: 4px;"">Reference: {reservation.BookingReference}</div>
                </td>
                <td style=""text-align: right; vertical-align: top;"">
                    <span style=""background-color: #f8fafc; border: 1px solid #e2e8f0; color: #0f2459; font-size: 10px; font-weight: bold; padding: 4px 12px; border-radius: 5px; text-transform: uppercase;"">HOTEL</span>
                </td>
            </tr>
        </table>

        <table style=""width: 100%; border-collapse: collapse; margin-bottom: 20px;"">
            <tr>
                <td style=""width: 32%; background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 10px; vertical-align: top;"">
                    <div style=""font-size: 8px; font-weight: bold; color: #78829b; margin-bottom: 4px; text-transform: uppercase;"">STAY</div>
                    <div style=""font-size: 11px; font-weight: bold; color: #0f2459;"">{reservation.HotelName} to {reservation.CityCode}</div>
                    <div style=""font-size: 9px; color: #78829b; margin-top: 4px;"">{GetRoomCategory(reservation.OfferId)}</div>
                </td>
                <td style=""width: 2%;""></td>
                <td style=""width: 32%; background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 10px; vertical-align: top;"">
                    <div style=""font-size: 8px; font-weight: bold; color: #78829b; margin-bottom: 4px; text-transform: uppercase;"">CHECK IN</div>
                    <div style=""font-size: 11px; font-weight: bold; color: #0f2459;"">{reservation.CheckInDate:dd MMM yyyy}</div>
                    <div style=""font-size: 9px; color: #78829b; margin-top: 4px;"">Check Out: {reservation.CheckOutDate:dd MMM yyyy}</div>
                </td>
                <td style=""width: 2%;""></td>
                <td style=""width: 32%; background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 10px; vertical-align: top;"">
                    <div style=""font-size: 8px; font-weight: bold; color: #78829b; margin-bottom: 4px; text-transform: uppercase;"">STATUS</div>
                    <div style=""font-size: 11px; font-weight: bold; color: #0f2459;"">{reservation.Status}</div>
                    <div style=""font-size: 9px; color: #78829b; margin-top: 4px;"">Booked at {ToIst(reservation.CreatedAt):dd MMM yyyy, hh:mm tt}</div>
                </td>
            </tr>
        </table>

        <div style=""margin-bottom: 20px;"">
            <div style=""font-size: 10px; font-weight: bold; color: #0f2459; margin-bottom: 6px;"">Passengers</div>
            <table style=""width: 100%; border-collapse: collapse; border: 1px solid #e2e8f0; border-radius: 8px; background-color: #f8fafc;"">
                <tr>
                    <td style=""padding: 10px; font-size: 11px; color: #0f2459;"">{reservation.GuestName} - Primary Guest</td>
                    <td style=""padding: 10px; font-size: 11px; font-weight: bold; color: #0f2459; text-align: right;"">Seat King Bed</td>
                </tr>
            </table>
        </div>

        <div style=""margin-bottom: 20px;"">
            <div style=""font-size: 10px; font-weight: bold; color: #0f2459; margin-bottom: 6px;"">Contact and Delivery</div>
            <table style=""width: 100%; border-collapse: collapse; border: 1px solid #e2e8f0; border-radius: 8px; background-color: #ffffff;"">
                <tr style=""border-bottom: 1px solid #e2e8f0;"">
                    <td style=""padding: 8px 12px; font-size: 11px; color: #78829b;"">Seats</td>
                    <td style=""padding: 8px 12px; font-size: 11px; font-weight: bold; color: #0f2459; text-align: right;"">{GetRoomCategory(reservation.OfferId)}</td>
                </tr>
                <tr style=""border-bottom: 1px solid #e2e8f0;"">
                    <td style=""padding: 8px 12px; font-size: 11px; color: #78829b;"">Email</td>
                    <td style=""padding: 8px 12px; font-size: 11px; font-weight: bold; color: #0f2459; text-align: right;"">{reservation.GuestEmail}</td>
                </tr>
                <tr style=""border-bottom: 1px solid #e2e8f0;"">
                    <td style=""padding: 8px 12px; font-size: 11px; color: #78829b;"">Mobile</td>
                    <td style=""padding: 8px 12px; font-size: 11px; font-weight: bold; color: #0f2459; text-align: right;"">{reservation.GuestPhone}</td>
                </tr>
                <tr style=""border-bottom: 1px solid #e2e8f0;"">
                    <td style=""padding: 8px 12px; font-size: 11px; color: #78829b;"">WhatsApp</td>
                    <td style=""padding: 8px 12px; font-size: 11px; font-weight: bold; color: #0f2459; text-align: right;"">Not selected</td>
                </tr>
                <tr>
                    <td style=""padding: 8px 12px; font-size: 11px; color: #78829b;"">Payment Method</td>
                    <td style=""padding: 8px 12px; font-size: 11px; font-weight: bold; color: #0f2459; text-align: right;"">Wallet</td>
                </tr>
            </table>
        </div>

        <div style=""margin-bottom: 20px;"">
            <div style=""font-size: 10px; font-weight: bold; color: #0f2459; margin-bottom: 6px;"">Confirmation Delivery Status</div>
            <table style=""width: 100%; border-collapse: collapse; border: 1px solid #e2e8f0; border-radius: 8px; background-color: #ffffff;"">
                <tr style=""border-bottom: 1px solid #e2e8f0;"">
                    <td style=""padding: 8px 12px; font-size: 11px; color: #78829b;"">Email Confirmation</td>
                    <td style=""padding: 8px 12px; font-size: 11px; font-weight: bold; color: #0f2459; text-align: right;"">Queued</td>
                </tr>
                <tr style=""border-bottom: 1px solid #e2e8f0;"">
                    <td style=""padding: 8px 12px; font-size: 11px; color: #78829b;"">SMS Confirmation</td>
                    <td style=""padding: 8px 12px; font-size: 11px; font-weight: bold; color: #0f2459; text-align: right;"">Queued</td>
                </tr>
                <tr>
                    <td style=""padding: 8px 12px; font-size: 11px; color: #78829b;"">WhatsApp Confirmation</td>
                    <td style=""padding: 8px 12px; font-size: 11px; font-weight: bold; color: #0f2459; text-align: right;"">Skipped</td>
                </tr>
            </table>
        </div>

        <div style=""border-top: 1px solid #e2e8f0; padding-top: 15px;"">
            <table style=""width: 100%; border-collapse: collapse;"">
                <tr>
                    <td style=""font-size: 11px; color: #78829b; padding-bottom: 6px;"">Base Fare</td>
                    <td style=""font-size: 11px; color: #0f2459; padding-bottom: 6px; text-align: right;"">INR {reservation.BasePrice:N2}</td>
                </tr>

                <tr>
                    <td style=""font-size: 11px; color: #78829b; padding-bottom: 6px;"">Convenience Fee</td>
                    <td style=""font-size: 11px; color: #0f2459; padding-bottom: 6px; text-align: right;"">INR {reservation.ConvenienceFee:N2}</td>
                </tr>
                <tr>
                    <td style=""font-size: 11px; color: #78829b; padding-bottom: 12px;"">Discount</td>
                    <td style=""font-size: 11px; color: #0f2459; padding-bottom: 12px; text-align: right;"">INR {reservation.CouponDiscount:N2}</td>
                </tr>
                <tr style=""border-top: 1px solid #e2e8f0;"">
                    <td style=""font-size: 13px; font-weight: bold; color: #0f2459; padding-top: 10px;"">Total Paid</td>
                    <td style=""font-size: 13px; font-weight: bold; color: #0f2459; padding-top: 10px; text-align: right;"">INR {reservation.TotalPrice:N2}</td>
                </tr>
            </table>
        </div>
    </div>
</div>";

        await _emailService.SendEmailWithAttachmentsAsync(
            reservation.GuestEmail,
            subject,
            body,
            [attachment]);
    }

    public async Task SendHotelCancellationAsync(HotelReservation reservation)
    {
        var pdfBytes = _ticketPdfService.GenerateHotelTicketPdf(reservation);

        var attachment = new EmailAttachment
        {
            FileName = $"hotel-cancelled-{reservation.BookingReference}.pdf",
            ContentType = "application/pdf",
            Content = pdfBytes
        };

        var subject = $"Hotel Booking Cancelled - {reservation.BookingReference} - {reservation.HotelName}";

        var body = $@"
<div style=""font-family: Arial, sans-serif; background-color: #f4f6fa; padding: 30px 20px; max-width: 600px; margin: 0 auto; border-radius: 12px;"">
    <div style=""background: linear-gradient(135deg, #7f1d1d 0%, #991b1b 100%); color: #ffffff; padding: 26px 20px; text-align: center; border-radius: 12px; margin-bottom: 25px; box-shadow: 0 4px 16px rgba(0,0,0,0.08);"">
        <div style=""background: #ffffff; display: inline-block; padding: 7px 22px; border-radius: 8px; box-shadow: 0 4px 12px rgba(0,0,0,0.18); margin-bottom: 12px;"">
            <img src=""https://www.picknbook.in/assets/picknbook-login-q5fv1iRs.png"" alt=""Pick&amp;book"" style=""height: 32px; width: auto; display: block; border: 0;"" />
        </div>
        <h2 style=""margin: 0; font-size: 22px; font-weight: 700; letter-spacing: 0.5px; color: #ffffff;"">Hotel Booking Cancelled</h2>
        <p style=""margin: 6px 0 0 0; font-size: 13px; color: #fecaca;"">Booking Reference: <b style=""color: #ffffff;"">{reservation.BookingReference}</b></p>
    </div>

    <div style=""background-color: #ffffff; border: 1px solid #e2e8f0; border-radius: 16px; padding: 25px; box-shadow: 0 4px 20px rgba(15, 36, 89, 0.08);"">
        <table style=""width: 100%; border-collapse: collapse; margin-bottom: 20px;"">
            <tr>
                <td style=""vertical-align: middle;"">
                    <div style=""display: inline-block; background-color: #d9251c; color: #ffffff; font-size: 14px; font-weight: bold; width: 24px; height: 24px; line-height: 24px; text-align: center; border-radius: 4px; margin-right: 8px; font-family: sans-serif;"">H</div>
                    <span style=""font-size: 16px; font-weight: bold; color: #d9251c; vertical-align: middle;"">{reservation.HotelName} Ticket</span>
                    <div style=""font-size: 11px; color: #78829b; margin-top: 4px;"">Reference: {reservation.BookingReference}</div>
                </td>
                <td style=""text-align: right; vertical-align: top;"">
                    <span style=""background-color: #f8fafc; border: 1px solid #e2e8f0; color: #d9251c; font-size: 10px; font-weight: bold; padding: 4px 12px; border-radius: 5px; text-transform: uppercase;"">CANCELLED</span>
                </td>
            </tr>
        </table>

        <table style=""width: 100%; border-collapse: collapse; margin-bottom: 20px;"">
            <tr>
                <td style=""width: 32%; background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 10px; vertical-align: top;"">
                    <div style=""font-size: 8px; font-weight: bold; color: #78829b; margin-bottom: 4px; text-transform: uppercase;"">STAY</div>
                    <div style=""font-size: 11px; font-weight: bold; color: #0f2459;"">{reservation.HotelName} to {reservation.CityCode}</div>
                    <div style=""font-size: 9px; color: #78829b; margin-top: 4px;"">{GetRoomCategory(reservation.OfferId)}</div>
                </td>
                <td style=""width: 2%;""></td>
                <td style=""width: 32%; background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 10px; vertical-align: top;"">
                    <div style=""font-size: 8px; font-weight: bold; color: #78829b; margin-bottom: 4px; text-transform: uppercase;"">CHECK IN</div>
                    <div style=""font-size: 11px; font-weight: bold; color: #0f2459;"">{reservation.CheckInDate:dd MMM yyyy}</div>
                    <div style=""font-size: 9px; color: #78829b; margin-top: 4px;"">Check Out: {reservation.CheckOutDate:dd MMM yyyy}</div>
                </td>
                <td style=""width: 2%;""></td>
                <td style=""width: 32%; background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 10px; vertical-align: top;"">
                    <div style=""font-size: 8px; font-weight: bold; color: #78829b; margin-bottom: 4px; text-transform: uppercase;"">STATUS</div>
                    <div style=""font-size: 11px; font-weight: bold; color: #d9251c;"">{reservation.Status}</div>
                    <div style=""font-size: 9px; color: #78829b; margin-top: 4px;"">Booked at {ToIst(reservation.CreatedAt):dd MMM yyyy, hh:mm tt}</div>
                </td>
            </tr>
        </table>

        <div style=""margin-bottom: 20px;"">
            <div style=""font-size: 10px; font-weight: bold; color: #0f2459; margin-bottom: 6px;"">Passengers</div>
            <table style=""width: 100%; border-collapse: collapse; border: 1px solid #e2e8f0; border-radius: 8px; background-color: #f8fafc;"">
                <tr>
                    <td style=""padding: 10px; font-size: 11px; color: #0f2459;"">{reservation.GuestName} - Primary Guest</td>
                    <td style=""padding: 10px; font-size: 11px; font-weight: bold; color: #0f2459; text-align: right;"">Seat King Bed</td>
                </tr>
            </table>
        </div>

        <div style=""margin-bottom: 20px;"">
            <div style=""font-size: 10px; font-weight: bold; color: #0f2459; margin-bottom: 6px;"">Contact and Delivery</div>
            <table style=""width: 100%; border-collapse: collapse; border: 1px solid #e2e8f0; border-radius: 8px; background-color: #ffffff;"">
                <tr style=""border-bottom: 1px solid #e2e8f0;"">
                    <td style=""padding: 8px 12px; font-size: 11px; color: #78829b;"">Seats</td>
                    <td style=""padding: 8px 12px; font-size: 11px; font-weight: bold; color: #0f2459; text-align: right;"">{GetRoomCategory(reservation.OfferId)}</td>
                </tr>
                <tr style=""border-bottom: 1px solid #e2e8f0;"">
                    <td style=""padding: 8px 12px; font-size: 11px; color: #78829b;"">Email</td>
                    <td style=""padding: 8px 12px; font-size: 11px; font-weight: bold; color: #0f2459; text-align: right;"">{reservation.GuestEmail}</td>
                </tr>
                <tr style=""border-bottom: 1px solid #e2e8f0;"">
                    <td style=""padding: 8px 12px; font-size: 11px; color: #78829b;"">Mobile</td>
                    <td style=""padding: 8px 12px; font-size: 11px; font-weight: bold; color: #0f2459; text-align: right;"">{reservation.GuestPhone}</td>
                </tr>
                <tr style=""border-bottom: 1px solid #e2e8f0;"">
                    <td style=""padding: 8px 12px; font-size: 11px; color: #78829b;"">WhatsApp</td>
                    <td style=""padding: 8px 12px; font-size: 11px; font-weight: bold; color: #0f2459; text-align: right;"">Not selected</td>
                </tr>
                <tr>
                    <td style=""padding: 8px 12px; font-size: 11px; color: #78829b;"">Payment Method</td>
                    <td style=""padding: 8px 12px; font-size: 11px; font-weight: bold; color: #0f2459; text-align: right;"">Wallet</td>
                </tr>
            </table>
        </div>

        <div style=""border-top: 1px solid #e2e8f0; padding-top: 15px;"">
            <table style=""width: 100%; border-collapse: collapse;"">
                <tr>
                    <td style=""font-size: 11px; color: #78829b; padding-bottom: 6px;"">Original Total Paid</td>
                    <td style=""font-size: 11px; color: #0f2459; padding-bottom: 6px; text-align: right;"">INR {reservation.TotalPrice:N2}</td>
                </tr>
                <tr>
                    <td style=""font-size: 11px; color: #78829b; padding-bottom: 6px;"">Cancellation Charges</td>
                    <td style=""font-size: 11px; color: #0f2459; padding-bottom: 6px; text-align: right;"">INR {reservation.CancellationCharges:N2}</td>
                </tr>
                <tr>
                    <td style=""font-size: 11px; color: #78829b; padding-bottom: 12px;"">Refund Amount</td>
                    <td style=""font-size: 11px; color: #0f2459; padding-bottom: 12px; text-align: right;"">INR {reservation.RefundAmount:N2}</td>
                </tr>
                <tr style=""border-top: 1px solid #e2e8f0;"">
                    <td style=""font-size: 13px; font-weight: bold; color: #d9251c; padding-top: 10px;"">Total Refunded</td>
                    <td style=""font-size: 13px; font-weight: bold; color: #d9251c; padding-top: 10px; text-align: right;"">INR {reservation.RefundAmount:N2}</td>
                </tr>
            </table>
        </div>
    </div>
</div>";

        await _emailService.SendEmailWithAttachmentsAsync(
            reservation.GuestEmail,
            subject,
            body,
            [attachment]);
    }

    private static string GetRoomCategory(string offerId)
    {
        if (string.IsNullOrWhiteSpace(offerId)) return "Standard Room";
        if (offerId.Contains("suite", StringComparison.OrdinalIgnoreCase)) return "Executive Suite";
        if (offerId.Contains("deluxe", StringComparison.OrdinalIgnoreCase)) return "Deluxe Room";
        return "Standard Room";
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



