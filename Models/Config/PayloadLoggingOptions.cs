using System.Text.Json.Serialization;

namespace PickNBook.Api.Models.Config
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PayloadLoggingMode
    {
        Omit,       // Logs "[Response payload omitted for performance]" on heavy endpoints
        Normal,     // Full payload in compact raw JSON
        Beautified  // Full payload formatted with indentation and line breaks
    }

    public class PayloadLoggingOptions
    {
        public const string SectionName = "PayloadLogging";

        /// <summary>
        /// Operating mode: Omit, Normal, or Beautified. Defaults to Omit.
        /// </summary>
        public PayloadLoggingMode Mode { get; set; } = PayloadLoggingMode.Omit;

        /// <summary>
        /// Boolean helper: true enables payload recording (defaults to Beautified unless Beautify is explicitly false).
        /// false sets Mode to Omit.
        /// </summary>
        public bool? RecordLargePayloads
        {
            get => Mode != PayloadLoggingMode.Omit;
            set
            {
                if (value.HasValue)
                {
                    Mode = value.Value
                        ? (Beautify == false ? PayloadLoggingMode.Normal : PayloadLoggingMode.Beautified)
                        : PayloadLoggingMode.Omit;
                }
            }
        }

        /// <summary>
        /// Boolean helper: when recording is enabled, true uses Beautified (indented) and false uses Normal (compact).
        /// </summary>
        public bool? Beautify
        {
            get => Mode == PayloadLoggingMode.Beautified;
            set
            {
                if (value.HasValue && Mode != PayloadLoggingMode.Omit)
                {
                    Mode = value.Value ? PayloadLoggingMode.Beautified : PayloadLoggingMode.Normal;
                }
            }
        }

        /// <summary>
        /// Safety limit: maximum characters to log. Truncates with warning beyond this limit.
        /// Default is 500,000 characters. Set to 0 or -1 for unlimited.
        /// </summary>
        public int MaxPayloadLength { get; set; } = 500000;
    }
}
