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
            {
                throw new InvalidOperationException("Utilisateur non authentifié.");
            }
        }

        private void EnsureIsManager()
        {
            if (session.Current.IsAuthenticated && !session.Current.IsManager)
            {
                throw new InvalidOperationException("Utilisateur n'est pas un manager.");
            }
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
            {
                return false;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(text);
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("error", out _))
                {
                    return false;
                }

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

        public async Task<UserProfile?> GetUserInfosAsync()
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

            _ = res.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("error", out JsonElement err))
            {
                throw new Exception("Erreur Odoo : " + err.ToString());
            }

            if (!root.TryGetProperty("result", out JsonElement result))
            {
                throw new Exception("Réponse Odoo inattendue : " + text);
            }

            if (result.ValueKind == JsonValueKind.False)
            {
                return null;
            }

            if (result.ValueKind != JsonValueKind.Array)
            {
                throw new Exception("Réponse Odoo inattendue (result n'est pas un tableau) : " + text);
            }

            if (result.GetArrayLength() == 0)
            {
                return null;
            }

            JsonElement emp = result[0];

            int id = emp.TryGetProperty("id", out JsonElement idEl) && idEl.TryGetInt32(out int idVal) ? idVal : 0;
            string name = emp.TryGetProperty("name", out JsonElement nameEl) && nameEl.ValueKind == JsonValueKind.String ? nameEl.GetString()! : string.Empty;
            string? workEmail = emp.TryGetProperty("work_email", out JsonElement emailEl) && emailEl.ValueKind == JsonValueKind.String ? emailEl.GetString() : null;
            string? jobTitle = emp.TryGetProperty("job_title", out JsonElement jobEl) && jobEl.ValueKind == JsonValueKind.String ? jobEl.GetString() : null;

            static (int? Id, string? Name) ParseMany2One(JsonElement el)
            {
                if (el.ValueKind != JsonValueKind.Array)
                {
                    return (null, null);
                }

                int? m2oId = null;
                string? m2oName = null;
                int len = el.GetArrayLength();

                if (len >= 1 && el[0].ValueKind == JsonValueKind.Number && el[0].TryGetInt32(out int i))
                {
                    m2oId = i;
                }

                if (len >= 2 && el[1].ValueKind == JsonValueKind.String)
                {
                    m2oName = el[1].GetString();
                }

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

        public async Task<List<Leave>> GetLeavesAsync(params string[] states)
        {

            List<object> domain = [];

            object[] allStates = { "draft", "confirm", "validate1", "validate", "refuse", "cancel" };

            object[] providedStates = (states ?? Array.Empty<string>())
                                .Where(s => !string.IsNullOrWhiteSpace(s))
                                .Select(s => (object)s.Trim())
                                .ToArray();

            if (providedStates.Length > 0)
            {
                domain.Add(new object[] { "state", "in", providedStates });
            }
            else
            {
                domain.Add(new object[] { "state", "in", allStates });
            }

            string[] fields =
            {
                "id",
                "holiday_status_id",
                "state",
                "request_date_to",
                "request_date_from",
                "number_of_days",
                "first_approver_id"
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

            _ = res.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("error", out JsonElement err))
            {
                throw new Exception("Erreur Odoo : " + err.ToString());
            }

            if (!root.TryGetProperty("result", out JsonElement result))
            {
                throw new Exception("Réponse Odoo inattendue : " + text);
            }

            if (result.ValueKind == JsonValueKind.False)
            {
                return [];
            }

            if (result.ValueKind != JsonValueKind.Array)
            {
                throw new Exception("Réponse Odoo inattendue (result n'est pas un tableau) : " + text);
            }

            List<Leave> list = [];

            foreach (JsonElement item in result.EnumerateArray())
            {
                int id = item.TryGetProperty("id", out JsonElement idEl) && idEl.ValueKind == JsonValueKind.Number
                    ? idEl.GetInt32()
                    : 0;

                string type = "";
                if (item.TryGetProperty("holiday_status_id", out JsonElement holidayStatusEl) &&
                    holidayStatusEl.ValueKind == JsonValueKind.Array &&
                    holidayStatusEl.GetArrayLength() > 1)
                {
                    JsonElement nameElement = holidayStatusEl[1];
                    if (nameElement.ValueKind == JsonValueKind.String)
                    {
                        type = nameElement.GetString() ?? "";
                    }
                }

                if (!item.TryGetProperty("request_date_from", out JsonElement fromEl) ||
                    fromEl.ValueKind != JsonValueKind.String)
                {
                    throw new Exception("Erreur sur la date de début.");
                }

                DateTime startDate = DateTime.Parse(fromEl.GetString());

                if (!item.TryGetProperty("request_date_to", out JsonElement toEl) ||
                    toEl.ValueKind != JsonValueKind.String)
                {
                    throw new Exception("Erreur sur la date de fin.");
                }

                DateTime endDate = DateTime.Parse(toEl.GetString());

                string state = "";
                if (item.TryGetProperty("state", out JsonElement stateEl) &&
                    stateEl.ValueKind == JsonValueKind.String)
                {
                    state = stateEl.GetString() ?? "";
                }

                if (!item.TryGetProperty("number_of_days", out JsonElement daysEl) ||
                    daysEl.ValueKind != JsonValueKind.Number)
                {
                    throw new Exception("Erreur sur les jours");
                }

                if (!item.TryGetProperty("first_approver_id", out JsonElement firEl))
                {
                    throw new Exception("Erreur sur l'approbateur");
                }

                string? firstApprover = null;
                if (firEl.ValueKind == JsonValueKind.Array && firEl.GetArrayLength() >= 2 && firEl[1].ValueKind == JsonValueKind.String)
                {
                    firstApprover = firEl[1].GetString();
                }

                int days = (int)Math.Round(daysEl.GetDouble());

                list.Add(new Leave(
                    id,
                    LeaveTypeHelper.Translate(type),
                    startDate,
                    endDate,
                    LeaveStatusHelper.StateToFrench(state),
                    days,
                    firstApprover
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
            _ = res.EnsureSuccessStatusCode();

            string text = await res.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(text);

            if (!doc.RootElement.TryGetProperty("result", out JsonElement result) || result.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

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
                            "request_date_from",
                            "request_date_to",
                            "state",
                            "name",
                            "can_validate",
                            "can_refuse"
                        },
                        limit = 0,
                        order = "request_date_from"
                    }
                },
                id = 1
            };

            HttpResponseMessage res = await _httpClient.PostAsync(_baseUrl, BuildJsonContent(payload));

            string text = await res.Content.ReadAsStringAsync();
            _ = res.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("error", out _))
            {
                throw new Exception($"Erreur: {text}");
            }

            if (!root.TryGetProperty("result", out JsonElement resEl) && resEl.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            List<LeaveToApprove> leaves = [];

            foreach (JsonElement item in resEl.EnumerateArray())
            {
                int id = item.GetProperty("id").GetInt32();

                string employeeName = item.GetProperty("employee_id")[1].GetString() ?? "Employé inconnu";

                string type = LeaveTypeHelper.Translate(item.GetProperty("holiday_status_id")[1].GetString());

                DateTime startDate = DateTime.Parse(item.GetProperty("request_date_from").GetString());
                DateTime endDate = DateTime.Parse(item.GetProperty("request_date_to").GetString());

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
            string text = await res.Content.ReadAsStringAsync();
            _ = res.EnsureSuccessStatusCode();

            // Vérifier les erreurs JSON-RPC (comme Session expired)
            CheckForOdooError(text);
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
            string text = await res.Content.ReadAsStringAsync();
            _ = res.EnsureSuccessStatusCode();

            // Vérifier les erreurs JSON-RPC (comme Session expired)
            CheckForOdooError(text);
        }

        /// <summary>
        /// Vérifie si la réponse Odoo contient une erreur JSON-RPC et lance une exception si c'est le cas
        /// </summary>
        private static void CheckForOdooError(string responseText)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(responseText);
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("error", out JsonElement errorEl))
                {
                    string errorMessage = "Erreur Odoo";

                    // Essayer de récupérer le message d'erreur
                    if (errorEl.TryGetProperty("message", out JsonElement msgEl) &&
                        msgEl.ValueKind == JsonValueKind.String)
                    {
                        errorMessage = msgEl.GetString() ?? errorMessage;
                    }

                    // Récupérer aussi le nom de l'erreur si disponible
                    if (errorEl.TryGetProperty("data", out JsonElement dataEl) &&
                        dataEl.TryGetProperty("name", out JsonElement nameEl) &&
                        nameEl.ValueKind == JsonValueKind.String)
                    {
                        string? errorName = nameEl.GetString();
                        if (!string.IsNullOrEmpty(errorName))
                        {
                            errorMessage = $"{errorMessage} ({errorName})";
                        }
                    }

                    throw new Exception(errorMessage);
                }
            }
            catch (JsonException)
            {
                // Si le parsing JSON échoue, ignorer (pas une réponse JSON valide)
            }
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
            _ = res.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(text);

            if (!doc.RootElement.TryGetProperty("result", out JsonElement resEl))
            {
                throw new InvalidOperationException("Réponse Odoo invalide: 'result' manquant.");
            }

            if (resEl.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Réponse Odoo inattendue: 'result' n'est pas un tableau.");
            }

            if (resEl.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("Aucun employé associé à cet utilisateur (user_id).");
            }

            JsonElement first = resEl[0];

            if (!first.TryGetProperty("id", out JsonElement idElement) || idElement.ValueKind != JsonValueKind.Number)
            {
                throw new InvalidOperationException("Champ 'id' introuvable ou invalide dans le résultat.");
            }

            int employeeId = idElement.GetInt32();
            return employeeId;

        }
        public async Task<bool> HasOverlappingLeaveAsync(DateTime startDate, DateTime endDate)
        {
            EnsureAuthenticated();

            string start = startDate.ToString("yyyy-MM-dd");
            string end = endDate.ToString("yyyy-MM-dd");

            object[] domain =
            {
        new object[] { "employee_id", "=", session.Current.EmployeeId!.Value },
        new object[] { "state", "not in", new object[] { "cancel", "refuse", "draft" } },
        new object[] { "request_date_from", "<=", end },
        new object[] { "request_date_to", ">=", start }
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
            _ = res.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("result", out JsonElement countEl) || countEl.ValueKind != JsonValueKind.Number)
            {
                throw new InvalidOperationException("Réponse Odoo inattendue pour search_count sur hr.leave.");
            }

            int count = countEl.GetInt32();
            return count > 0;
        }

        private async Task<List<LeaveTypeItem>> GetAllocationsAsync(int? typeId = null)
        {
            string today = DateTime.Today.ToString("yyyy-MM-dd");

            object[] domain = new object[]
            {
        new object[] { "employee_id", "=", session.Current.EmployeeId!.Value },
        new object[] { "state", "=", "validate" },
        new object[] { "date_from", "<=", today },
        "|",
        new object[] { "date_to", "=", false },
        new object[] { "date_to", ">=", today },
            };

            if (typeId.HasValue)
            {

                domain = domain
                            .Concat(new object[] { new object[] { "holiday_status_id", "=", typeId } })
                            .ToArray();
            }

            object[] fields = new object[]
                {
        "holiday_status_id",
        "number_of_days",
                };

            var payload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    model = "hr.leave.allocation",
                    method = "search_read",
                    args = new object[] { domain, fields },
                    kwargs = new { limit = 0 }
                },
                id = 1
            };

            HttpResponseMessage res = await _httpClient.PostAsync(_baseUrl, BuildJsonContent(payload));
            _ = res.EnsureSuccessStatusCode();
            string text = await res.Content.ReadAsStringAsync();

            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("error", out JsonElement errEl))
            {
                throw new Exception("Erreur Odoo : " + errEl.ToString());
            }

            if (!root.TryGetProperty("result", out JsonElement resEl) || resEl.ValueKind != JsonValueKind.Array)
            {
                throw new Exception("Réponse Odoo inattendue : " + text);
            }

            List<LeaveTypeItem> items = [];

            foreach (JsonElement row in resEl.EnumerateArray())
            {
                if (!row.TryGetProperty("holiday_status_id", out JsonElement hsEl) || hsEl.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                if (hsEl.GetArrayLength() <= 0)
                {
                    continue;
                }

                if (hsEl[0].ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                int leaveTypeId = hsEl[0].GetInt32();

                if (hsEl[1].ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string name = hsEl[1].GetString()!;

                if (!row.TryGetProperty("number_of_days", out JsonElement ndEl))
                {
                    continue;
                }

                if (ndEl.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                int days = ndEl.TryGetDecimal(out decimal d)
                    ? (int)Math.Round(d, MidpointRounding.AwayFromZero)
                    : (int)Math.Round(ndEl.GetDouble(), MidpointRounding.AwayFromZero);
                items.Add(new LeaveTypeItem(leaveTypeId, LeaveTypeHelper.Translate(name), true, days));
            }

            items = items.OrderBy(x => x.Name).ThenBy(x => x.Id).ToList();

            return items;
        }

        public async Task<List<LeaveTypeItem>> GetLeaveTypesAsync(bool? requiredAllocation = null, int? idRequest = null)
        {
            if (requiredAllocation.HasValue && requiredAllocation == true)
            {
                return await GetAllocationsAsync(idRequest);
            }

            object[] domain = new object[]
                    {
            new object[] { "active", "=", true },
            new object[] { "requires_allocation", "=", false }
                    };

            string[] fields = new string[] { "id", "name", "requires_allocation" };

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
                fields
                },
                    kwargs = new { limit = 1000, order = "name asc, id asc" }
                },
                id = 2
            };

            HttpResponseMessage res = await _httpClient.PostAsync(_baseUrl, BuildJsonContent(payload));
            _ = res.EnsureSuccessStatusCode();
            string text = await res.Content.ReadAsStringAsync();

            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("result", out JsonElement resultEl) || resultEl.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Réponse Odoo invalide: 'result' manquant ou non-array pour hr.leave.allocation.");
            }

            List<LeaveTypeItem> allowed = [];
            foreach (JsonElement row in resultEl.EnumerateArray())
            {

                if (!row.TryGetProperty("id", out JsonElement idEl) || idEl.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                if (!row.TryGetProperty("name", out JsonElement nameEl) || nameEl.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                int id = idEl.GetInt32();
                string name = nameEl.GetString()!;
                bool requiresAlloc = row.TryGetProperty("requires_allocation", out JsonElement raEl) && raEl.ValueKind == JsonValueKind.True;

                allowed.Add(new LeaveTypeItem(id, LeaveTypeHelper.Translate(name), requiresAlloc));
            }

            return allowed;
        }
        public async Task<List<AllocationSummary>> GetAllocationsSummaryAsync(int? idRequest = null)
        {
            int year = DateTime.Today.Year;

            string dateFrom = new DateTime(year, 1, 1).ToString("yyyy-MM-dd");
            string dateTo = new DateTime(year, 12, 31).ToString("yyyy-MM-dd");

            int employeeId = session.Current.EmployeeId!.Value;

            List<LeaveTypeItem> types = await GetAllocationsAsync(idRequest);
            int[] typeIds = types.Select(t => t.Id).ToArray();

            List<object[]> domain =
            [
                new object[] { "employee_id", "=", employeeId }
            ];

            if (idRequest.HasValue)
            {
                domain.Add(new object[] { "holiday_status_id", "=", idRequest.Value });
            }
            else
            {
                if (typeIds.Length == 0)
                {
                    return [];
                }

                domain.Add(new object[] { "holiday_status_id", "in", typeIds });
            }

            domain.Add(new object[] { "state", "in", new object[] { "validate", "validate1" } });
            domain.Add(new object[] { "request_date_from", ">=", dateFrom });
            domain.Add(new object[] { "request_date_to", "<=", dateTo });

            object[] argsLeaves =
                {
                domain.ToArray(),
                new object[] { "number_of_days:sum" },
                new object[] { "holiday_status_id" }
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
                    kwargs = new { lazy = false }
                },
                id = 2
            };
            HttpResponseMessage res = await _httpClient.PostAsync(_baseUrl, BuildJsonContent(payloadLeaves));

            _ = res.EnsureSuccessStatusCode();

            string text = await res.Content.ReadAsStringAsync();

            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("error", out JsonElement errEl))
            {
                throw new Exception("Erreur Odoo : " + errEl.ToString());
            }

            if (!root.TryGetProperty("result", out JsonElement resEl))
            {
                throw new Exception("Réponse Odoo inattendue : " + text);
            }

            Dictionary<int, double> takenByType = ParseLeavesByType(text);

            List<AllocationSummary> summaries = new(types.Count);

            foreach (LeaveTypeItem t in types)
            {
                int totalAllocated = t.Days ?? 0;

                int totalTaken = takenByType.TryGetValue(t.Id, out double taken)
                    ? (int)Math.Round(taken)
                    : 0;

                int totalRemaining = totalAllocated - totalTaken;

                summaries.Add(new AllocationSummary(
                    LeaveTypeId: t.Id,
                    LeaveTypeName: t.Name,
                    TotalAllocated: totalAllocated,
                    TotalTaken: totalTaken,
                    TotalRemaining: totalRemaining
                ));
            }

            return summaries
                    .OrderBy(s => s.LeaveTypeName)
                    .ThenBy(s => s.LeaveTypeId)
                    .ToList();
        }

        public async Task<int>
            CreateLeaveRequestAsync(
         int leaveTypeId,
         DateTime startDate,
         DateTime endDate,
         string reason)
        {
            EnsureAuthenticated();

            if (await HasOverlappingLeaveAsync(startDate, endDate))
            {
                throw new InvalidOperationException("Votre demande chevauche sur un congé qui a déjà été demandé ou pris.");
            }

            // Vérifier si le type de congé nécessite une allocation
            bool requiresAllocation = await DoesLeaveTypeRequireAllocationAsync(leaveTypeId);

            // Seulement vérifier l'allocation si le type de congé l'exige
            if (requiresAllocation && !await HasValidAllocationAsync(leaveTypeId))
            {
                throw new InvalidOperationException("Aucune allocation valide ne couvre les dates demandées pour ce type de congé.");
            }

            Dictionary<string, object> values = new()
            {
                ["employee_id"] = session.Current.EmployeeId!.Value,
                ["holiday_status_id"] = leaveTypeId,
                ["request_date_from"] = startDate.ToString("yyyy-MM-dd"),
                ["request_date_to"] = endDate.ToString("yyyy-MM-dd"),
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
            _ = res.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;

            return root.TryGetProperty("error", out JsonElement errEl)
                ? throw new Exception("Erreur Odoo : " + errEl.ToString())
                : !root.TryGetProperty("result", out JsonElement resEl)
                ? throw new Exception("Réponse Odoo inattendue : " + text)
                : resEl.ValueKind != JsonValueKind.Number
                ? throw new Exception("Réponse Odoo inattendue (result n'est pas un entier) : " + text)
                : resEl.GetInt32();
        }

        /// <summary>
        /// Vérifie si un type de congé nécessite une allocation
        /// </summary>
        private async Task<bool> DoesLeaveTypeRequireAllocationAsync(int leaveTypeId)
        {
            EnsureAuthenticated();

            object[] domain = new object[]
            {
                new object[] { "id", "=", leaveTypeId }
            };

            string[] fields = new string[] { "requires_allocation" };

            var payload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    model = "hr.leave.type",
                    method = "search_read",
                    args = new object[] { domain, fields },
                    kwargs = new { limit = 1 }
                },
                id = 804
            };

            HttpResponseMessage res = await _httpClient.PostAsync(_baseUrl, BuildJsonContent(payload));
            string text = await res.Content.ReadAsStringAsync();
            _ = res.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("result", out JsonElement resEl) ||
                resEl.ValueKind != JsonValueKind.Array ||
                resEl.GetArrayLength() == 0)
            {
                // Par défaut, considérer qu'une allocation est requise si on ne peut pas déterminer
                return true;
            }

            JsonElement first = resEl[0];

            // Dans Odoo, requires_allocation peut être "yes", "no" ou un booléen
            if (first.TryGetProperty("requires_allocation", out JsonElement reqEl))
            {
                if (reqEl.ValueKind == JsonValueKind.True)
                {
                    return true;
                }
                if (reqEl.ValueKind == JsonValueKind.False)
                {
                    return false;
                }
                if (reqEl.ValueKind == JsonValueKind.String)
                {
                    string val = reqEl.GetString() ?? "";
                    return val.Equals("yes", StringComparison.OrdinalIgnoreCase);
                }
            }

            return true;
        }

        public async Task<string> GetCurrentEmployeeNameAsync()
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
                        new string[] { "name" }
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
            _ = res.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(text);

            if (!doc.RootElement.TryGetProperty("result", out JsonElement resEl) ||
                resEl.ValueKind != JsonValueKind.Array ||
                resEl.GetArrayLength() == 0)
            {
                return "Employé inconnu";
            }

            JsonElement first = resEl[0];
            return first.TryGetProperty("name", out JsonElement nameEl) && nameEl.ValueKind == JsonValueKind.String
                ? nameEl.GetString() ?? "Employé inconnu"
                : "Employé inconnu";
        }

        private async Task<bool> HasValidAllocationAsync(int leaveTypeId)
        {
            EnsureAuthenticated();

            string today = DateTime.Today.ToString("yyyy-MM-dd");

            object[] domain = new object[]
            {
                new object[] { "employee_id", "=", session.Current.EmployeeId!.Value },
                new object[] { "holiday_status_id", "=", leaveTypeId },
                new object[] { "state", "=", "validate" },
                new object[] { "date_from", "<=", today },
                "|",
                new object[] { "date_to", "=", false },
                new object[] { "date_to", ">=", today }
            };

            var payload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    model = "hr.leave.allocation",
                    method = "search_count",
                    args = new object[] { domain },
                    kwargs = new { }
                },
                id = 803
            };

            HttpResponseMessage res = await _httpClient.PostAsync(_baseUrl, BuildJsonContent(payload));
            string text = await res.Content.ReadAsStringAsync();
            _ = res.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;

            System.Diagnostics.Debug.WriteLine($"{text}");
            return root.TryGetProperty("result", out JsonElement countEl) && countEl.ValueKind == JsonValueKind.Number && countEl.GetInt32() > 0;
        }
    }
}