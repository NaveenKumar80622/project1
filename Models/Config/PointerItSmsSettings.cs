namespace PickNBook.Api.Models.Config
{
    public class PointerItSmsSettings
    {
        public string Url { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string SenderId { get; set; } = string.Empty;
        public string PrincipalEntityId { get; set; } = string.Empty;
        public string ContentId { get; set; } = string.Empty;
        public string DefaultContentId 
        { 
            get => string.IsNullOrWhiteSpace(_defaultContentId) ? ContentId : _defaultContentId; 
            set => _defaultContentId = value; 
        }
        private string _defaultContentId = string.Empty;
    }
}
