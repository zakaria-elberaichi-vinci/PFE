
using System.Globalization;
using System.Text;

namespace PFE.Helpers;

public sealed class LeaveTypeItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public static class LeaveTypeHelper
{

    public static readonly Dictionary<int, string> LeaveTypes = new Dictionary<int, string>
{
    { 1,  "Congés payés" },
    { 2,  "Congé maladie" },
    { 4,  "Non payé" },
    { 3,  "Jours de compensation" },
    { 5,  "Jours de congé supplémentaires" },
    { 6,  "Heures supplémentaires" },
    { 13, "Congé extra-légal" },
    { 9,  "Petit chômage" },
    { 10, "Congé maternité" },
    { 11, "Raison impérieuse" },
    { 12, "Congé éducation" },
    { 14, "Récupération de jour férié" },
    { 15, "Congé européen" },
    { 16, "Crédit-temps" },
    { 17, "Congé accident de travail" },
    { 18, "Grève" },
    { 19, "Congé maladie sans certificat" },
    { 20, "Congé de naissance" }
};
    public static readonly List<LeaveTypeItem> LeaveTypeItems = LeaveTypes
            .Select(kvp => new LeaveTypeItem { Id = kvp.Key, Name = kvp.Value })
            .OrderBy(x => x.Name)
            .ToList();
    public static List<LeaveTypeItem> FilterLeaveTypeItems(HashSet<int> allowedIds)
    {
        if (allowedIds == null || allowedIds.Count == 0)
            return new List<LeaveTypeItem>();

        return LeaveTypeItems
            .Where(x => allowedIds.Contains(x.Id))
            .OrderBy(x => x.Name)
            .ToList();
    }

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

    // FR -> EN (construit une table inversée)
    private static readonly Dictionary<string, string> _reverseTranslations =
        _translations.ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.OrdinalIgnoreCase);
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

        // Partial search (accent/case-insensitive)
        string normInput = Normalize(englishName);
        foreach (KeyValuePair<string, string> kvp in _translations)
        {
            if (Normalize(kvp.Key).Contains(normInput))
                return kvp.Value;
        }

        return englishName; // fallback
    }
    public static string TranslateToEnglish(string frenchName)
    {
        if (string.IsNullOrWhiteSpace(frenchName))
            return "Unspecified type";

        // Match exact (case-insensitive)
        if (_reverseTranslations.TryGetValue(frenchName.Trim(), out string? englishName))
            return englishName;

        // Partial search (accent/case-insensitive)
        string normInput = Normalize(frenchName);
        foreach (KeyValuePair<string, string> kvp in _reverseTranslations)
        {
            if (Normalize(kvp.Key).Contains(normInput))
                return kvp.Value;
        }

        return frenchName; // fallback (on renvoie la saisie si inconnue)
    }
}
