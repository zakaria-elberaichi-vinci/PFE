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
        public int? EmployeeId { get; private set; }


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

                    
                    if (result.TryGetProperty("employee_id", out var empEl) && empEl.ValueKind == JsonValueKind.Array)
                    {
                        EmployeeId = empEl[0].GetInt32();
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
                    "holiday_status_id",
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
                if (item.TryGetProperty("holiday_status_id", out var holidayStatusEl) &&
                    holidayStatusEl.ValueKind == JsonValueKind.Array &&
                    holidayStatusEl.GetArrayLength() > 1)
                {
                    var nameElement = holidayStatusEl[1];
                    if (nameElement.ValueKind == JsonValueKind.String)
                        name = nameElement.GetString() ?? "";
                }

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
                    LeaveNameTranslator.Translate(name),
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

        
        private async Task<bool> UserIsInGroupAsync(int groupId)
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
                    args = new object[] { new int[] { groupId }, new string[] { "all_user_ids" } },
                    kwargs = new { }
                },
                id = 100 + groupId
            };

            var res = await _httpClient.PostAsync("/web/dataset/call_kw", BuildJsonContent(payload));
            string text = await res.Content.ReadAsStringAsync();
            System.Diagnostics.Debug.WriteLine($"UserIsInGroup({groupId}) response: " + text);

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (!root.TryGetProperty("result", out var result))
                return false;

            var arr = result[0];
            if (!arr.TryGetProperty("all_user_ids", out var usersEl))
                return false;

            foreach (var u in usersEl.EnumerateArray())
            {
                if (u.ValueKind == JsonValueKind.Number && u.GetInt32() == UserId)
                    return true;
            }
            return false;
        }

        public async Task<UserRole> GetUserRoleAsync()
        {
            EnsureAuthenticated();

            // 1. Administrator (ID 21)
            if (await UserIsInGroupAsync(21))
                return UserRole.Administrator;

            // 2. All Approver (Officer: Manage all requests) (ID 20)
            if (await UserIsInGroupAsync(20))
                return UserRole.AllApprover;

            // 3. Time Off Responsible (ID 19)
            if (await UserIsInGroupAsync(19))
                return UserRole.TimeOffResponsible;

            return UserRole.None;
        }





        /*
         * Ancienne méthode pour vérifier si l'utilisateur est gestionnaire des congés
        public async Task<bool> UserIsLeaveManagerAsync()
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
                    args = new object[] { new int[] { 19 }, new string[] { "all_user_ids" } },
                    kwargs = new { }  // <- Obligatoire pour call_kw
                },
                id = 21
            };

            var res = await _httpClient.PostAsync("/web/dataset/call_kw", BuildJsonContent(payload));
            string text = await res.Content.ReadAsStringAsync();
            System.Diagnostics.Debug.WriteLine("Odoo response: " + text);

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (!root.TryGetProperty("result", out var result))
                return false;

            var users = result[0].GetProperty("all_user_ids");
            foreach (var u in users.EnumerateArray())
            {
                if (u.GetInt32() == UserId)
                    return true;
            }

            return false;
        }*/

        /*
        public async Task<List<LeaveTimeOff>> GetLeavesToApproveAsync()
        {
            EnsureAuthenticated();

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

                // Ne surtout PAS filtrer sur manager_id !
                new object[]
                {
                    new object[] { "state", "in", new string[] { "confirm", "validate1" } }
                },
                new string[]
                {
                    "name",
                    "employee_id",
                    "request_date_from",
                    "request_date_to",
                    "state"
                    }
                },
                     kwargs = new { }
                },
                id = 11
            };

            var res = await _httpClient.PostAsync("/web/dataset/call_kw", BuildJsonContent(payload));
            string text = await res.Content.ReadAsStringAsync();
            System.Diagnostics.Debug.WriteLine("Odoo response: " + text);

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (!root.TryGetProperty("result", out var result))
                return new List<LeaveTimeOff>();

            var list = new List<LeaveTimeOff>();

            foreach (var item in result.EnumerateArray())
            {
                var employeeName = item.GetProperty("employee_id")[1].GetString() ?? "Employé inconnu";
                var leaveType = item.GetProperty("holiday_status_id")[1].GetString() ?? "Type non spécifié";
                var from = item.GetProperty("request_date_from").GetString() ?? "";
                var to = item.GetProperty("request_date_to").GetString() ?? "";
                var days = item.GetProperty("number_of_days").GetDouble();
                var status = item.GetProperty("state").GetString() ?? "";

                list.Add(new LeaveTimeOff
                {
                    EmployeeName = employeeName,
                    LeaveType = leaveType,
                    Period = $"{from} → {to}",
                    Days = $"{days} jours",
                    Status = status
                });
            }

            return list;
        }*/

        public async Task<List<LeaveTimeOff>> GetLeavesToApproveAsync()
        {
            EnsureAuthenticated();

            // 1. Récupérer les IDs des demandes en attente
            var payloadIds = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    model = "hr.leave",
                    method = "search",
                    args = new object[]
                    {
                new object[]
                {
                    new object[] { "state", "in", new string[] { "confirm", "validate1" } }
                }
                    },
                    kwargs = new { }
                },
                id = 1
            };

            var resIds = await _httpClient.PostAsync("/web/dataset/call_kw", BuildJsonContent(payloadIds));
            string textIds = await resIds.Content.ReadAsStringAsync();
            using var docIds = JsonDocument.Parse(textIds);
            var rootIds = docIds.RootElement;

            if (!rootIds.TryGetProperty("result", out var resultIds) || resultIds.GetArrayLength() == 0)
                return new List<LeaveTimeOff>();

            var ids = resultIds.EnumerateArray().Select(x => x.GetInt32()).ToArray();

            // 2. Lire les détails complets des demandes
            var payloadDetail = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    model = "hr.leave",
                    method = "read",
                    args = new object[]
                    {
                ids,
                new string[]
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
                }
                    },
                    kwargs = new { }
                },
                id = 2
            };

            var resDetail = await _httpClient.PostAsync("/web/dataset/call_kw", BuildJsonContent(payloadDetail));
            string textDetail = await resDetail.Content.ReadAsStringAsync();
            using var docDetail = JsonDocument.Parse(textDetail);
            var rootDetail = docDetail.RootElement;

            if (!rootDetail.TryGetProperty("result", out var resultDetail))
                return new List<LeaveTimeOff>();

            // 3. Construire les objets LeaveTimeOff
            var leaves = new List<LeaveTimeOff>();

            foreach (var item in resultDetail.EnumerateArray())
            {
                string employeeName = item.GetProperty("employee_id")[1].GetString() ?? "Employé inconnu";
                string leaveType = item.GetProperty("holiday_status_id")[1].GetString() ?? "Type non spécifié";
                string period = $"{item.GetProperty("request_date_from").GetString()} → {item.GetProperty("request_date_to").GetString()}";
                string days = $"{item.GetProperty("number_of_days").GetDouble()} jours";
                string status = item.GetProperty("state").GetString() ?? "";
                string reason = item.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                    ? nameProp.GetString()!
                    : "Aucune raison";

                bool canValidate = item.TryGetProperty("can_validate", out var cv) && cv.GetBoolean();
                bool canRefuse = item.TryGetProperty("can_refuse", out var cr) && cr.GetBoolean();

                leaves.Add(new LeaveTimeOff
                {
                    Id = item.GetProperty("id").GetInt32(),
                    EmployeeName = employeeName,
                    LeaveType = LeaveTypeHelper.Translate(leaveType ?? "Type non spécifié"),
                    Period = period,
                    Days = days,
                    Status = status,
                    Reason = reason,
                    CanValidate = canValidate,
                    CanRefuse = canRefuse
                });
            }

            return leaves;
        }

        public async Task ApproveLeaveAsync(int leaveId)
        {
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

            var res = await _httpClient.PostAsync("/web/dataset/call_kw", BuildJsonContent(payload));
            res.EnsureSuccessStatusCode();
            string text = await res.Content.ReadAsStringAsync();

            System.Diagnostics.Debug.WriteLine("ApproveLeaveAsync Odoo response: " + text);
        }

        public async Task RefuseLeaveAsync(int leaveId)
        {
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

            var res = await _httpClient.PostAsync("/web/dataset/call_kw", BuildJsonContent(payload));
            res.EnsureSuccessStatusCode();
            string text = await res.Content.ReadAsStringAsync();

            System.Diagnostics.Debug.WriteLine("RefuseLeaveAsync Odoo response: " + text);
        }






    }
}

