using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;

namespace Deucarian.BuildPipeline
{
    public static class DeucarianBuildCommandLine
    {
        private const string DefaultResultPath =
            "Library/Deucarian/BuildPipeline/command-result.json";

        /// <summary>
        /// Target-aware CI entry point. Supports list, validate, and build actions through
        /// the same registered workflows used by the Build Pipeline Manager.
        /// </summary>
        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            string action = (GetValue(args, "-deucarianAction") ?? "build")
                .Trim()
                .ToLowerInvariant();
            string targetKey = GetValue(args, "-deucarianTarget");
            string resultPath = GetValue(args, "-deucarianResult")
                                ?? DefaultResultPath;
            DeucarianBuildCommandResult result = CreateResult(
                action,
                targetKey);

            try
            {
                switch (action)
                {
                    case "list":
                        ListTargets(result);
                        break;
                    case "validate":
                        ValidateTarget(args, result);
                        break;
                    case "build":
                        BuildTarget(args, result);
                        break;
                    default:
                        throw new ArgumentException(
                            "-deucarianAction must be list, validate, or build.");
                }

                result.success = true;
                result.finishedAtUtc = DateTime.UtcNow.ToString("O");
                result.WriteTo(resultPath);
                DeucarianBuildPipelineLog.Info(result.ToJson(false));
            }
            catch (Exception exception)
            {
                CompleteFailure(result, exception);
                TryWriteFailure(result, resultPath);
                throw;
            }
        }

        /// <summary>
        /// Compatibility entry point for existing profile-based CI callers.
        /// New automation should call Run and provide -deucarianTarget.
        /// </summary>
        public static void Build()
        {
            string[] args = Environment.GetCommandLineArgs();
            string resultPath = GetValue(args, "-deucarianResult")
                                ?? DefaultResultPath;
            DeucarianBuildCommandResult result = CreateResult(
                "legacy-build",
                null);

            try
            {
                string profilePath = RequireValue(
                    args,
                    "-deucarianProfile");
                ValidateProfileArguments(
                    profilePath,
                    GetValue(args, "-activeBuildProfile"));
                DeucarianBuildEnvironment environment = ParseEnvironment(
                    RequireValue(args, "-deucarianEnvironment"));
                string outputPath = RequireValue(
                    args,
                    "-deucarianOutput");
                BuildProfile profile =
                    AssetDatabase.LoadAssetAtPath<BuildProfile>(profilePath);
                if (profile == null)
                {
                    throw new ArgumentException(
                        "No Build Profile exists at '" + profilePath + "'.");
                }

                DeucarianBuildResult buildResult =
                    DeucarianBuildRunner.Build(
                        new DeucarianBuildRequest(
                            profile,
                            environment,
                            outputPath,
                            ParseBuildOptions(
                                GetValue(args, "-deucarianOptions")),
                            ParseAotSafetyMode(
                                GetValue(args, "-deucarianAotMode"))));
                result.target = profilePath;
                result.manifestPath = GetManifestPath(outputPath, buildResult);
                result.message = "Build completed successfully.";
                result.success = true;
                result.finishedAtUtc = DateTime.UtcNow.ToString("O");
                result.WriteTo(resultPath);
                DeucarianBuildPipelineLog.Info(result.ToJson(false));
            }
            catch (Exception exception)
            {
                CompleteFailure(result, exception);
                TryWriteFailure(result, resultPath);
                throw;
            }
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

        internal static BuildOptions ParseBuildOptions(string optionsValue)
        {
            BuildOptions additionalOptions = BuildOptions.None;
            if (string.IsNullOrWhiteSpace(optionsValue))
            {
                return additionalOptions;
            }

            string[] optionNames = optionsValue.Split(',');
            for (int i = 0; i < optionNames.Length; i++)
            {
                BuildOptions option;
                if (!Enum.TryParse(
                        optionNames[i].Trim(),
                        true,
                        out option))
                {
                    throw new ArgumentException(
                        "Unknown BuildOptions value '"
                        + optionNames[i] + "'.");
                }

                additionalOptions |= option;
            }

            return additionalOptions;
        }

        internal static DeucarianAotSafetyMode ParseAotSafetyMode(
            string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DeucarianAotSafetyMode.Inherit;
            }

            DeucarianAotSafetyMode mode;
            if (!Enum.TryParse(value.Trim(), true, out mode))
            {
                throw new ArgumentException(
                    "-deucarianAotMode must be Inherit, Audit, or Enforce.");
            }

            return mode;
        }

        private static void ListTargets(DeucarianBuildCommandResult result)
        {
            result.catalog = DeucarianBuildTargetRegistry.GetCatalog();
            if (!result.catalog.valid)
            {
                throw new BuildFailedException(
                    "Registered build target discovery failed:\n- "
                    + string.Join("\n- ", result.catalog.issues));
            }

            result.message = "Registered build targets listed successfully.";
        }

        private static void ValidateTarget(
            string[] args,
            DeucarianBuildCommandResult result)
        {
            string targetKey = RequireValue(args, "-deucarianTarget");
            DeucarianBuildTargetDescriptor descriptor =
                RequireDescriptor(targetKey);
            ValidateProfileArguments(
                descriptor.buildProfileAssetPath,
                GetValue(args, "-activeBuildProfile"));
            DeucarianBuildValidationResult validation =
                DeucarianBuildTargetRegistry.Validate(
                    targetKey,
                    GetValue(args, "-deucarianOutput"),
                    ParseBuildOptions(
                        GetValue(args, "-deucarianOptions")),
                    ParseAotSafetyMode(
                        GetValue(args, "-deucarianAotMode")));
            result.validationIssues.AddRange(validation.Issues);
            if (!validation.IsValid)
            {
                throw new BuildFailedException(
                    validation.Format(
                        "Registered Deucarian build validation failed"));
            }

            result.message = "Registered build target is valid.";
        }

        private static void BuildTarget(
            string[] args,
            DeucarianBuildCommandResult result)
        {
            string targetKey = RequireValue(args, "-deucarianTarget");
            DeucarianBuildTargetDescriptor descriptor =
                RequireDescriptor(targetKey);
            ValidateProfileArguments(
                descriptor.buildProfileAssetPath,
                GetValue(args, "-activeBuildProfile"));
            string outputPath = GetValue(args, "-deucarianOutput");
            DeucarianBuildResult buildResult =
                DeucarianBuildTargetRegistry.Build(
                    targetKey,
                    outputPath,
                    ParseBuildOptions(
                        GetValue(args, "-deucarianOptions")),
                    ParseAotSafetyMode(
                        GetValue(args, "-deucarianAotMode")),
                    DeucarianBuildInvocationSource.CommandLine);
            string actualOutput = string.IsNullOrWhiteSpace(outputPath)
                ? descriptor.defaultOutputPath
                : outputPath.Trim();
            result.manifestPath = GetManifestPath(
                actualOutput,
                buildResult);
            result.message = "Build completed successfully.";
        }

        private static DeucarianBuildTargetDescriptor RequireDescriptor(
            string targetKey)
        {
            DeucarianBuildTargetCatalog catalog =
                DeucarianBuildTargetRegistry.GetCatalog();
            if (!catalog.valid)
            {
                throw new BuildFailedException(
                    "Registered build target discovery failed:\n- "
                    + string.Join("\n- ", catalog.issues));
            }

            for (int i = 0; i < catalog.targets.Count; i++)
            {
                DeucarianBuildTargetDescriptor descriptor =
                    catalog.targets[i];
                if (string.Equals(
                        descriptor.key,
                        targetKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return descriptor;
                }
            }

            throw new BuildFailedException(
                "No registered Deucarian build target matches '"
                + targetKey + "'.");
        }

        private static string GetManifestPath(
            string outputPath,
            DeucarianBuildResult result)
        {
            if (result == null || result.ArtifactManifest == null)
            {
                return string.Empty;
            }

            string output =
                DeucarianBuildPathUtility.ToFullOutputPath(outputPath);
            return Path.Combine(
                    output,
                    DeucarianBuildArtifactManifest.FileName)
                .Replace('\\', '/');
        }

        private static DeucarianBuildEnvironment ParseEnvironment(
            string value)
        {
            DeucarianBuildEnvironment environment;
            if (!Enum.TryParse(value, true, out environment))
            {
                throw new ArgumentException(
                    "-deucarianEnvironment must be Development or Production.");
            }

            return environment;
        }

        private static DeucarianBuildCommandResult CreateResult(
            string action,
            string target)
        {
            return new DeucarianBuildCommandResult
            {
                action = action ?? string.Empty,
                target = target ?? string.Empty,
                startedAtUtc = DateTime.UtcNow.ToString("O")
            };
        }

        private static void CompleteFailure(
            DeucarianBuildCommandResult result,
            Exception exception)
        {
            result.success = false;
            result.message = exception.GetBaseException().Message;
            result.errorType = exception.GetBaseException().GetType().FullName;
            result.finishedAtUtc = DateTime.UtcNow.ToString("O");
            DeucarianBuildPipelineLog.Error(result.message);
        }

        private static void TryWriteFailure(
            DeucarianBuildCommandResult result,
            string resultPath)
        {
            try
            {
                result.WriteTo(resultPath);
            }
            catch (Exception writeException)
            {
                DeucarianBuildPipelineLog.Error(
                    "Could not write command result: "
                    + writeException.GetBaseException().Message);
            }
        }

        private static string RequireValue(string[] args, string key)
        {
            string value = GetValue(args, key);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Missing required command-line argument " + key + ".");
            }

            return value;
        }

        private static string GetValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(
                        args[i],
                        key,
                        StringComparison.OrdinalIgnoreCase))
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
