namespace PickNBook.Api.Services.SeatLayouts
{
    public sealed class SeatDefinition
    {
        public string SeatCode { get; set; } = string.Empty;

        public string SeatType { get; set; } = "SEATER";

        public string Deck { get; set; } = "LOWER";

        public int Row { get; set; }

        public int Column { get; set; }

        public int Width { get; set; } = 1;

        public int Height { get; set; } = 1;

        public bool IsSleeper { get; set; }

        public bool IsUpper { get; set; }

        public string? Variant { get; set; }
    }
}
