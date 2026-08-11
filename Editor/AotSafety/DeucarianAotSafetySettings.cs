using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Deucarian.BuildPipeline
{
    [Serializable]
    public sealed class DeucarianAotPreserveType
    {
        public string assemblyName;
        public string typeName;
        public string reason;
    }

    [Serializable]
    public sealed class DeucarianAotSafetyException
    {
        public string assemblyName;
        public string declaringType;
        public string method;
        public string calledApi;
        public string strategy;
        public string reason;
        public List<DeucarianAotPreserveType> preserveTypes =
            new List<DeucarianAotPreserveType>();
    }

    [Serializable]
    public sealed class DeucarianAotSafetySettings
    {
        public const string ProjectSettingsPath =
            "ProjectSettings/DeucarianAotSafety.json";

        public string developmentMode = "Audit";
        public string productionMode = "Audit";
        public bool rejectManualProjectLinkXml = true;
        public List<DeucarianAotPreserveType> preserveTypes =
            new List<DeucarianAotPreserveType>();
        public List<DeucarianAotSafetyException> exceptions =
            new List<DeucarianAotSafetyException>();

        public static DeucarianAotSafetySettings Load()
        {
            string fullPath = GetFullPath();
            if (!File.Exists(fullPath))
            {
                return new DeucarianAotSafetySettings();
            }

            try
            {
                DeucarianAotSafetySettings settings =
                    JsonUtility.FromJson<DeucarianAotSafetySettings>(
                        File.ReadAllText(fullPath));
                return settings ?? new DeucarianAotSafetySettings();
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    "Could not read Deucarian AOT safety settings at '"
                    + ProjectSettingsPath + "'.",
                    exception);
            }
        }

        internal DeucarianAotSafetyMode ResolveMode(
            DeucarianBuildEnvironment environment,
            DeucarianAotSafetyMode requestedMode)
        {
            if (requestedMode != DeucarianAotSafetyMode.Inherit)
            {
                return requestedMode;
            }

            string configured =
                environment == DeucarianBuildEnvironment.Production
                    ? productionMode
                    : developmentMode;
            if (string.IsNullOrWhiteSpace(configured))
            {
                return DeucarianAotSafetyMode.Audit;
            }

            DeucarianAotSafetyMode parsed;
            if (!Enum.TryParse(configured.Trim(), true, out parsed)
                || parsed == DeucarianAotSafetyMode.Inherit)
            {
                throw new InvalidDataException(
                    "AOT safety mode '" + configured
                    + "' is invalid. Use Audit or Enforce in '"
                    + ProjectSettingsPath + "'.");
            }

            return parsed;
        }

        internal bool IsDeclaredException(
            string assemblyName,
            string declaringType,
            string method,
            string calledApi)
        {
            if (exceptions == null)
            {
                return false;
            }

            for (int i = 0; i < exceptions.Count; i++)
            {
                DeucarianAotSafetyException candidate = exceptions[i];
                if (candidate == null)
                {
                    continue;
                }

                if (Matches(candidate.assemblyName, assemblyName)
                    && Matches(candidate.declaringType, declaringType)
                    && Matches(candidate.method, method)
                    && Matches(candidate.calledApi, calledApi)
                    && HasValidExceptionContract(candidate))
                {
                    return true;
                }
            }

            return false;
        }

        internal IEnumerable<DeucarianAotPreserveType>
            EnumeratePreserveTypes()
        {
            if (preserveTypes != null)
            {
                for (int i = 0; i < preserveTypes.Count; i++)
                {
                    if (preserveTypes[i] != null)
                    {
                        yield return preserveTypes[i];
                    }
                }
            }

            if (exceptions == null)
            {
                yield break;
            }

            for (int exceptionIndex = 0;
                 exceptionIndex < exceptions.Count;
                 exceptionIndex++)
            {
                DeucarianAotSafetyException exception =
                    exceptions[exceptionIndex];
                if (exception == null || exception.preserveTypes == null)
                {
                    continue;
                }

                for (int preserveIndex = 0;
                     preserveIndex < exception.preserveTypes.Count;
                     preserveIndex++)
                {
                    if (exception.preserveTypes[preserveIndex] != null)
                    {
                        yield return exception.preserveTypes[preserveIndex];
                    }
                }
            }
        }

        private static string GetFullPath()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return string.IsNullOrWhiteSpace(projectRoot)
                ? ProjectSettingsPath
                : Path.Combine(projectRoot, ProjectSettingsPath);
        }

        private static bool HasValidExceptionContract(
            DeucarianAotSafetyException candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate.strategy)
                || string.IsNullOrWhiteSpace(candidate.reason))
            {
                return false;
            }

            if (!string.Equals(
                    candidate.strategy,
                    "Declared",
                    StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(
                           candidate.strategy,
                           "Generated",
                           StringComparison.OrdinalIgnoreCase)
                       || string.Equals(
                           candidate.strategy,
                           "Framework",
                           StringComparison.OrdinalIgnoreCase);
            }

            return candidate.preserveTypes != null
                   && candidate.preserveTypes.Count > 0;
        }

        private static bool Matches(string expected, string actual)
        {
            return string.IsNullOrWhiteSpace(expected)
                || string.Equals(
                    expected.Trim(),
                    actual ?? string.Empty,
                    StringComparison.Ordinal);
        }
    }
}
