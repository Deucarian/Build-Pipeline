using System;
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
            string environmentValue = RequireValue(args, "-deucarianEnvironment");
            string outputPath = RequireValue(args, "-deucarianOutput");

            DeucarianBuildEnvironment environment = ParseEnvironment(environmentValue);

            BuildProfile profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(profilePath);
            if (profile == null)
            {
                throw new ArgumentException("No Build Profile exists at '" + profilePath + "'.");
            }

            BuildOptions additionalOptions = ParseBuildOptions(
                GetValue(args, "-deucarianOptions"));

            DeucarianBuildRunner.Build(
                new DeucarianBuildRequest(
                    profile,
                    environment,
                    outputPath,
                    additionalOptions));
        }

        internal static DeucarianBuildEnvironment ParseEnvironment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "-deucarianEnvironment must be Development or Production.",
                    nameof(value));
            }

            string normalized = value.Trim();
            DeucarianBuildEnvironment environment;
            if (string.Equals(
                    normalized,
                    nameof(DeucarianBuildEnvironment.Development),
                    StringComparison.OrdinalIgnoreCase))
            {
                environment = DeucarianBuildEnvironment.Development;
            }
            else if (string.Equals(
                         normalized,
                         nameof(DeucarianBuildEnvironment.Production),
                         StringComparison.OrdinalIgnoreCase))
            {
                environment = DeucarianBuildEnvironment.Production;
            }
            else
            {
                throw new ArgumentException(
                    "-deucarianEnvironment must be Development or Production.",
                    nameof(value));
            }

            DeucarianBuildEnvironmentGuard.RequireDefined(environment, nameof(value));
            return environment;
        }

        internal static BuildOptions ParseBuildOptions(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return BuildOptions.None;
            }

            BuildOptions options = BuildOptions.None;
            string[] optionNames = value.Split(',');
            for (int i = 0; i < optionNames.Length; i++)
            {
                string optionName = optionNames[i].Trim();
                BuildOptions option;
                if (!TryParseNamedBuildOption(optionName, out option))
                {
                    throw new ArgumentException(
                        "Unknown BuildOptions value '" + optionNames[i] + "'.",
                        nameof(value));
                }

                options |= option;
            }

            return options;
        }

        private static bool TryParseNamedBuildOption(
            string optionName,
            out BuildOptions option)
        {
            string[] declaredNames = Enum.GetNames(typeof(BuildOptions));
            for (int i = 0; i < declaredNames.Length; i++)
            {
                if (!string.Equals(
                        optionName,
                        declaredNames[i],
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                option = (BuildOptions)Enum.Parse(
                    typeof(BuildOptions),
                    declaredNames[i]);
                return true;
            }

            option = BuildOptions.None;
            return false;
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
    }
}
