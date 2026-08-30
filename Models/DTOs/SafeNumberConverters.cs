using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PickNBook.Api.Models.DTOs
{
    public class SafeDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetDecimal();
            }
            if (reader.TokenType == JsonTokenType.String)
            {
                if (decimal.TryParse(reader.GetString(), out var val))
                    return val;
                return 0m;
            }
            return 0m;
        }

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }

    public class SafeDoubleConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetDouble();
            }
            if (reader.TokenType == JsonTokenType.String)
            {
                if (double.TryParse(reader.GetString(), out var val))
                    return val;
                return 0d;
            }
            return 0d;
        }

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }
    
    public class SafeIntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetInt32();
            }
            if (reader.TokenType == JsonTokenType.String)
            {
                if (int.TryParse(reader.GetString(), out var val))
                    return val;
                return 0;
            }
            return 0;
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }
    
    public class SafeLongConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetInt64();
            }
            if (reader.TokenType == JsonTokenType.String)
            {
                if (long.TryParse(reader.GetString(), out var val))
                    return val;
                return 0L;
            }
            return 0L;
        }

        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }
    
    public class SafeStringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString() ?? string.Empty;
            }
            if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetDecimal().ToString();
            }
            if (reader.TokenType == JsonTokenType.True)
            {
                return "true";
            }
            if (reader.TokenType == JsonTokenType.False)
            {
                return "false";
            }
            if (reader.TokenType == JsonTokenType.Null)
            {
                return string.Empty;
            }
            
            // For objects/arrays, we don't strictly expect string mapping but to be safe:
            using var doc = JsonDocument.ParseValue(ref reader);
            return doc.RootElement.ToString();
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
}
