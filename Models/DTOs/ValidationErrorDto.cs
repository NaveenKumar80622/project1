using System.Text.Json.Serialization;

namespace PickNBook.Api.Models.DTOs
{
    public class ValidationErrorItem
    {
        [JsonPropertyName("field")]
        public string Field { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    public class ValidationErrorDto
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; } = false;

        [JsonPropertyName("errorCode")]
        public string ErrorCode { get; set; } = "VALIDATION_FAILED";

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("errors")]
        public List<ValidationErrorItem> Errors { get; set; } = new();

        public static ValidationErrorDto Create(string message, string? field = null, string errorCode = "VALIDATION_FAILED")
        {
            var dto = new ValidationErrorDto
            {
                Success = false,
                ErrorCode = errorCode,
                Message = message
            };

            if (!string.IsNullOrWhiteSpace(field))
            {
                dto.Errors.Add(new ValidationErrorItem
                {
                    Field = field,
                    Message = message
                });
            }

            return dto;
        }
    }
}
