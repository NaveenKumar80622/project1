using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PickNBook.Api.Models.Config
{
    /// <summary>
    /// Sets format="date" on CheckInDate/CheckOutDate string properties
    /// so Swagger UI renders a calendar date picker instead of a text field.
    /// </summary>
    public class DateFormatSchemaFilter : ISchemaFilter
    {
        private static readonly HashSet<string> DatePropertyNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CheckInDate", "CheckOutDate"
        };

        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (schema.Properties == null) return;

            foreach (var prop in schema.Properties)
            {
                if (DatePropertyNames.Contains(prop.Key) && prop.Value.Type == "string")
                {
                    prop.Value.Format = "date";
                    if (prop.Key.Equals("CheckOutDate", StringComparison.OrdinalIgnoreCase))
                    {
                        prop.Value.Example = new Microsoft.OpenApi.Any.OpenApiString("2026-08-05");
                    }
                    else
                    {
                        prop.Value.Example = new Microsoft.OpenApi.Any.OpenApiString("2026-08-01");
                    }
                }
            }
        }
    }
}
