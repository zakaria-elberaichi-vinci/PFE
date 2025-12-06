using System.Text;
using System.Text.Json;
using PFE.Helpers;
using PFE.Models;

namespace PFE.Services
{
    public class OdooClient
    {
        private readonly HttpClient _httpClient;
        public int? UserId { get; private set; }
        public bool IsAuthenticated { get; private set; }

        public OdooClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
            UserId = null;
            IsAuthenticated = false;
        }

        private void EnsureAuthenticated()
        {
            if (!IsAuthenticated || UserId is null)
                throw new InvalidOperationException("Utilisateur non authentifié.");
        }

        private static StringContent BuildJsonContent(object payload)
        {
            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });
            return new StringContent(json, Encoding.UTF8, "application/json");
        }


        public async Task<bool> LoginAsync(
            string login, string password)
        {
            var payload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    db = OdooConfigService.OdooDb,
                    login,
                    password
                },
                id = 1
            };

            HttpResponseMessage res;
            try
            {
                res = await _httpClient.PostAsync("/web/session/authenticate", BuildJsonContent(payload));
            }
            catch (HttpRequestException)
            {
                return false;
            }

            string text = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
                return false;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(text);
                var root = doc.RootElement;

                if (root.TryGetProperty("error", out _))
                    return false;

                if (root.TryGetProperty("result", out var result) &&
                    result.ValueKind == JsonValueKind.Object &&
                    result.TryGetProperty("uid", out var uidEl) &&
                    uidEl.ValueKind == JsonValueKind.Number)
                {
                    int uid = uidEl.GetInt32();

                    IsAuthenticated = uid > 0;
                    UserId = IsAuthenticated ? uid : null;

                    return uid > 0;

                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task<UserProfile> GetUserInfosAsync()
        {
            EnsureAuthenticated();

            var payload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    model = "hr.employee.public",
                    method = "search_read",
                    args = new object[]
                    {
            new object[]
            {
                new object[] { "user_id", "=", UserId!.Value }
            },
            new string[]
            {
                "name",
                "work_email",
                "job_title",
                "department_id",
                "parent_id"
            }
                    },
                    kwargs = new
                    {
                        limit = 80,
                        context = new { prefetch_fields = false }
                    }
                },
                id = 2
            };

            var res = await _httpClient.PostAsync("/web/dataset/call_kw", BuildJsonContent(payload));
            string text = await res.Content.ReadAsStringAsync();

            res.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err))
                throw new Exception("Erreur Odoo : " + err.ToString());

            if (!root.TryGetProperty("result", out var result))
                throw new Exception("Réponse Odoo inattendue : " + text);

            if (result.ValueKind == JsonValueKind.False)
                return null;

            if (result.ValueKind != JsonValueKind.Array)
                throw new Exception("Réponse Odoo inattendue (result n'est pas un tableau) : " + text);

            if (result.GetArrayLength() == 0)
                return null;

            var emp = result[0];

            int id = emp.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out int idVal) ? idVal : 0;
            string name = emp.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String ? nameEl.GetString()! : string.Empty;
            string? workEmail = emp.TryGetProperty("work_email", out var emailEl) && emailEl.ValueKind == JsonValueKind.String ? emailEl.GetString() : null;
            string? jobTitle = emp.TryGetProperty("job_title", out var jobEl) && jobEl.ValueKind == JsonValueKind.String ? jobEl.GetString() : null;

            (int? Id, string? Name) ParseMany2One(JsonElement el)
            {
                if (el.ValueKind != JsonValueKind.Array) return (null, null);
                int? m2oId = null;
                string? m2oName = null;
                int len = el.GetArrayLength();

                if (len >= 1 && el[0].ValueKind == JsonValueKind.Number && el[0].TryGetInt32(out int i)) m2oId = i;
                if (len >= 2 && el[1].ValueKind == JsonValueKind.String) m2oName = el[1].GetString();
                return (m2oId, m2oName);
            }

            int? deptId = null; string? deptName = null;
            if (emp.TryGetProperty("department_id", out var deptEl))
            {
                (deptId, deptName) = ParseMany2One(deptEl);
            }

            int? mgrId = null; string? mgrName = null;
            if (emp.TryGetProperty("parent_id", out var mgrEl))
            {
                (mgrId, mgrName) = ParseMany2One(mgrEl);
            }

            UserProfile profile = new UserProfile(
                id,
                name,
                workEmail,
                jobTitle,
                deptId,
                deptName,
                mgrId,
                mgrName
            );

            return profile;

        }

        public async Task<List<Leave>> GetLeavesAsync()
        {
            var payload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    model = "hr.leave",
                    method = "search_read",
                    args = new object[]
                        {
                new object[] { },
                new string[]
                {
                    "name",
                    "state",
                    "request_date_from",
                    "request_date_to"
                }
                        },
                    kwargs = new
                    {
                        context = new
                        {
                            prefetch_fields = false
                        }
                    }
                },
                id = 2
            };

            var res = await _httpClient.PostAsync("/web/dataset/call_kw", BuildJsonContent(payload));
            string text = await res.Content.ReadAsStringAsync();

            res.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err))
                throw new Exception("Erreur Odoo : " + err.ToString());

            if (!root.TryGetProperty("result", out var result))
                throw new Exception("Réponse Odoo inattendue : " + text);

            if (result.ValueKind == JsonValueKind.False)
                return new List<Leave>();

            if (result.ValueKind != JsonValueKind.Array)
                throw new Exception("Réponse Odoo inattendue (result n'est pas un tableau) : " + text);

            List<Leave> list = new List<Leave>();

            foreach (var item in result.EnumerateArray())
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

                list.Add(new Leave(
                    string.IsNullOrWhiteSpace(name) ? "Congé" : name,
                    $"{from} → {to}",
                    LeaveStatusHelper.StateToFrench(state)
                    ));
            }

            return list;
        }

        public async Task LogoutAsync()
        {
            var payload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new { },
                id = 1
            };

            HttpResponseMessage res;
            try
            {
                res = await _httpClient.PostAsync("/web/session/logout", BuildJsonContent(payload));

                IsAuthenticated = false;
                UserId = null;
            }
            catch (HttpRequestException)
            {
                IsAuthenticated = false;
                UserId = null;
            }
        }

        public async Task<bool> UserIsLeaveManagerAsync()
        {
            EnsureAuthenticated();

            var payload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    model = "res.users",
                    method = "read",
                    args = new object[] {
                new int[] { UserId.Value },
                new string[] { "groups_id" }
            }
                },
                id = 10
            };

            var res = await _httpClient.PostAsync("/web/dataset/call_kw", BuildJsonContent(payload));
            string text = await res.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (!root.TryGetProperty("result", out var result))
                return false;

            var arr = result[0];
            var groups = arr.GetProperty("groups_id");

            foreach (var g in groups.EnumerateArray())
            {
                int groupId = g.GetInt32();
                if (groupId == 19) // TON ID de groupe "Time Off Responsible"
                    return true;
            }

            return false;
        }


    }
}

