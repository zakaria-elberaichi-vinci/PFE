using System.Net;
using System.Text.Json;
using PFE.Context;
using PFE.Helpers;
using PFE.Models;
using static PFE.Helpers.JsonValidation;

namespace PFE.Services
{
    public class OdooClient
    {
        private readonly HttpClient _httpClient;
        public readonly SessionContext session;
        private readonly CookieContainer _cookies;
        private readonly string _baseUrl = "/web/dataset/call_kw";
        public OdooClient(HttpClient httpClient, SessionContext session, CookieContainer cookie)
        {
            _httpClient = httpClient;
            this.session = session;
            _cookies = cookie;
        }

        private void EnsureAuthenticated()
        {
            if (!session.Current.IsAuthenticated)
                throw new InvalidOperationException("Utilisateur non authentifié.");
        }

        private void EnsureIsManager()
        {
            if (session.Current.IsAuthenticated && !session.Current.IsManager)
                throw new InvalidOperationException("Utilisateur n'est pas un manager.");
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
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("error", out _))
                    return false;

                if (root.TryGetProperty("result", out JsonElement result) &&
                    result.ValueKind == JsonValueKind.Object &&
                    result.TryGetProperty("uid", out JsonElement uidEl) &&
                    uidEl.ValueKind == JsonValueKind.Number)
                {
                    int uid = uidEl.GetInt32();

                    bool isAuthenticated = uid > 0;
                    session.Current = session.Current with
                    {
                        IsAuthenticated = isAuthenticated,
                        UserId = isAuthenticated ? uid : null,
                        IsManager = false,
                        EmployeeId = null
                    };

                    if (isAuthenticated)
                    {

                        Task<bool> isManagerTask = UserIsManagerAsync();
                        Task<int> employeeIdTask = GetEmployeeIdAsync();

                        try
                        {
                            await Task.WhenAll(isManagerTask, employeeIdTask);

                            bool isManager = await isManagerTask;
                            int employeeId = await employeeIdTask;

                            session.Current = session.Current with { IsManager = isManager, EmployeeId = employeeId };
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Erreur lors des requêtes parallèles: {ex}");
                        }
                    }

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
                new object[] { "user_id", "=", session.Current.UserId!.Value }
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

            HttpResponseMessage res = await _httpClient.PostAsync(_baseUrl, BuildJsonContent(payload));
            string text = await res.Content.ReadAsStringAsync();

            res.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("error", out JsonElement err))
                throw new Exception("Erreur Odoo : " + err.ToString());

            if (!root.TryGetProperty("result", out JsonElement result))
                throw new Exception("Réponse Odoo inattendue : " + text);

            if (result.ValueKind == JsonValueKind.False)
                return null;

            if (result.ValueKind != JsonValueKind.Array)
                throw new Exception("Réponse Odoo inattendue (result n'est pas un tableau) : " + text);

            if (result.GetArrayLength() == 0)
                return null;

            JsonElement emp = result[0];

            int id = emp.TryGetProperty("id", out JsonElement idEl) && idEl.TryGetInt32(out int idVal) ? idVal : 0;
            string name = emp.TryGetProperty("name", out JsonElement nameEl) && nameEl.ValueKind == JsonValueKind.String ? nameEl.GetString()! : string.Empty;
            string? workEmail = emp.TryGetProperty("work_email", out JsonElement emailEl) && emailEl.ValueKind == JsonValueKind.String ? emailEl.GetString() : null;
            string? jobTitle = emp.TryGetProperty("job_title", out JsonElement jobEl) && jobEl.ValueKind == JsonValueKind.String ? jobEl.GetString() : null;

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
            if (emp.TryGetProperty("department_id", out JsonElement deptEl))
            {
                (deptId, deptName) = ParseMany2One(deptEl);
            }

            int? mgrId = null; string? mgrName = null;
            if (emp.TryGetProperty("parent_id", out JsonElement mgrEl))
            {
                (mgrId, mgrName) = ParseMany2One(mgrEl);
            }

            return new UserProfile(
                id,
                name,
                workEmail,
                jobTitle,
                deptId,
                deptName,
                mgrId,
                mgrName
            );

        }

        public async Task<List<Leave>> GetLeavesAsync(int? yearRequest = null, string? stateRequest = null)
        {

            List<object> domain = new List<object>();

            if (yearRequest.HasValue)
            {
                int year = yearRequest.Value;
                DateTime startOfYear = new DateTime(year, 1, 1);
                DateTime endOfYear = new DateTime(year, 12, 31);

                domain.Add(new object[] { "date_to", ">=", startOfYear.ToString("yyyy-MM-dd") });
                domain.Add(new object[] { "date_from", "<=", endOfYear.ToString("yyyy-MM-dd") });
            }

            if (!string.IsNullOrWhiteSpace(stateRequest))
            {
                domain.Add(new object[] { "state", "=", stateRequest });
            }
            else
            {
                domain.Add(new object[] { "state", "in", new object[] { "draft", "confirm", "validate1", "validate", "refuse", "cancel" } });
            }

            string[] fields = new string[]
            {
                "holiday_status_id",
                "state",
                "request_date_to",
                "request_date_from",
                "date_from",
                "date_to",
                "number_of_days"
            };

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
                domain.ToArray(),
                fields
                    },
                    kwargs = new
                    {
                        context = new
                        {
                            prefetch_fields = false
                        },
                        limit = 0,
                        order = "request_date_from asc"
                    }
                },
                id = 2
            };


            HttpResponseMessage res = await _httpClient.PostAsync(_baseUrl, BuildJsonContent(payload));
            string text = await res.Content.ReadAsStringAsync();

            res.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("error", out JsonElement err))
                throw new Exception("Erreur Odoo : " + err.ToString());

            if (!root.TryGetProperty("result", out JsonElement result))
                throw new Exception("Réponse Odoo inattendue : " + text);

            if (result.ValueKind == JsonValueKind.False)
                return new List<Leave>();

            if (result.ValueKind != JsonValueKind.Array)
                throw new Exception("Réponse Odoo inattendue (result n'est pas un tableau) : " + text);

            List<Leave> list = new List<Leave>();

            foreach (JsonElement item in result.EnumerateArray())
            {
                string type = "";
                if (item.TryGetProperty("holiday_status_id", out JsonElement holidayStatusEl) &&
                    holidayStatusEl.ValueKind == JsonValueKind.Array &&
                    holidayStatusEl.GetArrayLength() > 1)
                {
                    JsonElement nameElement = holidayStatusEl[1];
                    if (nameElement.ValueKind == JsonValueKind.String)
                        type = nameElement.GetString() ?? "";
                }

                if (!item.TryGetProperty("date_from", out JsonElement fromEl) ||
                    fromEl.ValueKind != JsonValueKind.String)
                    throw new Exception("Erreur sur la date de début.");

                DateTime startDate = DateTime.Parse(fromEl.GetString());

                if (!item.TryGetProperty("date_to", out JsonElement toEl) ||
                    toEl.ValueKind != JsonValueKind.String)
                    throw new Exception("Erreur sur la date de fin.");

                DateTime endDate = DateTime.Parse(toEl.GetString());

                string state = "";
                if (item.TryGetProperty("state", out JsonElement stateEl) &&
                    stateEl.ValueKind == JsonValueKind.String)
                    state = stateEl.GetString() ?? "";

                if (!item.TryGetProperty("number_of_days", out JsonElement daysEl) ||
                    daysEl.ValueKind != JsonValueKind.Number)
                    throw new Exception("Erreur sur les jours");

                int days = (int)Math.Round(daysEl.GetDouble());

                list.Add(new Leave(
                    LeaveTypeHelper.Translate(type),
                    startDate,
                    endDate,
                    LeaveStatusHelper.StateToFrench(state),
                    days
                    ));
            }

            return list;
        }

        public void Logout()
        {
            session.Current = session.Current with
            {
                IsAuthenticated = false,
                UserId = null,
                IsManager = false,
                EmployeeId = null,
            };

            Uri baseUri = _httpClient.BaseAddress!;
            string domain = baseUri.Host;

            foreach (Cookie c in _cookies.GetCookies(baseUri))
            {
                c.Expires = DateTime.UnixEpoch;
                c.Expired = true;
            }

            _cookies.Add(new Cookie("session_id", "", "/", domain)
            {
                Expires = DateTime.UtcNow.AddDays(-1)
            });

        }
        private static class GroupIds
        {
            public const int Administrator = 21;
            public const int AllApprover = 20;
            public const int TimeOffResponsible = 19;
        }
        private async Task<bool> UserIsManagerAsync()
        {
            EnsureAuthenticated();

            var payload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    model = "res.groups",
                    method = "read",
                    args = new object[] { new int[] { GroupIds.Administrator, GroupIds.AllApprover, GroupIds.TimeOffResponsible },
                        new string[] { "all_user_ids" } },
                    kwargs = new { }
                },
                id = 999
            };

            HttpResponseMessage res = await _httpClient.PostAsync(_baseUrl, BuildJsonContent(payload));
            res.EnsureSuccessStatusCode();

            string text = await res.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(text);

            if (!doc.RootElement.TryGetProperty("result", out JsonElement result) || result.ValueKind != JsonValueKind.Array)
                return false;

            foreach (JsonElement grp in result.EnumerateArray())
            {
                if (grp.TryGetProperty("all_user_ids", out JsonElement usersEl) && usersEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement u in usersEl.EnumerateArray())
                    {
                        if (u.ValueKind == JsonValueKind.Number && u.GetInt32() == session.Current.UserId)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;

        }

        public async Task<List<LeaveToApprove>> GetLeavesToApproveAsync()
        {
            EnsureIsManager();

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
                new object[]
                {
                    new object[] { "state", "in", new object[]{"confirm", "validate1"} }
                }
                    },
                    kwargs = new
                    {
                        fields = new[]
                        {
                            "employee_id",
                            "holiday_status_id",
                            "number_of_days",
                            "date_from",
                            "date_to",
                            "state",
                            "name",
                            "can_validate",
                            "can_refuse"
                        },
                        limit = 1000,
                        order = "request_date_from"
                    }
                },
                id = 1
            };

            HttpResponseMessage res = await _httpClient.PostAsync(_baseUrl, BuildJsonContent(payload));

            string text = await res.Content.ReadAsStringAsync();
            res.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("error", out var _))
                throw new Exception($"Erreur: {text}");

            if (!root.TryGetProperty("result", out JsonElement resEl) && resEl.ValueKind != JsonValueKind.Array)
                return new List<LeaveToApprove>();

            List<LeaveToApprove> leaves = new List<LeaveToApprove>();

            foreach (JsonElement item in resEl.EnumerateArray())
            {
                int id = item.GetProperty("id").GetInt32();

                string employeeName = item.GetProperty("employee_id")[1].GetString() ?? "Employé inconnu";

                string type = LeaveTypeHelper.Translate(item.GetProperty("holiday_status_id")[1].GetString());

                DateTime startDate = DateTime.Parse(item.GetProperty("date_from").GetString());
                DateTime endDate = DateTime.Parse(item.GetProperty("date_to").GetString());

                int days = (int)Math.Round(item.GetProperty("number_of_days").GetDouble());

                string status = item.GetProperty("state").GetString() ?? "";

                string reason = item.TryGetProperty("name", out JsonElement nameProp) && nameProp.ValueKind == JsonValueKind.String
                    ? nameProp.GetString()!
                    : "Aucune raison";

                bool canValidate = item.TryGetProperty("can_validate", out JsonElement cv) && cv.GetBoolean();

                bool canRefuse = item.TryGetProperty("can_refuse", out JsonElement cr) && cr.GetBoolean();

                leaves.Add(new LeaveToApprove
                (
                    id,
                    employeeName,
                    type,
                    startDate,
                    endDate,
                    days,
                    status,
                    reason,
                    canValidate,
                    canRefuse
                ));
            }

            return leaves;
        }

        public async Task ApproveLeaveAsync(int leaveId)
        {
            EnsureIsManager();

            var payload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    model = "hr.leave",
                    method = "action_approve",
                    args = new object[] { leaveId },
                    kwargs = new { }
                },
                id = 0
            };

            HttpResponseMessage res = await _httpClient.PostAsync(_baseUrl, BuildJsonContent(payload));
            res.EnsureSuccessStatusCode();
        }

        public async Task RefuseLeaveAsync(int leaveId)
        {
            EnsureIsManager();

            var payload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    model = "hr.leave",
                    method = "action_refuse",
                    args = new object[] { leaveId },
                    kwargs = new { }
                },
                id = 0
            };

            HttpResponseMessage res = await _httpClient.PostAsync(_baseUrl, BuildJsonContent(payload));
            res.EnsureSuccessStatusCode();
        }

        private async Task<int> GetEmployeeIdAsync()
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
                new object[] { "user_id", "=", session.Current.UserId!.Value }
            },
            new string[]
            {
                "id",
            }
                    },
                    kwargs = new
                    {
                        limit = 1,
                    }
                },
                id = 2
            };

            HttpResponseMessage res = await _httpClient.PostAsync(_baseUrl, BuildJsonContent(payload));
            string text = await res.Content.ReadAsStringAsync();
            res.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(text);

            if (!doc.RootElement.TryGetProperty("result", out JsonElement resEl))
                throw new InvalidOperationException("Réponse Odoo invalide: 'result' manquant.");

            if (resEl.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Réponse Odoo inattendue: 'result' n'est pas un tableau.");

            if (resEl.GetArrayLength() == 0)
                throw new InvalidOperationException("Aucun employé associé à cet utilisateur (user_id).");

            JsonElement first = resEl[0];

            if (!first.TryGetProperty("id", out JsonElement idElement) || idElement.ValueKind != JsonValueKind.Number)
                throw new InvalidOperationException("Champ 'id' introuvable ou invalide dans le résultat.");

            int employeeId = idElement.GetInt32();
            return employeeId;

        }
        public async Task<bool> HasOverlappingLeaveAsync(DateTime startDate, DateTime endDate)
        {
            EnsureAuthenticated();

            string start = startDate.ToString("yyyy-MM-dd");
            string end = endDate.ToString("yyyy-MM-dd");

            object[] domain = new object[]
            {
        new object[] { "employee_id", "=", session.Current.EmployeeId!.Value },
        new object[] { "state", "not in", new object[] { "cancel", "refuse" } },
        new object[] { "date_from", "<=", end },
        new object[] { "date_to", ">=", start }
            };

            var payload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    model = "hr.leave",
                    method = "search_count",
                    args = new object[] { domain },
                    kwargs = new { }
                },
                id = 801
            };

            HttpResponseMessage res = await _httpClient.PostAsync(_baseUrl, BuildJsonContent(payload));
            string text = await res.Content.ReadAsStringAsync();
            res.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("result", out JsonElement countEl) || countEl.ValueKind != JsonValueKind.Number)
                throw new InvalidOperationException("Réponse Odoo inattendue pour search_count sur hr.leave.");

            int count = countEl.GetInt32();
            return count > 0;
        }

        public async Task<HashSet<int>> GetAllowedLeaveTypeIdsAsync()
        {

            object[] domain = new object[]
            {
    // Filtres de base
    new object[] { "active", "=", true },
    new object[] { "requires_allocation", "=", false },
            };

            var payload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    model = "hr.leave.type",
                    method = "search_read",
                    args = new object[]
                    {
                domain,
                new string[] { "id", "name", "requires_allocation" }
                    },
                    kwargs = new { limit = 1000 }
                },
                id = 900
            };

            HttpResponseMessage res = await _httpClient.PostAsync(_baseUrl, BuildJsonContent(payload));
            string text = await res.Content.ReadAsStringAsync();
            res.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("result", out JsonElement resultEl) || resultEl.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Réponse Odoo invalide: 'result' manquant ou non-array pour hr.leave.allocation.");

            HashSet<int> allowed = new HashSet<int>();
            foreach (JsonElement row in resultEl.EnumerateArray())
            {
                if (row.TryGetProperty("id", out JsonElement hs) &&
                    hs.ValueKind == JsonValueKind.Number)
                {
                    allowed.Add(hs.GetInt32());
                }
            }

            return allowed;
        }

        public async Task<(int totalAlloc, int totalLeaves)> GetNumberTimeOffAsync(int? yearRequested = null)
        {
            int year = yearRequested ?? DateTime.Today.Year;

            string dateFrom = new DateTime(year, 1, 1).ToString("yyyy-MM-dd");
            string dateTo = new DateTime(year, 12, 31).ToString("yyyy-MM-dd");

            int employeeId = session.Current.EmployeeId!.Value;

            object[] argsAlloc = new object[]
            {
    new object[]
    {
        new object[] { "employee_id", "=", employeeId },
        new object[] { "state", "=", "validate" },
        new object[] { "date_from", "<=", dateTo },

        // OR à plat dans la liste (pas imbriqué)
        "|",
        new object[] { "date_to", "=", false },
        new object[] { "date_to", ">=", dateFrom }
    },
    new object[] { "number_of_days:sum" },
    new object[] { "holiday_status_id" }
            };


            object[] argsLeaves = new object[]
            {
        new object[]
        {
            new object[] {"employee_id", "=", employeeId},
            new object[] {"state", "in", new object[] { "validate", "validate1" } },
            new object[] {"date_from", ">=", dateFrom},
            new object[] {"date_to",   "<=", dateTo},
        },
        new object[] { "number_of_days:sum" },
        new object[] { "holiday_status_id" }
            };

            var payloadAlloc = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    model = "hr.leave.allocation",
                    method = "read_group",
                    args = argsAlloc,
                    kwargs = new { }
                },
                id = 1
            };

            var payloadLeaves = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    model = "hr.leave",
                    method = "read_group",
                    args = argsLeaves,
                    kwargs = new { }
                },
                id = 2
            };

            Task<HttpResponseMessage> allocTask = _httpClient.PostAsync(_baseUrl, BuildJsonContent(payloadAlloc));
            Task<HttpResponseMessage> leavesTask = _httpClient.PostAsync(_baseUrl, BuildJsonContent(payloadLeaves));

            await Task.WhenAll(allocTask, leavesTask);

            HttpResponseMessage allocRes = await allocTask;
            HttpResponseMessage leavesRes = await leavesTask;

            allocRes.EnsureSuccessStatusCode();
            leavesRes.EnsureSuccessStatusCode();

            string allocJson = await allocRes.Content.ReadAsStringAsync();
            string leavesJson = await leavesRes.Content.ReadAsStringAsync();

            double totalAllocDays = SumReadGroupNumberOfDays(allocJson);
            double totalLeavesDays = SumReadGroupNumberOfDays(leavesJson);

            return ((int)Math.Round(totalAllocDays), (int)Math.Round(totalLeavesDays));

        }

        public async Task<int> CreateLeaveRequestAsync(
         int leaveTypeId,
         DateTime startDate,
         DateTime endDate,
         string reason)
        {

            EnsureAuthenticated();

            Dictionary<string, object> values = new()
            {
                ["employee_id"] = session.Current.EmployeeId!.Value,
                ["holiday_status_id"] = leaveTypeId,
                ["date_from"] = startDate.ToString("yyyy-MM-dd"),
                ["date_to"] = endDate.ToString("yyyy-MM-dd"),
                ["name"] = string.IsNullOrWhiteSpace(reason) ? "Demande de congé" : reason
            };

            var payload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    model = "hr.leave",
                    method = "create",
                    args = new object[] { values },
                    kwargs = new { }
                },
                id = 1
            };

            HttpResponseMessage res = await _httpClient.PostAsync(_baseUrl, BuildJsonContent(payload));
            string text = await res.Content.ReadAsStringAsync();
            res.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("error", out JsonElement errEl))
                throw new Exception("Erreur Odoo : " + errEl.ToString());

            if (!root.TryGetProperty("result", out JsonElement resEl))
                throw new Exception("Réponse Odoo inattendue : " + text);

            if (resEl.ValueKind != JsonValueKind.Number)
                throw new Exception("Réponse Odoo inattendue (result n'est pas un entier) : " + text);

            return resEl.GetInt32();
        }
    }
}

