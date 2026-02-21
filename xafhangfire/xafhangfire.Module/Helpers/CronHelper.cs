using Cronos;
using CronExpressionDescriptor;

namespace xafhangfire.Module.Helpers
{
    public static class CronHelper
    {
        public static string GetDescription(string cronExpression)
        {
            if (string.IsNullOrWhiteSpace(cronExpression))
                return string.Empty;

            try
            {
                return ExpressionDescriptor.GetDescription(cronExpression);
            }
            catch
            {
                return $"Invalid: {cronExpression}";
            }
        }

        public static List<DateTime> GetNextRuns(string cronExpression, int count)
        {
            if (string.IsNullOrWhiteSpace(cronExpression) || count <= 0)
                return new List<DateTime>();

            try
            {
                var expression = CronExpression.Parse(cronExpression);
                var results = new List<DateTime>();
                var from = DateTime.UtcNow;

                for (var i = 0; i < count; i++)
                {
                    var next = expression.GetNextOccurrence(from, inclusive: false);
                    if (next == null) break;
                    results.Add(next.Value);
                    from = next.Value;
                }

                return results;
            }
            catch
            {
                return new List<DateTime>();
            }
        }

        public static string FormatNextRuns(string cronExpression, int count)
        {
            var runs = GetNextRuns(cronExpression, count);
            if (runs.Count == 0) return string.Empty;
            return string.Join("\n", runs.Select(r => r.ToLocalTime().ToString("yyyy-MM-dd HH:mm (ddd)")));
        }
    }
}
