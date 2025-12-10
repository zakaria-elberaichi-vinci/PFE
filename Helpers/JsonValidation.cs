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
        public static Dictionary<int, double> ParseLeavesByType(string json)
        {
            Dictionary<int, double> result = new Dictionary<int, double>();

            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("result", out JsonElement resultElem)
                || resultElem.ValueKind != JsonValueKind.Array)
                return result;

            foreach (JsonElement rec in resultElem.EnumerateArray())
            {
                int? typeId = null;
                if (rec.TryGetProperty("holiday_status_id", out JsonElement hs))
                {
                    if (hs.ValueKind == JsonValueKind.Array && hs.GetArrayLength() >= 1)
                    {
                        if (hs[0].ValueKind == JsonValueKind.Number && hs[0].TryGetInt32(out var idVal))
                            typeId = idVal;
                    }
                    else if (hs.ValueKind == JsonValueKind.Number && hs.TryGetInt32(out var idVal2))
                    {
                        typeId = idVal2;
                    }
                }

                if (typeId is null)
                    continue;

                double sum = 0;
                if (rec.TryGetProperty("number_of_days", out JsonElement nod))
                {
                    if (nod.ValueKind == JsonValueKind.Number)
                        sum = nod.GetDouble();
                }
                result[typeId.Value] = sum;
            }

            return result;
        }

    }
}
