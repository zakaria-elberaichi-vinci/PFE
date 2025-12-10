namespace PFE.Helpers
{
    public class DateHelper
    {
        public static IEnumerable<DateTime> ExpandRangeToDays(DateTime start, DateTime end)
        {
            var startDate = start.Date;
            var endDate = end.Date;

            for (var d = startDate; d <= endDate; d = d.AddDays(1))
                yield return d;
        }

    }
}
