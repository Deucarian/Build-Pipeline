using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
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

            Dispatch(
                profile,
                environment,
                outputPath,
                additionalOptions,
                DeucarianBuildManagerDiscovery.Discover(),
                DeucarianBuildRunner.Build);
        }

        internal static DeucarianBuildResult Dispatch(
            BuildProfile profile,
            DeucarianBuildEnvironment environment,
            string outputPath,
            BuildOptions additionalOptions,
            DeucarianBuildManagerDiscoveryResult discovery,
            Func<DeucarianBuildRequest, DeucarianBuildResult> unregisteredBuild)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (discovery == null)
            {
                throw new BuildFailedException(
                    "Registered build target discovery returned no result.");
            }

            if (discovery.Issues.Count > 0)
            {
                throw new BuildFailedException(
                    "Registered build target discovery reported "
                    + discovery.Issues.Count
                    + " issue(s). Resolve build-provider discovery before "
                    + "running command-line builds.");
            }

            var matches = DeucarianUnityBuildBridge.FindEntriesForProfile(
                profile,
                discovery);
            if (matches.Count > 1)
            {
                throw new BuildFailedException(
                    "Multiple registered build targets use the requested Build Profile. "
                    + "Give every target its own profile before running CI.");
            }

            if (matches.Count == 0)
            {
                if (unregisteredBuild == null)
                {
                    throw new ArgumentNullException(nameof(unregisteredBuild));
                }

                return unregisteredBuild(new DeucarianBuildRequest(
                    profile,
                    environment,
                    outputPath,
                    additionalOptions));
            }

            DeucarianBuildManagerTarget target = matches[0].Target;
            if (target.Environment != environment)
            {
                throw new BuildFailedException(
                    "The requested command-line environment does not match the "
                    + "registered target for this Build Profile.");
            }

            if (!target.SupportsInvocationOverrides)
            {
                throw new BuildFailedException(
                    "The registered command-line build target uses the legacy "
                    + "parameterless callback contract.");
            }

            return DeucarianBuildDispatcher.Build(
                target,
                new DeucarianBuildInvocation(
                    profile,
                    outputPath,
                    additionalOptions,
                    DeucarianBuildInvocationSource.CommandLine));
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
