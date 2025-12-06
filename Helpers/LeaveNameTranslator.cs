namespace PFE.Helpers;

public static class LeaveNameTranslator
{
    private static readonly Dictionary<string, string> LeaveTypeDictionary = new()
    {
        // Mappez les noms anglais d'Odoo aux traductions françaises
        { "Paid Time Off", "Congé payé" },
        { "Sick Time Off", "Congé maladie" },
        { "Unpaid", "Congé non payé" },
        { "Extra Hours", "Heures supplémentaires" },
        { "Maternity Time Off", "Congé maternité" },
        { "Unpredictable Reason", "Raison imprévisible" },
        { "Credit Time", "Temps de crédit" },
        { "Work Accident Time Off", "Congé accident du travail" },
         { "Time Off", "Congé" },
       
    };

    public static string Translate(string englishName)
    {
        System.Diagnostics.Debug.WriteLine($"[LeaveNameTranslator] Input: '{englishName}' (Length: {englishName?.Length})");
        
        if (string.IsNullOrWhiteSpace(englishName))
            return "Non disponible";

        // Normaliser : trim et lowercase pour comparaison
        string normalizedInput = englishName.Trim();

        // Cherche une correspondance exacte d'abord
        if (LeaveTypeDictionary.TryGetValue(normalizedInput, out var frenchName))
        {
            System.Diagnostics.Debug.WriteLine($"[LeaveNameTranslator] Exact match found: '{normalizedInput}' -> '{frenchName}'");
            return frenchName;
        }

        // Cherche une correspondance case-insensitive
        foreach (var kvp in LeaveTypeDictionary)
        {
            if (string.Equals(kvp.Key, normalizedInput, StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine($"[LeaveNameTranslator] Case-insensitive match found: '{normalizedInput}' -> '{kvp.Value}'");
                return kvp.Value;
            }
        }

        // Cherche une correspondance partielle (contient)
        foreach (var kvp in LeaveTypeDictionary)
        {
            if (normalizedInput.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine($"[LeaveNameTranslator] Partial match found: '{normalizedInput}' contient '{kvp.Key}' -> '{kvp.Value}'");
                return kvp.Value;
            }
        }

        System.Diagnostics.Debug.WriteLine($"[LeaveNameTranslator] No match found, returning original: '{englishName}'");
        return englishName;
    }
}
