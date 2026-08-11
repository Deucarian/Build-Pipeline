namespace Deucarian.BuildPipeline
{
    internal static class DeucarianAotSafetyBuildState
    {
        private static readonly object SyncRoot = new object();
        private static DeucarianAotSafetyReport report;

        internal static void Begin(
            DeucarianAotSafetyMode mode,
            DeucarianAotSafetyReport initialReport)
        {
            lock (SyncRoot)
            {
                report = initialReport ?? new DeucarianAotSafetyReport();
                report.mode = mode.ToString();
            }
        }

        internal static void Merge(DeucarianAotSafetyReport value)
        {
            lock (SyncRoot)
            {
                if (report == null)
                {
                    report = new DeucarianAotSafetyReport();
                }

                report.Merge(value);
            }
        }

        internal static DeucarianAotSafetyReport Snapshot()
        {
            lock (SyncRoot)
            {
                return report ?? new DeucarianAotSafetyReport();
            }
        }

        internal static void Clear()
        {
            lock (SyncRoot)
            {
                report = null;
            }
        }
    }
}
