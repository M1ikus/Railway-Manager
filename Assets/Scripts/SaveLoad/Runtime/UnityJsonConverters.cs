using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RailwayManager.SaveLoad
{
    /// <summary>Vector3 → {x,y,z} bez normalized/magnitude (które loop'ują
    /// rekursywnie przez default Newtonsoft serialization na property structach).</summary>
    public class UnityVector3Converter : JsonConverter<Vector3>
    {
        public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x"); writer.WriteValue(value.x);
            writer.WritePropertyName("y"); writer.WriteValue(value.y);
            writer.WritePropertyName("z"); writer.WriteValue(value.z);
            writer.WriteEndObject();
        }

        public override Vector3 ReadJson(JsonReader reader, Type objectType, Vector3 existingValue,
                                         bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return Vector3.zero;
            var jo = JObject.Load(reader);
            return new Vector3(
                jo.Value<float?>("x") ?? 0f,
                jo.Value<float?>("y") ?? 0f,
                jo.Value<float?>("z") ?? 0f);
        }
    }

    /// <summary>Vector2 → {x,y}. Ten sam problem co Vector3.normalized.</summary>
    public class UnityVector2Converter : JsonConverter<Vector2>
    {
        public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x"); writer.WriteValue(value.x);
            writer.WritePropertyName("y"); writer.WriteValue(value.y);
            writer.WriteEndObject();
        }

        public override Vector2 ReadJson(JsonReader reader, Type objectType, Vector2 existingValue,
                                         bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return Vector2.zero;
            var jo = JObject.Load(reader);
            return new Vector2(
                jo.Value<float?>("x") ?? 0f,
                jo.Value<float?>("y") ?? 0f);
        }
    }

    /// <summary>Vector2Int → {x,y}. Newtonsoft sam by sobie poradził (bez .normalized),
    /// ale podajemy jawny format dla spójności z manualnym JObject build w DepotSavable.</summary>
    public class UnityVector2IntConverter : JsonConverter<Vector2Int>
    {
        public override void WriteJson(JsonWriter writer, Vector2Int value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x"); writer.WriteValue(value.x);
            writer.WritePropertyName("y"); writer.WriteValue(value.y);
            writer.WriteEndObject();
        }

        public override Vector2Int ReadJson(JsonReader reader, Type objectType, Vector2Int existingValue,
                                            bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return Vector2Int.zero;
            var jo = JObject.Load(reader);
            return new Vector2Int(
                jo.Value<int?>("x") ?? 0,
                jo.Value<int?>("y") ?? 0);
        }
    }

    /// <summary>Bootstrap globalnych converterów Unity dla Newtonsoft.Json.
    ///
    /// Rejestrujemy przez <see cref="JsonConvert.DefaultSettings"/>, więc działa
    /// dla każdego <c>JArray.FromObject(...)</c> / <c>ToObject&lt;T&gt;()</c> w
    /// modułach SaveLoad bez zmiany ich kodu. Bez tego DepotSavable wybucha na
    /// <c>Self referencing loop ... Position.normalized</c>.
    ///
    /// **Merge zamiast nadpisania**: jeśli inny system już ustawił `JsonConvert.DefaultSettings`
    /// (np. zewnętrzny config loader z własnymi ContractResolver/NullValueHandling),
    /// zachowujemy jego ustawienia i DODAJEMY nasze converter'y zamiast zastąpić wszystko.
    /// Wcześniej assignowanie `DefaultSettings = () => new ...` znikało settings które
    /// inni callerzy ustawili wcześniej (race ze static init order).</summary>
    public static class UnityJsonConvertersBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            var existing = JsonConvert.DefaultSettings;
            JsonConvert.DefaultSettings = () =>
            {
                var settings = existing?.Invoke() ?? new JsonSerializerSettings();
                if (settings.Converters == null)
                    settings.Converters = new List<JsonConverter>();
                AddIfMissing<UnityVector3Converter>(settings.Converters);
                AddIfMissing<UnityVector2Converter>(settings.Converters);
                AddIfMissing<UnityVector2IntConverter>(settings.Converters);
                return settings;
            };
        }

        private static void AddIfMissing<T>(IList<JsonConverter> list) where T : JsonConverter, new()
        {
            foreach (var c in list)
                if (c is T) return;
            list.Add(new T());
        }
    }
}
