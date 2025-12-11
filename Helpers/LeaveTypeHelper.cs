using System.Globalization;
using System.Text;

namespace PFE.Helpers;

public static class LeaveTypeHelper
{
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
    private static readonly Dictionary<string, string> _colors = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Congés payés", "#2563EB" },              // Bleu
        { "Congé maladie", "#DC2626" },             // Rouge
        { "Non payé", "#6B7280" },                  // Gris
        { "Jours de compensation", "#7C3AED" },     // Violet
        { "Jours de congé supplémentaires", "#0891B2" }, // Cyan
        { "Heures supplémentaires", "#EA580C" },    // Orange
        { "Congé extra-légal", "#4F46E5" },         // Indigo
        { "Petit chômage", "#CA8A04" },             // Jaune foncé
        { "Congé maternité", "#DB2777" },           // Rose
        { "Raison impérieuse", "#B91C1C" },         // Rouge foncé
        { "Congé Education", "#0D9488" },           // Teal
        { "Récupération de jour férié", "#059669" }, // Vert émeraude
        { "Congé européen", "#1D4ED8" },            // Bleu foncé
        { "Crédit-temps", "#9333EA" },              // Violet vif
        { "Congé accident de travail", "#BE123C" }, // Rose foncé
        { "Grève", "#78716C" },                     // Gris chaud
        { "Congé maladie sans certificat", "#EF4444" }, // Rouge clair
        { "Congé de naissance", "#EC4899" }         // Pink
    };

    private static readonly string _defaultColor = "#1E40AF"; // Bleu par défaut

    private static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        string formD = s.Trim().Normalize(NormalizationForm.FormD);
        StringBuilder sb = new(formD.Length);
        foreach (char ch in formD)
        {
            UnicodeCategory uc = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != UnicodeCategory.NonSpacingMark)
            {
                _ = sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    public static string Translate(string englishName)
    {
        if (string.IsNullOrWhiteSpace(englishName))
        {
            return "Type non spécifié";
        }

        if (_translations.TryGetValue(englishName.Trim(), out string? frenchName))
        {
            return frenchName;
        }

        string normInput = Normalize(englishName);
        foreach (KeyValuePair<string, string> kvp in _translations)
        {
            if (Normalize(kvp.Key).Contains(normInput))
            {
                return kvp.Value;
            }
        }

        return englishName;
    }

    public static string GetColorHex(string frenchName)
    {
        if (string.IsNullOrWhiteSpace(frenchName))
        {
            return _defaultColor;
        }

        if (_colors.TryGetValue(frenchName.Trim(), out string? colorHex))
        {
            return colorHex;
        }

        string normInput = Normalize(frenchName);
        foreach (KeyValuePair<string, string> kvp in _colors)
        {
            if (Normalize(kvp.Key).Contains(normInput) || normInput.Contains(Normalize(kvp.Key)))
            {
                return kvp.Value;
            }
        }

        return _defaultColor;
    }
}