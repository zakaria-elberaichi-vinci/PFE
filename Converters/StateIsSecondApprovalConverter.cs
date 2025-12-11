using System.Globalization;

namespace PFE.Converters
{
    public class StateIsSecondApprovalConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string state)
            {
                return state == "validate1"; // Odoo state for Second Approval
            }

            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}