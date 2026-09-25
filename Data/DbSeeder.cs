using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Models;
using PickNBook.Api.Services.SeatLayouts;

namespace PickNBook.Api.Data;

public static class DbSeeder
{
    private static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);

    private static readonly string[] AllowedTravelClasses =
    [
        "Economy",
        "Premium Economy",
        "Business",
        "Premium Business",
        "First Class"
    ];

    private static readonly Dictionary<string, int> ClassSeatConfig = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Economy"] = 120,
        ["Premium Economy"] = 24,
        ["Business"] = 18,
        ["Premium Business"] = 12,
        ["First Class"] = 8
    };

    private static readonly Dictionary<string, decimal> ClassPriceMultiplier = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Economy"] = 1.00m,
        ["Premium Economy"] = 1.35m,
        ["Business"] = 2.00m,
        ["Premium Business"] = 2.40m,
        ["First Class"] = 3.20m
    };

    public static async Task SeedAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
    {
        //await EnsureTablesAsync(dbContext, cancellationToken);

        var hasCmsPages = await dbContext.CmsPages.AnyAsync(cancellationToken);
        if (!hasCmsPages)
        {
            dbContext.CmsPages.AddRange(
                new CmsPage
                {
                    Title = "Terms & Conditions",
                    Slug = "terms-conditions",
                    Module = "All",
                    Status = "Active",
                    MetaTitle = "Terms & Conditions | Pick N Book",
                    MetaKeyword = "Pick N Book terms, booking terms, travel terms",
                    MetaDescription = "Read the Pick N Book Terms & Conditions for using our travel booking services.",
                    Description = """
Welcome to Pick N Book! By accessing and using our website, services, and mobile app (collectively referred to as "Services"), you agree to comply with and be bound by the following terms and conditions ("Terms"). Please read them carefully before using our Services.

1. Acceptance of Terms
By accessing or using the Pick N Book website or services, you agree to be bound by these Terms and our Privacy Policy, which is incorporated by reference. If you do not agree to these Terms, you should not use our website or services.

2. Modifications to Terms
We reserve the right to update, modify, or revise these Terms at any time. When we do, we will update the effective date at the top of this page. Your continued use of the website or Services after such changes constitutes your acceptance of the revised Terms.

3. Use of the Services
You agree to use our Services solely for lawful purposes and in accordance with these Terms. You shall not:
- Engage in any activity that disrupts, damages, or interferes with the functioning of the website or Services.
- Use the Services to transmit any malicious content, including but not limited to viruses, malware, or spyware.
- Use the website to engage in fraudulent activities or illegal actions.
- Violate any applicable local, state, national, or international law.

4. Account Registration
To access certain features of our Services, you may need to create an account. You agree to provide accurate, current, and complete information when registering, and to update your information as necessary. You are solely responsible for the confidentiality of your account information, including your username and password. You agree to notify us immediately if you suspect unauthorized access to your account.

5. Booking and Payment
When booking flights, buses, or other services via Pick N Book, you agree to:
- Provide accurate and truthful information during the booking process.
- Pay the applicable fees, including taxes, service charges, and other costs associated with your booking.
- Acknowledge that all transactions made on the website are subject to availability, and that your booking is not confirmed until payment is processed and confirmed by us.
- Abide by the terms and conditions of airlines, bus operators, and other service providers, including cancellation, modification, and refund policies.

6. Cancellation and Refunds
Refunds, cancellations, or modifications to bookings are subject to the specific policies of the service provider (e.g., airlines, bus operators). For any cancellations or modifications, you must contact us directly through our customer service channels. Refunds are processed in 7-14 business days and are credited back to the original payment method or can be provided as a wallet credit.

7. Privacy Policy
Your use of our website and Services is also governed by our Privacy Policy, which describes how we collect, use, and protect your personal information. By using our Services, you consent to the practices described in the Privacy Policy.

8. Intellectual Property
All content available on the Pick N Book website, including but not limited to text, graphics, logos, images, software, and trademarks, is the property of Pick N Book or its licensors and is protected by copyright and intellectual property laws. You may not reproduce, modify, distribute, or otherwise use the content without our prior written consent.

9. Third-Party Links
Our website may contain links to third-party websites that are not operated or controlled by Pick N Book. We are not responsible for the content, privacy policies, or practices of third-party websites. We encourage you to review the terms and privacy policies of any third-party websites before using them.

10. Disclaimers
Pick N Book provides the website and Services "as is" and makes no representations or warranties of any kind, express or implied, regarding the accuracy, completeness, reliability, or availability of the Services.
- We do not guarantee that the Services will be free from errors, viruses, or other harmful components.
- We do not assume liability for any issues arising from the use of our Services, including but not limited to booking errors, cancellations, delays, or other travel-related issues.

11. Limitation of Liability
To the maximum extent permitted by law, Pick N Book and its affiliates, officers, employees, or agents shall not be liable for any indirect, incidental, special, punitive, or consequential damages arising out of or in connection with your use of the website or Services, even if we have been advised of the possibility of such damages.

12. Indemnification
You agree to indemnify, defend, and hold harmless Pick N Book and its affiliates, officers, employees, and agents from any claims, losses, damages, liabilities, and expenses (including legal fees) arising out of your use of the website or Services, your violation of these Terms, or your violation of any rights of another.

13. Governing Law and Dispute Resolution
These Terms shall be governed by and construed in accordance with the laws of India. Any dispute arising out of or in connection with these Terms shall be subject to the exclusive jurisdiction of the courts located in India.

14. Termination
We reserve the right to suspend or terminate your access to the Services at any time, without notice, for any reason, including if you violate these Terms. Upon termination, all rights and obligations under these Terms will cease, except for provisions that by their nature should survive termination.

15. Severability
If any provision of these Terms is found to be invalid, illegal, or unenforceable by a court of competent jurisdiction, the remaining provisions will continue in full force and effect.
""",
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                },
                new CmsPage
                {
                    Title = "Privacy Policy",
                    Slug = "privacy-policy",
                    Module = "All",
                    Status = "Active",
                    MetaTitle = "Privacy Policy | Pick N Book",
                    MetaKeyword = "Pick N Book privacy, travel booking privacy, data protection",
                    MetaDescription = "Read how Pick N Book collects, uses, and safeguards your personal information.",
                    Description = """
At Pick N Book, we are committed to protecting your privacy. This Privacy Policy explains how we collect, use, and safeguard your personal information when you visit our website, use our services, or make a booking for flights and bus services. By using our website, you agree to the terms and conditions outlined in this Privacy Policy.

1. Information We Collect
We collect various types of information to provide and improve our services to you, including:
- Personal Information: When you book flights or bus services or interact with our website, we may collect personal information such as: Full name, Email address, Phone number, Payment details, Passport details, and Travel preferences.
- Usage Data: We may collect information about how you access and use our website, including: IP address, Browser type, Pages visited, Time spent, and Referring URLs.
- Cookies: Our website uses cookies to enhance your browsing experience.

2. How We Use Your Information
We use the collected information to:
- Process and confirm bookings.
- Communicate with you.
- Improve our website and services.
- Prevent fraud.
- Comply with legal obligations.

3. Sharing Your Information
We may share your information with:
- Third-party service providers.
- Public authorities if required by law.
- Entities involved in a business transfer.

4. Data Security
We use industry-standard encryption techniques to safeguard sensitive data during transmission, though no method is 100% secure.

5. Retention of Information
We retain personal info as long as necessary to fulfill target purposes.

6. Your Rights and Choices
You have the right to access, correct, delete, or opt-out of marketing communications.

7. Third-Party Links
We are not responsible for privacy practices of linked third-party websites.

8. Changes to This Privacy Policy
We may update this Privacy Policy from time to time.
""",
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                },
                new CmsPage
                {
                    Title = "Refund & Cancellation Policy",
                    Slug = "refund-cancellation-policy",
                    Module = "All",
                    Status = "Active",
                    MetaTitle = "Refund & Cancellation Policy | Pick N Book",
                    MetaKeyword = "PickNBook refunds, cancellation policy, flight refund, bus refund",
                    MetaDescription = "Read the PickNBook refund and cancellation terms for flight and bus bookings.",
                    Description = """
1. Introduction
Welcome to PickNBook. This Refund and Cancellation Policy outlines the terms and conditions applicable to all flight and bus bookings made through our platform.

2. Flight Bookings
- Cancellation by Customer: Governed by respective airline's rules. Service fees are non-refundable.
- Cancellation by Airline: Passengers entitled to refund/rebooking per airline policy.
- Refund Timelines: Processed within 7-15 business days after airline confirmation.

3. Bus Bookings
- Cancellation by Customer: Subject to operator's timing restrictions and charges.
- Cancellation by Operator: Passengers receive full refund or rescheduled journey.
- Refund Timelines: Generally processed within 5-10 business days.

4. No-Show Policy
Failure to board or check in at the scheduled time results in no refund.

5. Modification and Rebooking
Subject to airline/operator modification policies and extra charges.

6. Refund Mode
Refunds are issued to the original payment method.
""",
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                }
            );
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var hasConvenienceFee = await dbContext.BusConvenienceFees.AnyAsync(cancellationToken);
        if (!hasConvenienceFee)
        {
            dbContext.BusConvenienceFees.Add(new BusConvenienceFee
            {
                FeeInr = 0m,
                EntryDateUtc = DateTime.UtcNow,
                UpdateDateUtc = DateTime.UtcNow,
                UpdatedBy = "system",
                Status = "Active"
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var hasFlightConvenienceFee = await dbContext.FlightConvenienceFees.AnyAsync(cancellationToken);
        if (!hasFlightConvenienceFee)
        {
            dbContext.FlightConvenienceFees.Add(new FlightConvenienceFee
            {
                AmountType = "Fixed",
                Value = 0m,
                EntryDateUtc = DateTime.UtcNow,
                UpdateDateUtc = DateTime.UtcNow,
                UpdatedBy = "system",
                Status = "Active"
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var hasBuses = await dbContext.BusBookings.AnyAsync(x => x.BusNumber == "PNB-B1001", cancellationToken);
        if (!hasBuses)
        {
            var buses = BuildBusSeed();
            await dbContext.BusBookings.AddRangeAsync(buses, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            Console.WriteLine($"Seeded {buses.Count} rows into bus_bookings.");
        }

        var hasNewBuses = await dbContext.BusBookings.AnyAsync(x => x.BusNumber == "PNB-B2002", cancellationToken);
        if (!hasNewBuses)
        {
            var now = DateTime.UtcNow;
            var newBuses = new List<BusBooking>
            {
                new BusBooking
                {
                    BusNumber = "PNB-B2002",
                    OperatorName = "Kaveri Travels",
                    BusType = "AC Sleeper",
                    FromCity = "Hyderabad",
                    ToCity = "Vijayawada",
                    DepartureTime = now.AddDays(1),
                    ArrivalTime = now.AddDays(1).AddHours(4.8), // 4.8 hours = 288 minutes
                    PriceInr = 1100.00m,
                    AvailableSeats = 36,
                    TotalSeats = 36,
                    BoardingPoint = "MGBS",
                    DroppingPoint = "Benz Circle",
                    GstCategory = "AC",
                },
                new BusBooking
                {
                    BusNumber = "PNB-B2003",
                    OperatorName = "Morning Star Travels",
                    BusType = "Volvo AC Seater",
                    FromCity = "Hyderabad",
                    ToCity = "Vijayawada",
                    DepartureTime = now.AddDays(2),
                    ArrivalTime = now.AddDays(2).AddHours(4.5), // 4.5 hours = 270 minutes
                    PriceInr = 1300.00m,
                    AvailableSeats = 40,
                    TotalSeats = 40,
                    BoardingPoint = "Ameerpet",
                    DroppingPoint = "Benz Circle",
                    GstCategory = "VOLVO",
                },
                new BusBooking
                {
                    BusNumber = "PNB-B2004",
                    OperatorName = "Dhanunjaya Travels",
                    BusType = "Non-AC Sleeper",
                    FromCity = "Hyderabad",
                    ToCity = "Vijayawada",
                    DepartureTime = now.AddDays(3),
                    ArrivalTime = now.AddDays(3).AddHours(5.5), // 5.5 hours = 330 minutes
                    PriceInr = 800.00m,
                    AvailableSeats = 30,
                    TotalSeats = 30,
                    BoardingPoint = "MGBS",
                    DroppingPoint = "Benz Circle",
                    GstCategory = "Non-AC",
                },
                new BusBooking
                {
                    BusNumber = "PNB-B2005",
                    OperatorName = "Diwakar Travels",
                    BusType = "Non AC Seater/Sleeper 2+1",
                    FromCity = "Hyderabad",
                    ToCity = "Vijayawada",
                    DepartureTime = now.AddDays(4),
                    ArrivalTime = now.AddDays(4).AddHours(5.17), // 5.17 hours ~ 310 minutes
                    PriceInr = 700.00m,
                    AvailableSeats = 45,
                    TotalSeats = 45,
                    BoardingPoint = "Ameerpet",
                    DroppingPoint = "Benz Circle",
                    GstCategory = "Non-AC",
                },
                new BusBooking
                {
                    BusNumber = "PNB-B2006",
                    OperatorName = "Rajesh Travels",
                    BusType = "Non-AC Seater",
                    FromCity = "Hyderabad",
                    ToCity = "Vijayawada",
                    DepartureTime = now.AddDays(5),
                    ArrivalTime = now.AddDays(5).AddHours(5.33), // 5.33 hours ~ 320 minutes
                    PriceInr = 600.00m,
                    AvailableSeats = 44,
                    TotalSeats = 44,
                    BoardingPoint = "MGBS",
                    DroppingPoint = "Benz Circle",
                    GstCategory = "Non-AC",
                }
            };

            await dbContext.BusBookings.AddRangeAsync(newBuses, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            Console.WriteLine("Seeded 5 new buses into bus_bookings.");
        }


      


       
        var hasFullWeekData = await dbContext.BusBookings
     .Where(x => x.DepartureTime > DateTime.UtcNow)
     .Select(x => x.DepartureTime.Date)
     .Distinct()
     .CountAsync(cancellationToken);

        if (hasFullWeekData < 7)
        {
            await InsertNextWeekBusSchedulesAsync(dbContext, cancellationToken);
        }

        
        await EnsureSeatMapsAsync(dbContext, cancellationToken);

    }
    
    private static async Task EnsureSeatMapsAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        // Seats are generated on-demand when accessed via APIs to preserve DB connections
        // 
        // await EnsureBusSeatMapsAsync(dbContext, cancellationToken);
    }

    private static List<BusBooking> BuildBusSeed()
    {
        var templates = new[]
        {
            new BusTemplate("PNB-B1001", "VRL Travels", "AC Sleeper", "Mumbai", "Pune", 1, 6, 0, 210, 850m, 18, 36, "Borivali", "Swargate"),
            new BusTemplate("PNB-B1002", "RedBus Partner", "Non-AC Seater", "Delhi", "Jaipur", 1, 7, 30, 300, 650m, 22, 44, "ISBT Kashmere Gate", "Sindhi Camp"),
            new BusTemplate("PNB-B1003", "SRS Travels", "AC Sleeper", "Bengaluru", "Chennai", 1, 22, 0, 390, 1200m, 14, 32, "Madiwala", "Koyambedu"),
            new BusTemplate("PNB-B1004", "Orange Travels", "Volvo Multi-Axle", "Hyderabad", "Bengaluru", 1, 21, 15, 510, 1450m, 16, 40, "MGBS", "Majestic"),
            new BusTemplate("PNB-B1005", "Gujarat Travels", "AC Seater", "Ahmedabad", "Udaipur", 2, 8, 45, 330, 980m, 20, 40, "Paldi", "Udaipole"),
            new BusTemplate("PNB-B1006", "Parveen Travels", "AC Sleeper", "Chennai", "Coimbatore", 2, 22, 30, 470, 1350m, 19, 36, "Perungalathur", "Gandhipuram"),
            new BusTemplate("PNB-B1007", "GreenLine", "Volvo AC", "Kolkata", "Bhubaneswar", 2, 20, 0, 430, 1250m, 17, 40, "Esplanade", "Baramunda"),
            new BusTemplate("PNB-B1008", "Neeta Tours", "AC Sleeper", "Goa", "Mumbai", 2, 18, 45, 710, 1650m, 12, 34, "Mapusa", "Dadar"),
            new BusTemplate("PNB-B1009", "RSRTC", "Non-AC Seater", "Jaipur", "Delhi", 3, 9, 15, 310, 620m, 25, 48, "Sindhi Camp", "ISBT Kashmere Gate"),
            new BusTemplate("PNB-B1010", "IntrCity", "AC Sleeper", "Lucknow", "Delhi", 3, 21, 0, 560, 1550m, 13, 30, "Alambagh", "Anand Vihar"),
            new BusTemplate("PNB-B1011", "TSRTC", "AC Seater", "Hyderabad", "Vijayawada", 3, 6, 50, 330, 900m, 24, 44, "Ameerpet", "Benz Circle"),
            new BusTemplate("PNB-B1012", "KSRTC", "AC Seater", "Bengaluru", "Mysuru", 3, 7, 10, 190, 550m, 27, 44, "Satellite Bus Stand", "Mysuru Suburban"),
            new BusTemplate("PNB-B1013", "HRTC", "Volvo AC", "Delhi", "Chandigarh", 4, 5, 30, 260, 780m, 20, 40, "Majnu Ka Tila", "Sector 43"),
            new BusTemplate("PNB-B1014", "AbhiBus Partner", "AC Sleeper", "Pune", "Nagpur", 4, 20, 40, 720, 1750m, 11, 30, "Wakad", "Ravi Nagar"),
            new BusTemplate("PNB-B1015", "Patel Travels", "Non-AC Sleeper", "Surat", "Ahmedabad", 4, 23, 15, 270, 740m, 18, 40, "Adajan", "Geeta Mandir"),
            new BusTemplate("PNB-B1016", "Sangitam", "AC Sleeper", "Bhopal", "Indore", 5, 6, 45, 240, 620m, 21, 36, "Nadra Bus Stand", "Sarvate"),
            new BusTemplate("PNB-B1017", "Orange Travels", "AC Sleeper", "Visakhapatnam", "Hyderabad", 5, 19, 20, 760, 1850m, 10, 28, "Maddilapalem", "Miyapur"),
            new BusTemplate("PNB-B1018", "VRL Travels", "Volvo AC Seater", "Mumbai", "Goa", 5, 20, 10, 690, 1700m, 9, 40, "Sion", "Panaji"),
            new BusTemplate("PNB-B2001", "SURESH TRAVELS", "Non AC Seater/Sleeper 2+1", "Hyderabad", "Vijayawada", 5, 15, 30, 480, 750m, 45, 45, "MGBS", "Benz Circle"),
            new BusTemplate("PNB-B2002", "Kaveri Travels", "AC Sleeper", "Hyderabad", "Vijayawada", 1, 23, 0, 300, 1100m, 36, 36, "MGBS", "Benz Circle"),
            new BusTemplate("PNB-B2003", "Morning Star Travels", "Volvo AC Seater", "Hyderabad", "Vijayawada", 2, 8, 30, 270, 1300m, 40, 40, "Ameerpet", "Benz Circle"),
            new BusTemplate("PNB-B2004", "Dhanunjaya Travels", "Non-AC Sleeper", "Hyderabad", "Vijayawada", 3, 22, 15, 330, 800m, 30, 30, "MGBS", "Benz Circle"),
            new BusTemplate("PNB-B2005", "Diwakar Travels", "Non AC Seater/Sleeper 2+1", "Hyderabad", "Vijayawada", 4, 14, 0, 310, 700m, 45, 45, "Ameerpet", "Benz Circle"),
            new BusTemplate("PNB-B2006", "Rajesh Travels", "Non-AC Seater", "Hyderabad", "Vijayawada", 5, 10, 0, 320, 600m, 44, 44, "MGBS", "Benz Circle"),
            new BusTemplate("TS-HYB-002", "Royal Travels", "SEATER/SLEEPER 2+1 HYBRID AC", "Delhi", "Jaipur", 5, 18, 0, 420, 1350m, 36, 36, "Delhi ISBT", "Jaipur Sindhi Camp")
        };
        
        return templates.Select(t =>
        {
            var departure = DepartureAtUtcFromIst(t.DayOffset, t.DepartureHour, t.DepartureMinute);
            var boardingPoint = t.BoardingPoint;
            var droppingPoint = t.DroppingPoint;



            string assignedGstCategory = "AC";
            if (t.BusType.Contains("Non-AC", StringComparison.OrdinalIgnoreCase) || 
                t.BusType.Contains("Non AC", StringComparison.OrdinalIgnoreCase))
            {
                assignedGstCategory = "Non-AC";
            }
            else if (t.BusType.Contains("Volvo", StringComparison.OrdinalIgnoreCase))
            {
                assignedGstCategory = "VOLVO";
            }

            return new BusBooking
            {
                BusNumber = t.BusNumber,
                OperatorName = t.OperatorName,
                BusType = t.BusType,
                GstCategory = assignedGstCategory,
                FromCity = t.FromCity,
                ToCity = t.ToCity,
                DepartureTime = departure,
                ArrivalTime = departure.AddMinutes(t.DurationMinutes),
                PriceInr = t.PriceInr,
                AvailableSeats = t.AvailableSeats,
                TotalSeats = t.TotalSeats,
                BoardingPoint = boardingPoint,
                DroppingPoint = droppingPoint,
            };
        }).ToList();
    }
    private static async Task InsertNextWeekBusSchedulesAsync(
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var todayIst = DateTimeOffset.UtcNow.ToOffset(IndiaOffset).Date;

        // ── 1. Fetch one template row per unique bus+route ──────────────────────
        var allRows = await dbContext.BusBookings
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var templates = allRows
            .GroupBy(x => new
            {
                x.BusNumber,
                x.FromCity,
                x.ToCity,
                Time = x.DepartureTime.Add(IndiaOffset).TimeOfDay
            })
            .Select(g => g.OrderBy(x => x.Id).First())   // always pick the seed/original row
            .ToList();

        // ── 2. Load ALL existing keys for the next 7 days in ONE query ──────────
        var windowStart = DateTime.SpecifyKind(todayIst.AddDays(1) - IndiaOffset, DateTimeKind.Utc);
        var windowEnd = DateTime.SpecifyKind(todayIst.AddDays(8) - IndiaOffset, DateTimeKind.Utc);

        var existingKeys = await dbContext.BusBookings
            .AsNoTracking()
            .Where(x => x.DepartureTime >= windowStart && x.DepartureTime < windowEnd)
            .Select(x => new { x.BusNumber, x.FromCity, x.ToCity, x.DepartureTime })
            .ToListAsync(cancellationToken);

        // Use minute-precision string key — immune to sub-minute tick differences
        var existingSet = existingKeys
            .Select(x => $"{x.BusNumber}|{x.FromCity}|{x.ToCity}|{x.DepartureTime:yyyy-MM-ddTHH:mm:ss}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ── 3. Build only the genuinely missing rows ────────────────────────────
        var newBuses = new List<BusBooking>();

        foreach (var template in templates)
        {
            for (int i = 1; i <= 7; i++)
            {
                var newDateIst = todayIst.AddDays(i);

                var depIstTime = template.DepartureTime.Add(IndiaOffset).TimeOfDay;
                var depIst = new DateTime(newDateIst.Year, newDateIst.Month, newDateIst.Day).Add(depIstTime);
                var depUtc = DateTime.SpecifyKind(depIst - IndiaOffset, DateTimeKind.Utc);

                // ✅ ONE set lookup instead of one DB round-trip per row
                var key = $"{template.BusNumber}|{template.FromCity}|{template.ToCity}|{depUtc:yyyy-MM-ddTHH:mm:ss}";
                if (existingSet.Contains(key))
                    continue;

                var duration = template.ArrivalTime - template.DepartureTime;
                if (duration <= TimeSpan.Zero)
                    duration = TimeSpan.FromMinutes(300);

                newBuses.Add(new BusBooking
                {
                    BusNumber = template.BusNumber,
                    OperatorName = template.OperatorName,
                    BusType = template.BusType,
                    GstCategory = template.GstCategory,
                    FromCity = template.FromCity,
                    ToCity = template.ToCity,
                    BoardingPoint = template.BoardingPoint,
                    DroppingPoint = template.DroppingPoint,
                    DepartureTime = depUtc,
                    ArrivalTime = depUtc.Add(duration),
                    PriceInr = template.PriceInr,
                    TotalSeats = template.TotalSeats,
                    AvailableSeats = template.TotalSeats
                });
            }
        }

        // ── 4. Insert only if there is something new ────────────────────────────
        if (newBuses.Count == 0)
            return;

        await dbContext.BusBookings.AddRangeAsync(newBuses, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            Console.WriteLine($"Inserted {newBuses.Count} new bus schedule rows.");
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("Duplicate") == true)
        {
            // DB-level safety net — should never fire now
            Console.WriteLine("Duplicate insert caught and ignored.");
            dbContext.ChangeTracker.Clear();
        }
    }

    private static (DateTime StartUtc, DateTime EndUtc) NextWeekUtcRange()
    {
        var todayIst = DateTimeOffset.UtcNow.ToOffset(IndiaOffset).Date;
        var startIst = new DateTimeOffset(todayIst.Year, todayIst.Month, todayIst.Day, 0, 0, 0, IndiaOffset).AddDays(7);
        var endIst = startIst.AddDays(7);
        return (startIst.UtcDateTime, endIst.UtcDateTime);
    }

    private static DateTime DepartureAtUtcFromIst(int dayOffset, int hour, int minute)
    {
        var todayIst = DateTimeOffset.UtcNow.ToOffset(IndiaOffset).Date;
        var date = todayIst.AddDays(dayOffset);
        var departureIst = new DateTimeOffset(date.Year, date.Month, date.Day, hour, minute, 0, IndiaOffset);
        return departureIst.UtcDateTime;
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 23;
            foreach (var c in value)
            {
                hash = (hash * 31) + c;
            }

            return Math.Abs(hash);
        }
    }

    private sealed record BusTemplate(
        string BusNumber,
        string OperatorName,
        string BusType,
        string FromCity,
        string ToCity,
        int DayOffset,
        int DepartureHour,
        int DepartureMinute,
        int DurationMinutes,
        decimal PriceInr,
        int AvailableSeats,
        int TotalSeats,
        string BoardingPoint,
        string DroppingPoint);

    private sealed record AirlineDef(string Name, string Code);
}

