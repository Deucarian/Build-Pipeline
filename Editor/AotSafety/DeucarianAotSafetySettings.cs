using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Deucarian.BuildPipeline
{
    [Serializable]
    public sealed class DeucarianAotSafetyException
    {
        public string assemblyName;
        public string declaringType;
        public string method;
        public string calledApi;
        public string reason;
    }

    [Serializable]
    public sealed class DeucarianAotSafetySettings
    {
        public const string ProjectSettingsPath =
            "ProjectSettings/DeucarianAotSafety.json";

        public DeucarianAotSafetyMode developmentMode =
            DeucarianAotSafetyMode.Audit;
        public DeucarianAotSafetyMode productionMode =
            DeucarianAotSafetyMode.Audit;
        public bool rejectManualProjectLinkXml = true;
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

        private static string GetFullPath()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return string.IsNullOrWhiteSpace(projectRoot)
                ? ProjectSettingsPath
                : Path.Combine(projectRoot, ProjectSettingsPath);
        }

        internal DeucarianAotSafetyMode ResolveMode(
            DeucarianBuildEnvironment environment,
            DeucarianAotSafetyMode requestedMode)
        {
            if (requestedMode != DeucarianAotSafetyMode.Inherit)
            {
                return requestedMode;
            }

            DeucarianAotSafetyMode configured =
                environment == DeucarianBuildEnvironment.Production
                    ? productionMode
                    : developmentMode;
            return configured == DeucarianAotSafetyMode.Inherit
                ? DeucarianAotSafetyMode.Audit
                : configured;
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
                    && !string.IsNullOrWhiteSpace(candidate.reason))
                {
                    return true;
                }
            }

            return false;
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
