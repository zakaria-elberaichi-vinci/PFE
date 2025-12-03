using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;

namespace PFE;

public partial class UserProfilePage : ContentPage
{
    private readonly string _url;
    private readonly string _db;
    private readonly int _userId;
    private readonly string _password;

    public UserProfilePage(string url, string db, int userId, string password)
    {
        InitializeComponent();

        _url = url;
        _db = db;
        _userId = userId;
        _password = password;

        LoadUserInfos();
    }

    private async void LoadUserInfos()
    {
        try
        {
            using var client = new HttpClient();

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
            _db,
            _userId,
            _password,
            "hr.employee",
            "search_read",
            new object[]
            {
                new object[]
                {
                    new object[] { "user_id", "=", _userId }
                }
            },
            new
            {
                fields = new[]
                {
                    "name",
                    "work_email",
                    "job_title",
                    "department_id",
                    "parent_id"
                }
            }
         }
                }
            };


            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{_url}/jsonrpc", content);
            var resultString = await response.Content.ReadAsStringAsync();

            var json = JsonSerializer.Deserialize<JsonElement>(resultString);

            var records = json.GetProperty("result").GetProperty("result");

            if (records.GetArrayLength() > 0)
            {
                var user = records[0];

                LblName.Text = user.GetProperty("name").GetString();
                LblEmail.Text = user.GetProperty("work_email").GetString();
                LblJob.Text = user.GetProperty("job_title").GetString();
                LblDepartment.Text = user.GetProperty("department_id")[1].GetString();
                LblManager.Text = user.GetProperty("parent_id")[1].GetString();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erreur", ex.Message, "OK");
        }
    }
}
