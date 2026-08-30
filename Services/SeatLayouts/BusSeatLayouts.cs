namespace PickNBook.Api.Services.SeatLayouts
{
    public sealed class SeaterLayout : IBusSeatLayout
    {
        public const string TypeId = "seater";

        public string LayoutType => TypeId;
        public string DisplayName => "Seater (2+2)";
        public int Priority => 10;

        private static readonly char[] Letters = ['A', 'B', 'C', 'D'];

        public bool Matches(string busType)
        {
            var lower = busType.ToLowerInvariant();

            return (lower.Contains("seater") || lower.Contains("seat"))
                   && !lower.Contains("sleeper");
        }

        public List<string> BuildSeatCodes(
            int totalSeats,
            string? busType = null)
        {
            var seats = new List<string>(totalSeats);

            for (var i = 0; i < totalSeats; i++)
            {
                var row = (i / Letters.Length) + 1;
                var letter = Letters[i % Letters.Length];

                seats.Add($"{row}{letter}");
            }

            return seats;
        }

        public List<SeatSection> GetSections(
            int totalSeats,
            string? busType = null)
        {
            var codes = BuildSeatCodes(totalSeats, busType);

            return
            [
                new SeatSection
            {
                Label = string.Empty,
                SeatCodes = codes,
                ColumnsPerRow = 4,
                AisleAfterColumn = 1,
                SeatType = "SEATER",
                Variant = "STANDARD"
            }
            ];
        }

        public List<SeatDefinition> GetSeatDefinitions(
            int totalSeats,
            string? busType = null)
        {
            var defs = new List<SeatDefinition>();
            var seats = BuildSeatCodes(totalSeats, busType);

            for (int i = 0; i < seats.Count; i++)
            {
                defs.Add(new SeatDefinition
                {
                    SeatCode = seats[i],
                    SeatType = "SEATER",
                    Deck = "LOWER",
                    Row = (i / 4) + 1,
                    Column = (i % 4) + 1
                });
            }

            return defs;
        }
    }


    // ─────────────────────────────────────────────────────────────
    //  2. SLEEPER  (Lower + Upper Berths)  => 1 + aisle + 2 style
    // ─────────────────────────────────────────────────────────────
    public sealed class SleeperLayout : IBusSeatLayout
    {
        public const string TypeId = "sleeper";

        public string LayoutType => TypeId;
        public string DisplayName => "Sleeper (L/U Berths)";
        public int Priority => 10;

        public bool Matches(string busType)
        {
            var lower = busType.ToLowerInvariant();

            return lower.Contains("sleeper")
                   && !lower.Contains("seater");
        }

        public List<string> BuildSeatCodes(
            int totalSeats,
            string? busType = null)
        {
            var upper = totalSeats / 2;
            var lower = totalSeats - upper;

            var seats = new List<string>(totalSeats);

            for (var i = 1; i <= lower; i++)
                seats.Add($"L{i}");

            for (var i = 1; i <= upper; i++)
                seats.Add($"U{i}");

            return seats;
        }

        public List<SeatSection> GetSections(
            int totalSeats,
            string? busType = null)
        {
            var upper = totalSeats / 2;
            var lower = totalSeats - upper;

            var lowerCodes = Enumerable.Range(1, lower)
                                       .Select(i => $"L{i}")
                                       .ToList();

            var upperCodes = Enumerable.Range(1, upper)
                                       .Select(i => $"U{i}")
                                       .ToList();

            return
            [
                new SeatSection
                {
                    Label = "Lower Deck",
                    SeatCodes = lowerCodes,
                    ColumnsPerRow = 3,
                    AisleAfterColumn = 0,
                    SeatType = "SLEEPER",
                    Variant = "STANDARD"
                },

                new SeatSection
                {
                    Label = "Upper Deck",
                    SeatCodes = upperCodes,
                    ColumnsPerRow = 3,
                    AisleAfterColumn = 0,
                    SeatType = "SLEEPER",
                    Variant = "STANDARD"
                }
            ];
        }

        public List<SeatDefinition> GetSeatDefinitions(
            int totalSeats,
            string? busType = null)
        {
            var defs = new List<SeatDefinition>();

            var upper = totalSeats / 2;
            var lower = totalSeats - upper;

            for (int i = 1; i <= lower; i++)
            {
                defs.Add(new SeatDefinition
                {
                    SeatCode = $"L{i}",
                    SeatType = "SLEEPER",
                    Deck = "LOWER",
                    Row = ((i - 1) / 3) + 1,
                    Column = ((i - 1) % 3) + 1,
                    IsSleeper = true
                });
            }

            for (int i = 1; i <= upper; i++)
            {
                defs.Add(new SeatDefinition
                {
                    SeatCode = $"U{i}",
                    SeatType = "SLEEPER",
                    Deck = "UPPER",
                    Row = ((i - 1) / 3) + 1,
                    Column = ((i - 1) % 3) + 1,
                    IsSleeper = true,
                    IsUpper = true
                });
            }

            return defs;
        }
    }


    // ─────────────────────────────────────────────────────────────
    //  3. SEATER + SLEEPER (2+1)
    //
    //  STANDARD
    //      Lower = Seater
    //      Upper = Sleeper
    //
    //  HYBRID
    //      Lower = Sleeper + Seater
    //      Upper = Sleeper
    // ─────────────────────────────────────────────────────────────
    public sealed class SeaterSleeper2Plus1Layout : IBusSeatLayout
    {
        public const string TypeId = "seater-sleeper-2plus1";

        public const string StandardVariant = "STANDARD";
        public const string HybridVariant = "HYBRID";

        public string LayoutType => TypeId;
        public string DisplayName => "Seater/Sleeper (2+1)";
        public int Priority => 40;

        public bool Matches(string busType)
        {
            var lower = busType.ToLowerInvariant();

            return lower.Contains("2+1");
        }

        public List<string> BuildSeatCodes(
            int totalSeats,
            string? busType = null)
        {
            var seats = new List<string>();

            var lowerBusType =
                busType?.ToLowerInvariant() ?? string.Empty;

            // ─────────────────────────────────────────────
            // HYBRID VARIANT
            // ─────────────────────────────────────────────
            if (lowerBusType.Contains("hybrid"))
            {
                // LOWER SLEEPERS
                for (int i = 1; i <= 6; i++)
                    seats.Add($"LS{i}");

                // LOWER SEATERS
                for (int i = 1; i <= 18; i++)
                    seats.Add($"L{i}");

                // UPPER SLEEPERS
                for (int i = 1; i <= 12; i++)
                    seats.Add($"U{i}");

                return seats;
            }

            // ─────────────────────────────────────────────
            // STANDARD VARIANT
            // ─────────────────────────────────────────────
            var upper = (int)Math.Ceiling(totalSeats / 3.0);
            var lower = totalSeats - upper;

            for (var i = 1; i <= lower; i++)
                seats.Add($"L{i}");

            for (var i = 1; i <= upper; i++)
                seats.Add($"U{i}");

            return seats;
        }

        public List<SeatSection> GetSections(
            int totalSeats,
            string? busType = null)
        {
            var lowerBusType =
                busType?.ToLowerInvariant() ?? string.Empty;

            // ─────────────────────────────────────────────
            // STANDARD VARIANT
            // ─────────────────────────────────────────────
            if (!lowerBusType.Contains("hybrid"))
            {
                var upper = (int)Math.Ceiling(totalSeats / 3.0);
                var lower = totalSeats - upper;

                var lowerCodes = Enumerable.Range(1, lower)
                                           .Select(i => $"L{i}")
                                           .ToList();

                var upperCodes = Enumerable.Range(1, upper)
                                           .Select(i => $"U{i}")
                                           .ToList();

                return
                [
                    new SeatSection
                    {
                        Label = "Lower Deck",
                        SeatCodes = lowerCodes,
                        ColumnsPerRow = 3,
                        AisleAfterColumn = 0,
                        SeatType = "SEATER",
                        Variant = StandardVariant
                    },

                    new SeatSection
                    {
                        Label = "Upper Deck",
                        SeatCodes = upperCodes,
                        ColumnsPerRow = 3,
                        AisleAfterColumn = 0,
                        SeatType = "SLEEPER",
                        Variant = StandardVariant
                    }
                ];
            }

            // ─────────────────────────────────────────────
            // HYBRID VARIANT
            // EXACT FIXED COUNTS
            // TOTAL = 36
            //
            // LS1-LS6   = 6 lower sleepers
            // L1-L18    = 18 lower seaters
            //              → 2 COLUMNS × 9 ROWS
            // U1-U12    = 12 upper sleepers
            // ─────────────────────────────────────────────
            var lowerSleeperSeats =
                Enumerable.Range(1, 6)
                          .Select(i => $"LS{i}")
                          .ToList();

            var lowerSeaterSeats =
                Enumerable.Range(1, 18)
                          .Select(i => $"L{i}")
                          .ToList();

            var upperSleeperSeats =
                Enumerable.Range(1, 12)
                          .Select(i => $"U{i}")
                          .ToList();

            return
            [
                new SeatSection
    {
        Label = "Lower Sleeper",
        SeatCodes = lowerSleeperSeats,
        ColumnsPerRow = 1,
        AisleAfterColumn = 0,

        SeatType = "SLEEPER",
        Variant = HybridVariant
    },

    new SeatSection
    {
        Label = "Lower Seater",

        SeatCodes = lowerSeaterSeats,

        // 2 COLUMNS
        ColumnsPerRow = 2,

        AisleAfterColumn = -1,

        SeatType = "SEATER",
        Variant = HybridVariant
    },

    new SeatSection
    {
        Label = "Upper Sleeper",
        SeatCodes = upperSleeperSeats,

        // 3 columns
        ColumnsPerRow = 3,

        AisleAfterColumn = 0,

        SeatType = "SLEEPER",
        Variant = HybridVariant
    }
            ];
        }

        public List<SeatDefinition> GetSeatDefinitions(
            int totalSeats,
            string? busType = null)
        {
            var defs = new List<SeatDefinition>();

            var lowerBusType =
                busType?.ToLowerInvariant() ?? string.Empty;

            // ─────────────────────────────────────────────
            // HYBRID VARIANT
            // ─────────────────────────────────────────────
            if (lowerBusType.Contains("hybrid"))
            {
                // ─────────────────────────────────────────
                // LOWER SLEEPERS
                // LS1-LS6
                // ─────────────────────────────────────────
                for (int i = 1; i <= 6; i++)
                {
                    defs.Add(new SeatDefinition
                    {
                        SeatCode = $"LS{i}",
                        SeatType = "SLEEPER",
                        Deck = "LOWER",

                        // vertical stack
                        Row = i,
                        Column = 1,

                        IsSleeper = true
                    });
                }

                // ─────────────────────────────────────────
                // LOWER SEATERS
                // 2 COLUMNS × 9 ROWS
                // ─────────────────────────────────────────
                int lowerSeaterRow = 1;

                for (int i = 1; i <= 18; i += 2)
                {
                    defs.Add(new SeatDefinition
                    {
                        SeatCode = $"L{i}",
                        SeatType = "SEATER",
                        Deck = "LOWER",

                        Row = lowerSeaterRow,
                        Column = 1
                    });

                    if (i + 1 <= 18)
                    {
                        defs.Add(new SeatDefinition
                        {
                            SeatCode = $"L{i + 1}",
                            SeatType = "SEATER",
                            Deck = "LOWER",

                            Row = lowerSeaterRow,
                            Column = 2
                        });
                    }

                    lowerSeaterRow++;
                }

                // ─────────────────────────────────────────
                // UPPER SLEEPERS
                // 3 COLUMNS × 4 ROWS
                // ─────────────────────────────────────────
                int upperSleeperRow = 1;

                for (int i = 1; i <= 12; i++)
                {
                    defs.Add(new SeatDefinition
                    {
                        SeatCode = $"U{i}",
                        SeatType = "SLEEPER",
                        Deck = "UPPER",

                        Row = upperSleeperRow,
                        Column = ((i - 1) % 3) + 1,

                        IsSleeper = true,
                        IsUpper = true
                    });

                    if (i % 3 == 0)
                        upperSleeperRow++;
                }
            

                return defs;
            }
            // ─────────────────────────────────────────────
            // STANDARD VARIANT
            // ─────────────────────────────────────────────
            var upper = (int)Math.Ceiling(totalSeats / 3.0);
            var lower = totalSeats - upper;

            // LOWER SEATERS
            int lowerRow = 1;

            for (int i = 1; i <= lower; i += 3)
            {
                defs.Add(new SeatDefinition
                {
                    SeatCode = $"L{i}",
                    SeatType = "SEATER",
                    Deck = "LOWER",
                    Row = lowerRow,
                    Column = 1
                });

                if (i + 1 <= lower)
                {
                    defs.Add(new SeatDefinition
                    {
                        SeatCode = $"L{i + 1}",
                        SeatType = "SEATER",
                        Deck = "LOWER",
                        Row = lowerRow,
                        Column = 2
                    });
                }

                if (i + 2 <= lower)
                {
                    defs.Add(new SeatDefinition
                    {
                        SeatCode = $"L{i + 2}",
                        SeatType = "SEATER",
                        Deck = "LOWER",
                        Row = lowerRow,
                        Column = 3
                    });
                }

                lowerRow++;
            }

            // UPPER SLEEPERS
            int upperRow = 1;

            for (int i = 1; i <= upper; i++)
            {
                defs.Add(new SeatDefinition
                {
                    SeatCode = $"U{i}",
                    SeatType = "SLEEPER",
                    Deck = "UPPER",
                    Row = upperRow,
                    Column = ((i - 1) % 3) + 1,
                    IsSleeper = true,
                    IsUpper = true
                });

                if (i % 3 == 0)
                    upperRow++;
            }

            return defs;
        }
    }
}