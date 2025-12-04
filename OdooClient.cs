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
        private readonly string _odooUrl;
        private readonly string _odooDb;
        private readonly int _userId;
        private readonly string _userPassword;
        private readonly HttpClient _client;

        // Tu peux mettre la même valeur que App.OdooUrl
        private const string baseUrl = "https://ipl-pfe-2025-groupe11.odoo.com";

        // 👉 Constructeur utilisé APRÈS login (Dashboard, LeavesPage, LeaveRequestPage…)
        public OdooClient(string odooUrl, string odooDb, int userId, string userPassword)
        {
            _odooUrl = odooUrl;
            _odooDb = odooDb;
            _userId = userId;
            _userPassword = userPassword;
            _client = new HttpClient();
        }

        // 👉 Constructeur utilisé AVANT login (MainPage)
        public OdooClient(string odooUrl)
        {
            _odooUrl = odooUrl;
            _odooDb = string.Empty;
            _userId = 0;
            _userPassword = string.Empty;
            _client = new HttpClient();
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

        // ---------------------------------------------------------
        //  LOGIN
        // ---------------------------------------------------------

        /// <summary>
        /// Authentifie l'utilisateur via JSON-RPC Odoo.
        /// Retourne (success, userId, rawResponse) pour faciliter le debug.
        /// </summary>
        public async Task<(bool Success, int UserId, string RawResponse)> LoginAsync(
            string db, string login, string password)
        {
            var url = $"{baseUrl}/jsonrpc";

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
                response = await _client.PostAsync(url, content);
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

        // ---------------------------------------------------------
        //  LECTURE DES CONGÉS
        // ---------------------------------------------------------

        /// <summary>
        /// Récupère les congés de l'utilisateur courant (_userId) dans Odoo.
        /// Utilise _odooDb, _userId, _userPassword configurés dans le constructeur.
        /// </summary>
        public async Task<List<LeaveRecord>> GetLeavesAsync()
        {
            var url = $"{baseUrl}/jsonrpc";

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
                        _odooDb,
                        _userId,
                        _userPassword,
                        "hr.leave",      // modèle
                        "search_read",   // méthode

                        // domaine : ici vide, tu peux filtrer plus tard par employé
                        new object[]
                        {
                            new object[] { }   // []
                        },

                        // options : champs à lire
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

            var response = await _client.PostAsync(url, content);
            var responseString = await response.Content.ReadAsStringAsync();

            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorElement))
                throw new Exception("Erreur Odoo : " + errorElement.ToString());

            if (!root.TryGetProperty("result", out var resultElement))
                throw new Exception("Réponse Odoo inattendue : " + responseString);

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

        // ---------------------------------------------------------
        //  CRÉATION D’UNE DEMANDE DE CONGÉ
        // ---------------------------------------------------------

        /// <summary>
        /// Crée une demande de congé hr.leave dans Odoo.
        /// Retourne l'identifiant de l'enregistrement créé.
        /// </summary>
        /// 
        // Récupère l'employé lié à l'utilisateur courant (_userId)
        private async Task<int> GetCurrentEmployeeIdAsync()
        {
            var url = $"{baseUrl}/jsonrpc";

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
                _odooDb,
                _userId,
                _userPassword,
                "hr.employee",
                "search_read",

                // Domaine : hr.employee avec user_id = _userId
                new object[]
                {
                    new object[] { "user_id", "=", _userId }
                },

                // Options
                new
                {
                    fields = new[] { "id", "name" },
                    limit = 1
                }
                    }
                },
                id = 10
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _client.PostAsync(url, content);
            var responseString = await response.Content.ReadAsStringAsync();

            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;

            if (!root.TryGetProperty("result", out var resultElement) ||
                resultElement.ValueKind != JsonValueKind.Array)
            {
                return 0;
            }

            var first = resultElement.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Object)
                return 0;

            if (first.TryGetProperty("id", out var idEl) &&
                idEl.ValueKind == JsonValueKind.Number)
            {
                return idEl.GetInt32();
            }

            return 0;
        }

        /// <summary>
        /// Crée une demande de congé hr.leave dans Odoo
        /// pour l'utilisateur courant (_userId / _userPassword).
        /// </summary>
        public async Task<int> CreateLeaveRequestAsync(
     int leaveTypeId,
     DateTime startDate,
     DateTime endDate,
     string reason)
        {
            // ⚠ TEMPORAIRE : forcer Maya Dévers (employee_id = 7)
            int employeeId = 7;

            var url = $"{baseUrl}/jsonrpc";

            var values = new Dictionary<string, object>
            {
                ["employee_id"] = employeeId,
                ["holiday_status_id"] = leaveTypeId,
                ["request_date_from"] = startDate.ToString("yyyy-MM-dd"),
                ["request_date_to"] = endDate.ToString("yyyy-MM-dd"),
                ["name"] = string.IsNullOrWhiteSpace(reason)
                                            ? "Demande de congé"
                                            : reason
            };

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
                _odooDb,
                _userId,
                _userPassword,
                "hr.leave",
                "create",
                new object[]
                {
                    values
                }
                    }
                },
                id = 3
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _client.PostAsync(url, content);
            var responseString = await response.Content.ReadAsStringAsync();

            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorElement))
                throw new Exception("Erreur Odoo : " + errorElement.ToString());

            if (!root.TryGetProperty("result", out var resultElement))
                throw new Exception("Réponse Odoo inattendue : " + responseString);

            if (resultElement.ValueKind != JsonValueKind.Number)
                throw new Exception("Réponse Odoo inattendue (result n'est pas un entier) : " + responseString);

            return resultElement.GetInt32();
        }
    }
}