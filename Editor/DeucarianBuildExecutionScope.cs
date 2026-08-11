using System;

namespace Deucarian.BuildPipeline
{
    internal sealed class DeucarianBuildExecutionScope : IDisposable
    {
        [ThreadStatic]
        private static int depth;

        [ThreadStatic]
        private static DeucarianBuildEnvironment? currentEnvironment;

        [ThreadStatic]
        private static DeucarianAotSafetyMode? currentAotSafetyMode;

        private readonly DeucarianBuildEnvironment? previousEnvironment;
        private readonly DeucarianAotSafetyMode? previousAotSafetyMode;
        private bool disposed;

        private DeucarianBuildExecutionScope(
            DeucarianBuildEnvironment environment,
            DeucarianAotSafetyMode aotSafetyMode)
        {
            previousEnvironment = currentEnvironment;
            previousAotSafetyMode = currentAotSafetyMode;
            currentEnvironment = environment;
            currentAotSafetyMode = aotSafetyMode;
            depth++;
        }

        internal static bool IsActive => depth > 0;

        internal static DeucarianBuildEnvironment? CurrentEnvironment =>
            currentEnvironment;

        internal static DeucarianAotSafetyMode? CurrentAotSafetyMode =>
            currentAotSafetyMode;

        internal static DeucarianBuildExecutionScope Enter()
        {
            return Enter(
                DeucarianBuildEnvironment.Development,
                DeucarianAotSafetyMode.Audit);
        }

        internal static DeucarianBuildExecutionScope Enter(
            DeucarianBuildEnvironment environment,
            DeucarianAotSafetyMode aotSafetyMode)
        {
            return new DeucarianBuildExecutionScope(
                environment,
                aotSafetyMode);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            depth = Math.Max(0, depth - 1);
            currentEnvironment = previousEnvironment;
            currentAotSafetyMode = previousAotSafetyMode;
        }
    }
}
