using System;

namespace Deucarian.BuildPipeline
{
    internal sealed class DeucarianBuildInvocationScope : IDisposable
    {
        [ThreadStatic]
        private static DeucarianAotSafetyMode? currentAotSafetyMode;

        private readonly DeucarianAotSafetyMode? previousAotSafetyMode;
        private bool disposed;

        private DeucarianBuildInvocationScope(
            DeucarianAotSafetyMode aotSafetyMode)
        {
            previousAotSafetyMode = currentAotSafetyMode;
            currentAotSafetyMode = aotSafetyMode;
        }

        internal static DeucarianAotSafetyMode? CurrentAotSafetyMode =>
            currentAotSafetyMode;

        internal static DeucarianBuildInvocationScope Enter(
            DeucarianAotSafetyMode aotSafetyMode)
        {
            return new DeucarianBuildInvocationScope(aotSafetyMode);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            currentAotSafetyMode = previousAotSafetyMode;
        }
    }
}
