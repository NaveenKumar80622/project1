namespace PickNBook.Api.Services.SeatLayouts
{
    // ─────────────────────────────────────────────────────────────
    //  Core contract every layout must satisfy
    // ─────────────────────────────────────────────────────────────
    public interface IBusSeatLayout
    {
        /// <summary>
        /// Short identifier sent to the frontend so it knows which
        /// React component to render. Examples: "seater", "sleeper",
        /// "mixed", "semi-sleeper", "double-decker".
        /// Must be unique across all registered layouts.
        /// </summary>
        string LayoutType { get; }

        /// <summary>
        /// Human-readable name shown in admin UI / logs.
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Returns true when this layout should handle the given
        /// BusBooking.BusType string (case-insensitive matching is
        /// the implementor's responsibility).
        /// The registry tries layouts in priority order and picks
        /// the first match, so more-specific layouts should have a
        /// higher priority number.
        /// </summary>
        bool Matches(string busType);

        /// <summary>
        /// Priority used when two layouts both match the same
        /// busType string. Higher number wins.
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Generates the ordered list of seat codes for a bus with
        /// the given total seat count.
        /// The ordering matters: the frontend renders seats in this
        /// exact order, section by section.
        /// </summary>
        List<string> BuildSeatCodes(
            int totalSeats,
            string? busType = null);

        /// <summary>
        /// Optional: describes the physical sections so the frontend
        /// can render labelled deck/zone headers without hard-coding
        /// any layout knowledge.
        /// Return an empty list if your layout has no distinct
        /// sections.
        /// </summary>
        List<SeatSection> GetSections(
            int totalSeats,
            string? busType = null) => [];

        List<SeatDefinition> GetSeatDefinitions(
            int totalSeats,
            string? busType = null);
    }

    // ─────────────────────────────────────────────────────────────
    //  Section descriptor (optional per-layout metadata)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Describes a logical section of the bus (a deck, a zone, …).
    /// The frontend receives this list and uses it to render section
    /// headers / separators between groups of seats.
    /// </summary>
    public sealed class SeatSection
    {
        /// <summary>Label shown above this section. E.g. "Lower Deck", "Seater Zone".</summary>
        public string Label { get; init; } = string.Empty;

        /// <summary>Seat codes that belong to this section, in display order.</summary>
        public List<string> SeatCodes { get; init; } = [];

        /// <summary>
        /// Hint for the frontend: how many columns per row in this section.
        /// 4 = 2+2 seater layout, 3 = 2+1 seater or sleeper-style layout,
        /// 1 = single-column sleepers, etc.
        /// </summary>
        public int ColumnsPerRow { get; init; } = 4;

        /// <summary>
        /// Where the aisle gap falls (0-based column index BEFORE the gap).
        /// -1 means no aisle. For 2+1: AisleAfterColumn = 0 (gap after first column).
        /// </summary>
        public int AisleAfterColumn { get; init; } = -1;

        public string SeatType { get; init; } = "SEATER";
        public string? Variant { get; init; }
    }

    // ─────────────────────────────────────────────────────────────
    //  Registry – single source of truth for all layouts
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Holds every registered layout and picks the right one for a
    /// given bus type string.
    ///
    /// TO REGISTER A NEW LAYOUT: add one line to the static
    /// constructor below, e.g.:
    ///   Register(new DoubleDecker());
    /// </summary>
    public static class BusSeatLayoutRegistry
    {
        private static readonly List<IBusSeatLayout> _layouts = [];

        // ── static constructor: register all built-in layouts ───────
        static BusSeatLayoutRegistry()
        {
            Register(new SeaterLayout());
            Register(new SleeperLayout());

            Register(new SeaterSleeper2Plus1Layout());
            // ↑ Add your new layout class here and nowhere else.
        }

        // ── public API ───────────────────────────────────────────────

        /// <summary>
        /// Register a layout. Called by the static constructor for
        /// built-in layouts, but can also be called from tests or
        /// integration setups to inject custom layouts.
        /// </summary>
        public static void Register(IBusSeatLayout layout)
        {
            if (_layouts.Any(l => l.LayoutType == layout.LayoutType))
            {
                throw new InvalidOperationException(
                    $"A layout with LayoutType '{layout.LayoutType}' is already registered.");
            }
            _layouts.Add(layout);
        }

        /// <summary>Returns all registered layouts (read-only snapshot).</summary>
        public static IReadOnlyList<IBusSeatLayout> All =>
            _layouts.OrderByDescending(l => l.Priority).ToList();

        /// <summary>
        /// Finds the highest-priority layout that matches busType.
        /// Falls back to SeaterLayout if nothing matches (safe default).
        /// </summary>
        public static IBusSeatLayout Resolve(string busType)
        {
            return _layouts
                .OrderByDescending(l => l.Priority)
                .FirstOrDefault(l => l.Matches(busType))
                ?? _layouts.First(l => l.LayoutType == SeaterLayout.TypeId);
        }

        public static string GetSeatType(
            string busType,
            string seatCode,
            int totalSeats)
        {
            var layout = Resolve(busType);

            var sections = layout.GetSections(totalSeats, busType);

            foreach (var section in sections)
            {
                if (section.SeatCodes.Contains(
                    seatCode,
                    StringComparer.OrdinalIgnoreCase))
                {
                    return section.SeatType;
                }
            }

            return "SEATER";
        }

        // ── convenience helpers used by controllers & seeder ─────────

        public static string GetLayoutType(string busType) =>
            Resolve(busType).LayoutType;

        public static List<string> BuildSeatCodes(
            int totalSeats,
            string busType) =>
            Resolve(busType)
                .BuildSeatCodes(totalSeats, busType);

        public static List<SeatSection> GetSections(
            int totalSeats,
            string busType) =>
            Resolve(busType)
                .GetSections(totalSeats, busType);
    }
}