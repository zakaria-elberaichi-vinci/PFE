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
            var client = new OdooClient(_url);
            var userInfo = await client.GetUserInfoAsync(_db, _userId, _password);

            LblName.Text = userInfo.Name;
            LblEmail.Text = userInfo.WorkEmail;
            LblJob.Text = userInfo.JobTitle;
            LblDepartment.Text = userInfo.Department;
            LblManager.Text = userInfo.Manager;
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erreur", ex.Message, "OK");
        }
    }
}

