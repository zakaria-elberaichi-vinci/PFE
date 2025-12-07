namespace PFE.Controls;

public partial class ConfirmationPopup : ContentView
{
    public ConfirmationPopup()
    {
        InitializeComponent();
    }

    public void Show(string message)
    {
        MessageLabel.Text = message;
        IsVisible = true;
    }

    private void OnOkClicked(object sender, EventArgs e)
    {
        IsVisible = false;
    }
}
