using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Profile;

namespace Deucarian.BuildPipeline
{
    public static class DeucarianBuildCommandLine
    {
        public static void Build()
        {
            string[] args = Environment.GetCommandLineArgs();
            string profilePath = RequireValue(args, "-deucarianProfile");
            ValidateProfileArguments(
                profilePath,
                GetValue(args, "-activeBuildProfile"));
            string environmentValue = RequireValue(args, "-deucarianEnvironment");
            string outputPath = RequireValue(args, "-deucarianOutput");

            DeucarianBuildEnvironment environment;
            if (!Enum.TryParse(environmentValue, true, out environment))
            {
                throw new ArgumentException(
                    "-deucarianEnvironment must be Development or Production.");
            }

            BuildProfile profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(profilePath);
            if (profile == null)
            {
                throw new ArgumentException("No Build Profile exists at '" + profilePath + "'.");
            }

            BuildOptions additionalOptions = BuildOptions.None;
            string optionsValue = GetValue(args, "-deucarianOptions");
            if (!string.IsNullOrWhiteSpace(optionsValue))
            {
                string[] optionNames = optionsValue.Split(',');
                for (int i = 0; i < optionNames.Length; i++)
                {
                    BuildOptions option;
                    if (!Enum.TryParse(optionNames[i].Trim(), true, out option))
                    {
                        throw new ArgumentException(
                            "Unknown BuildOptions value '" + optionNames[i] + "'.");
                    }

                    additionalOptions |= option;
                }
            }

            DeucarianBuildRunner.Build(
                new DeucarianBuildRequest(
                    profile,
                    environment,
                    outputPath,
                    additionalOptions));
        }

        internal static void ValidateProfileArguments(
            string profilePath,
            string activeProfilePath)
        {
            if (string.IsNullOrWhiteSpace(activeProfilePath))
            {
                return;
            }

            string requested = NormalizeAssetPath(profilePath);
            string active = NormalizeAssetPath(activeProfilePath);
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (string.Equals(requested, active, comparison))
            {
                return;
            }

            throw new ArgumentException(
                "-activeBuildProfile ('"
                + activeProfilePath
                + "') and -deucarianProfile ('"
                + profilePath
                + "') must reference the same Build Profile asset.");
        }

        private static string RequireValue(string[] args, string key)
        {
            string value = GetValue(args, key);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Missing required command-line argument " + key + ".");
            }

            return value;
        }

        private static string GetValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static string NormalizeAssetPath(string path)
        {
            return (path ?? string.Empty).Trim().Replace('\\', '/');
        }
    }
}
