using System.Globalization;

namespace PFE.Helpers;

public class StatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string status)
            return Colors.Gray;

        return status switch
        {
            "Brouillon" => Color.FromArgb("#FFA500"),         // Orange
            "En attente d'approbation" => Color.FromArgb("#4169E1"), // Royal Blue
            "Validé" => Color.FromArgb("#28A745"),            // Green
            "Refusé" => Color.FromArgb("#DC3545"),            // Red
            "Annulé" => Color.FromArgb("#6C757D"),            // Gray
            _ => Color.FromArgb("#808080")                     // Default Gray
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
