namespace PickNBook.Api.Models.DTOs
{
    public class NotificationQueryParameters
    {
        private int _page = 1;
        private int _pageSize = 20;

        public int Page
        {
            get => _page;
            set => _page = value < 1 ? 1 : value;
        }

        public int? PageNumber
        {
            get => _page;
            set
            {
                if (value.HasValue)
                    _page = value.Value < 1 ? 1 : value.Value;
            }
        }

        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = value < 1 ? 20 : (value > 100 ? 100 : value);
        }

        public bool? UnreadOnly { get; set; }
        public string? Category { get; set; }
        public string? Type { get; set; }
        public string? Severity { get; set; }
        public string? TargetRole { get; set; }
    }
}
