namespace PFE.Helpers
{
    public static class LeaveStatusHelper
    {
        public static string StateToFrench(string state) =>
            state switch
            {
                "draft" => "Brouillon",
                "confirm" => "En attente d'approbation",
                "validate" => "Validé",
                "refuse" => "Refusé",
                "cancel" => "Annulé",
                _ => state
            };
    }
}
