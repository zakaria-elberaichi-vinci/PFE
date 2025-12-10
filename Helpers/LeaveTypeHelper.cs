using System.Globalization;
using System.Text;

namespace PFE.Helpers;

public static class LeaveTypeHelper
{

    // EN -> FR
    private static readonly Dictionary<string, string> _translations = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Paid Time Off", "Congés payés" },
        { "Sick Time Off", "Congé maladie" },
        { "Unpaid", "Non payé" },
        { "Compensatory Days", "Jours de compensation" },
        { "Extra Time Off", "Jours de congé supplémentaires" },
        { "Extra Hours", "Heures supplémentaires" },
        { "Extra Legal Time Off", "Congé extra-légal" },
        { "Small Unemployment", "Petit chômage" },
        { "Maternity Time Off", "Congé maternité" },
        { "Unpredictable Reason", "Raison impérieuse" },
        { "Training Time Off", "Congé Education" },
        { "Recovery Bank Holiday", "Récupération de jour férié" },
        { "European Time Off", "Congé européen" },
        { "Credit Time", "Crédit-temps" },
        { "Work Accident Time Off", "Congé accident de travail" },
        { "Strike", "Grève" },
        { "Sick Leave Without Certificate", "Congé maladie sans certificat" },
        { "Brief Holiday (Birth)", "Congé de naissance" }
    };

    private static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        string formD = s.Trim().Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder(formD.Length);
        foreach (char ch in formD)
        {
            UnicodeCategory uc = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
    public static string Translate(string englishName)
    {
        if (string.IsNullOrWhiteSpace(englishName))
            return "Type non spécifié";

        if (_translations.TryGetValue(englishName.Trim(), out string? frenchName))
            return frenchName;

        string normInput = Normalize(englishName);
        foreach (KeyValuePair<string, string> kvp in _translations)
        {
            if (Normalize(kvp.Key).Contains(normInput))
                return kvp.Value;
        }

        return englishName;
    }
}
