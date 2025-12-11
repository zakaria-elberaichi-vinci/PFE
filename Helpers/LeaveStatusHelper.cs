namespace PFE.Helpers
{
    public static class LeaveStatusHelper
    {
        public static string StateToFrench(string state)
        {
            return state switch
            {
                "draft" => "En brouillon",
                "confirm" => "En attente d'approbation",
                "validate1" => "Validé par le manager",
                "validate" => "Validé par le RH",
                "refuse" => "Refusé",
                "cancel" => "Annulé",
                _ => state
            };
        }

        public static readonly Dictionary<string, string> FrenchToEnglishStatus =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "En brouillon",               "draft" },
                { "En attente d'approbation",   "confirm" },
                { "Validé par le manager",      "validate1" },
                { "Validé par le RH",           "validate" },
                { "Refusé",                     "refuse" },
                { "Annulé",                     "cancel" },
            };
    }
}