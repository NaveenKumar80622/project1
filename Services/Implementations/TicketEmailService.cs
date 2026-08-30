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
            $"Your PickNBook Ticket - {request.BookingReference}";

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
    
    <div style=""text-align: center; margin-bottom: 25px;"">
        <h2 style=""color: #0f2459; margin: 0 0 5px 0; font-size: 24px;"">Your Boarding Pass is Ready!</h2>
        <p style=""color: #0f2459; margin: 0 0 10px 0; font-size: 16px; font-weight: bold;"">Congratulations on your booking! Your flight reservation is confirmed.</p>
        <p style=""color: #5a6578; margin: 0; font-size: 14px;"">Please find your flight booking confirmation and boarding passes attached below.</p>
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
        &copy; 2026 PickNBook Travel Services. All rights reserved.
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
        // PASSENGER LIST
        // =========================================

        var passengerLines =
            request.Passengers.Count > 0
            ? string.Join(
                "<br/>",
                request.Passengers.Select((p, i) =>
                    $"&nbsp;&nbsp;{i + 1}. {p.FullName} â€” Seat <b>{p.SeatNumber}</b>"))
            : $"Seats: {request.SeatNumber}";

        // =========================================
        // DISCOUNT BREAKDOWN
        // =========================================

        var discountSection = string.Empty;

        if (request.AutoDiscountAmount > 0)
        {
            discountSection += $@"
            <p>
                <b>Offer Discount:</b>
                - â‚¹{request.AutoDiscountAmount:0.00}
            </p>";
        }

        if (request.CouponDiscountAmount > 0)
        {
            discountSection += $@"
            <p>
                <b>Coupon Discount:</b>
                - â‚¹{request.CouponDiscountAmount:0.00}
            </p>";
        }

        // BACKWARD COMPATIBILITY
        if (string.IsNullOrWhiteSpace(discountSection) &&
            request.DiscountAmount.GetValueOrDefault() > 0)
        {
            discountSection = $@"
            <p>
                <b>Discount:</b>
                - â‚¹{request.DiscountAmount:0.00}
            </p>";
        }

        // =========================================
        // EMAIL BODY
        // =========================================

        var gstSection = request.GstAmount > 0 
            ? $@"
        <p>
            <b>GST:</b>
            â‚¹{request.GstAmount:0.00}
        </p>"
            : string.Empty;

        var body = $@"
        <p>Hi {request.PassengerName},</p>

        <p>
            Your bus booking is confirmed for
            <b>{request.Origin} â†’ {request.Destination}</b>.
        </p>

        <p>
            <b>PNR / Ticket No:</b>
            {request.Pnr}
        </p>

        <p>
            <b>Boarding:</b>
            {request.BoardingPoint} at {ToIst(request.BoardingPointTime):ddd, dd MMM yyyy HH:mm}
        </p>
        
        <p>
            <b>Dropping:</b>
            {request.ArrivalPoint} at {ToIst(request.ArrivalPointTime):ddd, dd MMM yyyy HH:mm}
        </p>

        <p>
            <b>Passengers:</b><br/>
            {passengerLines}
        </p>



        {discountSection}
        {gstSection}

        <p>
            <b>Total Fare:</b>
            â‚¹{request.Price:0.00}
        </p>

        <p>
            Please find your ticket PDF attached.
            Have a safe journey!
        </p>

        <p>Team PickNBook</p>";

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
        // PASSENGER LIST
        // =========================================

        var passengerLines =
            request.Passengers.Count > 0
            ? string.Join(
                "<br/>",
                request.Passengers.Select((p, i) =>
                    $"&nbsp;&nbsp;{i + 1}. {p.FullName} â€” Seat <b>{p.SeatNumber}</b>"))
            : $"Seats: {request.SeatNumber}";

        // =========================================
        // DISCOUNT BREAKDOWN
        // =========================================

        var discountSection = string.Empty;

        if (request.AutoDiscountAmount > 0)
        {
            discountSection += $@"
            <p>
                <b>Offer Discount:</b>
                - â‚¹{request.AutoDiscountAmount:0.00}
            </p>";
        }

        if (request.CouponDiscountAmount > 0)
        {
            discountSection += $@"
            <p>
                <b>Coupon Discount:</b>
                - â‚¹{request.CouponDiscountAmount:0.00}
            </p>";
        }

        // BACKWARD COMPATIBILITY
        if (string.IsNullOrWhiteSpace(discountSection) &&
            request.DiscountAmount.GetValueOrDefault() > 0)
        {
            discountSection = $@"
            <p>
                <b>Discount:</b>
                - â‚¹{request.DiscountAmount:0.00}
            </p>";
        }

        // =========================================
        // EMAIL BODY
        // =========================================

        var body = $@"
        <p>Hi {request.PassengerName},</p>

        <p>
            Your bus ticket for
            <b>{request.Origin} â†’ {request.Destination}</b>
            has been
            <b style='color:red;'>cancelled</b>.
        </p>

        <p>
            <b>PNR / Ticket No:</b>
            {request.Pnr}
        </p>

        <p>
            <b>Passengers:</b><br/>
            {passengerLines}
        </p>

        <p>
            <b>Original Fare:</b>
            â‚¹{request.NetFare:0.00}
        </p>

        {discountSection}

        <p>
            <b>GST:</b>
            â‚¹{request.GstAmount:0.00}
        </p>



        <p>
            <b>Total Fare:</b>
            â‚¹{request.Price:0.00}
        </p>

        <p>
            <b>Refund Amount:</b>
            â‚¹{refundAmount:0.00}
        </p>

        <p>
            The refund will be processed to your
            original payment method within
            5â€“7 working days.
        </p>

        <p>
            If you did not initiate this cancellation,
            please contact support immediately.
        </p>

        <p>
            Regards,<br/>
            Team PickNBook
        </p>";

        // =========================================
        // PDF
        // =========================================

        var pdfBytes =
            _ticketPdfService.GenerateBusTicketPdf(request);

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
                    $"&nbsp;&nbsp;{i + 1}. {p.FullName} â€” Seat <b>{p.SeatNumber ?? "N/A"}</b>")) + "</p>";
        }
        else
        {
            passengerLines = $"<p><b>Passenger:</b> {request.PassengerName} â€” Seat <b>{request.SeatNumber ?? "N/A"}</b></p>";
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
        <p>Hi {request.PassengerName},</p>

        <p>
            Your flight ticket for
            <b>{request.Origin} to {request.Destination}</b>
            has been {cancellationText}.
        </p>

        <p>
            <b>Booking Reference:</b>
            {request.BookingReference}
        </p>

        {segmentLines}
        {passengerLines}

        <p>
            <b>Original Fare:</b>
            â‚¹{request.Price:0.00}
        </p>

        <p>
            <b>Refund Amount:</b>
            â‚¹{refundAmount:0.00}
        </p>

        <p>
            The refund will be processed to your
            original payment method within
            5â€“7 working days.
        </p>

        <p>
            If you did not initiate this cancellation,
            please contact support immediately.
        </p>

        <p>
            Regards,<br/>
            Team PickNBook
        </p>";

        await _emailService.SendEmailAsync(
            request.ToEmail,
            subject,
            body);
    }

    private static DateTime ToIst(DateTime utc)
    {
        return DateTime.SpecifyKind(
            utc,
            DateTimeKind.Utc
        ).AddHours(5.5);
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
    <div style=""text-align: center; margin-bottom: 25px;"">
        <h2 style=""color: #0f2459; margin: 0 0 5px 0;"">Hotel Booking Confirmed!</h2>
        <p style=""color: #5a6578; margin: 0; font-size: 14px;"">Your hotel is booked. Please find your ticket and details below.</p>
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
    <div style=""text-align: center; margin-bottom: 25px;"">
        <h2 style=""color: #d9251c; margin: 0 0 5px 0;"">Hotel Booking Cancelled</h2>
        <p style=""color: #5a6578; margin: 0; font-size: 14px;"">Your hotel booking has been cancelled. Please find details below.</p>
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



