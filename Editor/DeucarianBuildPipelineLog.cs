using Deucarian.Logging;

namespace Deucarian.BuildPipeline
{
    internal static class DeucarianBuildPipelineLog
    {
        private static readonly DLog GeneralLog = DLog.For("BuildPipeline");

        public static void Info(string message)
        {
            GeneralLog.Info(message);
        }

        public static void Warning(string message)
        {
            GeneralLog.Warning(message);
        }

        public static void Error(string message)
        {
            GeneralLog.Error(message);
        }
    }
}
