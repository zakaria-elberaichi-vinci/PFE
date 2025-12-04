using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;


namespace PFE
{

    public class LeaveRecord
    {
        public string Name { get; set; } = "";
        public string Period { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public class OdooClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        // baseUrl = "https://ipl-pfe-2025-groupe11.odoo.com"
        public OdooClient(string baseUrl)
        {
            _httpClient = new HttpClient();
            _baseUrl = baseUrl.TrimEnd('/');
        }
        private static string StateToFrench(string state) =>
    state switch
    {
        "draft" => "Brouillon",
        "confirm" => "En attente d'approbation",
        "validate" => "Validé",
        "refuse" => "Refusé",
        "cancel" => "Annulé",
        _ => state
    };

        /// <summary>
        /// Authentifie l'utilisateur via JSON-RPC Odoo.
        /// Retourne (success, rawResponse) pour faciliter le debug.
        /// </summary>
        public async Task<(bool Success, int UserId, string RawResponse)> LoginAsync(
    string db, string login, string password)
        {
            var url = $"{_baseUrl}/jsonrpc";

            var payload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    service = "common",
                    method = "authenticate",
                    args = new object[] { db, login, password, new { } }
                },
                id = 1
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.PostAsync(url, content);
            }
            catch (HttpRequestException ex)
            {
                return (false, 0, $"Erreur réseau : {ex.Message}");
            }

            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return (false, 0, $"HTTP {response.StatusCode} : {responseString}");

            try
            {
                using var doc = JsonDocument.Parse(responseString);
                var root = doc.RootElement;

                if (root.TryGetProperty("result", out var resultElement))
                {
                    if (resultElement.ValueKind == JsonValueKind.Number)
                    {
                        int userId = resultElement.GetInt32();
                        return (userId > 0, userId, responseString);
                    }

                    if (resultElement.ValueKind == JsonValueKind.False)
                    {
                        return (false, 0, responseString); // mauvais login/mdp
                    }
                }

                return (false, 0, responseString);
            }
            catch
            {
                return (false, 0, responseString);
            }
        }

        public async Task<List<LeaveRecord>> GetLeavesAsync(string db, int uid, string password)
        {
            var url = $"{_baseUrl}/jsonrpc";

            var payload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    service = "object",
                    method = "execute_kw",
                    args = new object[]
                    {
                db,
                uid,
                password,
                "hr.leave",      // modèle
                "search_read",   // méthode

                // ⬇⬇⬇ ICI : liste des arguments de search_read
                // 1er argument = domaine []
                new object[]
                {
                    new object[] { }   // domaine vide : []
                },

                // 2e argument = options (fields, limit, etc.)
                new
                {
                    fields = new[]
                    {
                        "name",
                        "state",
                        "request_date_from",
                        "request_date_to"
                    }
                }
                    }
                },
                id = 2
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            var responseString = await response.Content.ReadAsStringAsync();

            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorElement))
                throw new Exception("Erreur Odoo : " + errorElement.ToString());

            if (!root.TryGetProperty("result", out var resultElement))
                throw new Exception("Réponse Odoo inattendue : " + responseString);

            // Odoo peut renvoyer false si pas de droits / pas de données
            if (resultElement.ValueKind == JsonValueKind.False)
                return new List<LeaveRecord>();

            if (resultElement.ValueKind != JsonValueKind.Array)
                throw new Exception("Réponse Odoo inattendue (result n'est pas un tableau) : " + responseString);

            var list = new List<LeaveRecord>();

            foreach (var item in resultElement.EnumerateArray())
            {
                string name = "";
                if (item.TryGetProperty("name", out var nameEl) &&
                    nameEl.ValueKind == JsonValueKind.String)
                    name = nameEl.GetString() ?? "";

                string from = "";
                if (item.TryGetProperty("request_date_from", out var fromEl) &&
                    fromEl.ValueKind == JsonValueKind.String)
                    from = fromEl.GetString() ?? "";

                string to = "";
                if (item.TryGetProperty("request_date_to", out var toEl) &&
                    toEl.ValueKind == JsonValueKind.String)
                    to = toEl.GetString() ?? "";

                string state = "";
                if (item.TryGetProperty("state", out var stateEl) &&
                    stateEl.ValueKind == JsonValueKind.String)
                    state = stateEl.GetString() ?? "";

                list.Add(new LeaveRecord
                {
                    Name = string.IsNullOrWhiteSpace(name) ? "Congé" : name,
                    Period = $"{from} → {to}",
                    Status = StateToFrench(state)
                });
            }

            return list;
        }


    }
}

