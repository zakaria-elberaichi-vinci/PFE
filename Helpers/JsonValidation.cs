using System.Text;
using System.Text.Json;

namespace PFE.Helpers
{
    public class JsonValidation
    {
        public static StringContent BuildJsonContent(object payload)
        {
            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        public static double SumReadGroupNumberOfDays(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("error", out JsonElement _))
                return 0.0;

            if (!root.TryGetProperty("result", out JsonElement resultElem) || resultElem.ValueKind != JsonValueKind.Array)
                return 0.0;

            double sum = 0.0;

            foreach (JsonElement row in resultElem.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;

                if (row.TryGetProperty("number_of_days", out JsonElement v))
                {
                    sum += CoerceToDouble(v);
                }
            }

            return sum;
        }
        private static double CoerceToDouble(JsonElement elem)
        {
            return elem.ValueKind switch
            {
                JsonValueKind.Number => elem.TryGetDouble(out double d) ? d : 0.0,
                JsonValueKind.String => double.TryParse(elem.GetString(), System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out double dv) ? dv : 0.0,
                JsonValueKind.Null => 0.0,
                _ => 0.0
            };
        }
    }
}
