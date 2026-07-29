using System;

namespace Deucarian.BuildPipeline
{
    internal sealed class DeucarianBuildExecutionScope : IDisposable
    {
        [ThreadStatic]
        private static int depth;

        private bool disposed;

        private DeucarianBuildExecutionScope()
        {
            depth++;
        }

        internal static bool IsActive => depth > 0;

        internal static DeucarianBuildExecutionScope Enter()
        {
            return new DeucarianBuildExecutionScope();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            depth = Math.Max(0, depth - 1);
        }
    }
}
