using System;
using System.IO;
using UnityEditor.Build;

namespace Deucarian.BuildPipeline
{
    internal static class DeucarianBuildManifestStore
    {
        internal static string GetManifestPath(string outputPath)
        {
            string fullOutput = DeucarianBuildPathUtility.ToFullOutputPath(
                outputPath);
            return Path.Combine(
                fullOutput,
                DeucarianBuildArtifactManifest.FileName);
        }

        internal static void Invalidate(string outputPath)
        {
            string manifestPath = GetManifestPath(outputPath);
            string temporaryPath = manifestPath + ".tmp";
            try
            {
                if (File.Exists(manifestPath))
                {
                    File.Delete(manifestPath);
                }

                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception)
            {
                throw new BuildFailedException(
                    new IOException(
                        "The previous build success manifest could not be invalidated before building.",
                        exception));
            }
        }
    }
}
