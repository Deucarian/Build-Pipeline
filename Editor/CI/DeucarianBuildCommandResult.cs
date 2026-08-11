using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Deucarian.BuildPipeline
{
    [Serializable]
    public sealed class DeucarianBuildCommandResult
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public string action;
        public string target;
        public bool success;
        public string message;
        public string errorType;
        public string startedAtUtc;
        public string finishedAtUtc;
        public string manifestPath;
        public DeucarianBuildTargetCatalog catalog;
        public List<string> validationIssues = new List<string>();

        public string ToJson(bool prettyPrint = true)
        {
            return JsonUtility.ToJson(this, prettyPrint);
        }

        public void WriteTo(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "A command result path is required.",
                    nameof(path));
            }

            string fullPath = ResolvePath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, ToJson());
        }

        internal static string ResolvePath(string path)
        {
            if (Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(
                projectRoot ?? string.Empty,
                path));
        }
    }
}
