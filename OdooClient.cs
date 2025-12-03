using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PFE
{
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

        /// <summary>
        /// Authentifie l'utilisateur via JSON-RPC Odoo.
        /// Retourne (success, rawResponse) pour faciliter le debug.
        /// </summary>
        public async Task<(bool Success, string RawResponse)> LoginAsync(
            string db, string login, string password)
        {
            // IMPORTANT : endpoint JSON-RPC = /jsonrpc (sans /odoo)
            var url = $"{_baseUrl}/jsonrpc";

            var payload = new
            {
                jsonrpc = "2.0",
                method = "call",
                // /jsonrpc n'utilise PAS de CSRF, c'est du JSON pur
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
                return (false, $"Erreur réseau : {ex.Message}");
            }

            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return (false, $"HTTP {response.StatusCode} : {responseString}");
            }

            try
            {
                using var doc = JsonDocument.Parse(responseString);
                var root = doc.RootElement;

                if (root.TryGetProperty("result", out var resultElement))
                {
                    if (resultElement.ValueKind == JsonValueKind.Number)
                    {
                        int userId = resultElement.GetInt32();
                        return (userId > 0, responseString);
                    }

                    if (resultElement.ValueKind == JsonValueKind.False)
                    {
                        return (false, responseString); // mauvais login / mdp
                    }
                }

                return (false, responseString);
            }
            catch
            {
                // La réponse n'est pas du JSON (ex: HTML) → on renvoie brute
                return (false, responseString);
            }
        }
    }
}
