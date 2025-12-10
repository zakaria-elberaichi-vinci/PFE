namespace PFE.Helpers
{
    public class DateHelper
    {
        public static IEnumerable<DateTime> ExpandRangeToDays(DateTime start, DateTime end)
        {
            DateTime startDate = start.Date;
            DateTime endDate = end.Date;

            for (DateTime d = startDate; d <= endDate; d = d.AddDays(1))
                yield return d;
        }

    }
}
