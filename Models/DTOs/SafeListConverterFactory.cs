using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PickNBook.Api.Models.DTOs
{
    /// <summary>
    /// Safely handles deserializing List<T> when a PHP-based API like SRDV 
    /// occasionally returns an empty string ("") instead of an empty array ([]).
    /// </summary>
    public class SafeListConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            if (!typeToConvert.IsGenericType) return false;
            return typeToConvert.GetGenericTypeDefinition() == typeof(List<>);
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            var itemType = typeToConvert.GetGenericArguments()[0];
            var converterType = typeof(SafeListConverterInner<>).MakeGenericType(itemType);
            return (JsonConverter)Activator.CreateInstance(converterType)!;
        }

        private class SafeListConverterInner<T> : JsonConverter<List<T>>
        {
            public override List<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    reader.GetString(); // Consume string
                    return new List<T>();
                }
                
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    var list = new List<T>();
                    var elementOptions = new JsonSerializerOptions(options);
                    
                    // We must temporarily remove this factory from options 
                    // to avoid infinite loops if T is also a List.
                    // But typically elementOptions won't re-enter unless T = List<T2>.
                    
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        var item = JsonSerializer.Deserialize<T>(ref reader, elementOptions);
                        if (item != null) list.Add(item);
                    }
                    return list;
                }
                
                // If it's an unexpected object or type, consume it and return empty list to prevent crashes
                using var doc = JsonDocument.ParseValue(ref reader);
                return new List<T>();
            }

            public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options)
            {
                JsonSerializer.Serialize(writer, value, options);
            }
        }
    }
}
