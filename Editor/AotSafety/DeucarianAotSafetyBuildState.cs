namespace Deucarian.BuildPipeline
{
    internal static class DeucarianAotSafetyBuildState
    {
        private static readonly object SyncRoot = new object();
        private static DeucarianAotSafetyReport report;
        private static DeucarianAotSafetyMode? mode;

        internal static DeucarianAotSafetyMode? CurrentMode
        {
            get
            {
                lock (SyncRoot)
                {
                    return mode;
                }
            }
        }

        internal static void Begin(
            DeucarianAotSafetyMode requestedMode,
            DeucarianAotSafetyReport initialReport)
        {
            lock (SyncRoot)
            {
                mode = requestedMode;
                report = initialReport ?? new DeucarianAotSafetyReport();
                report.mode = requestedMode.ToString();
            }
        }

        internal static void Merge(DeucarianAotSafetyReport value)
        {
            lock (SyncRoot)
            {
                if (report == null)
                {
                    report = new DeucarianAotSafetyReport();
                    if (mode.HasValue)
                    {
                        report.mode = mode.Value.ToString();
                    }
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
                mode = null;
            }
        }
    }
}
