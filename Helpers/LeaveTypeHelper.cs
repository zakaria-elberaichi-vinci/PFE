namespace PFE.Helpers;

public static class LeaveTypeHelper
{
    private static readonly Dictionary<string, string> _translations = new()
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

    public static string Translate(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return "Type inconnu";

        return _translations.TryGetValue(type, out var fr) ? fr : type;
    }
}
