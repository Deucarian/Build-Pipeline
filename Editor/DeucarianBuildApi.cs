using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;

namespace Deucarian.BuildPipeline
{
    public enum DeucarianBuildEnvironment
    {
        Development,
        Production
    }

    [Serializable]
    public sealed class DeucarianBuildRequest
    {
        public BuildProfile BuildProfile { get; set; }
        public DeucarianBuildEnvironment Environment { get; set; }
        public string OutputPath { get; set; }
        public BuildOptions AdditionalBuildOptions { get; set; }

        public DeucarianBuildRequest()
        {
        }

        public DeucarianBuildRequest(
            BuildProfile buildProfile,
            DeucarianBuildEnvironment environment,
            string outputPath,
            BuildOptions additionalBuildOptions = BuildOptions.None)
        {
            BuildProfile = buildProfile;
            Environment = environment;
            OutputPath = outputPath;
            AdditionalBuildOptions = additionalBuildOptions;
        }
    }

    public interface IDeucarianPlatformBuildPolicy
    {
        BuildTarget Target { get; }

        void ApplySettings(BuildProfile profile, DeucarianBuildEnvironment environment);

        DeucarianBuildValidationResult ValidateProfile(
            BuildProfile profile,
            DeucarianBuildEnvironment environment);

        DeucarianBuildValidationResult ValidateGeneratedArtifacts(
            DeucarianBuildRequest request,
            DeucarianBuildArtifactManifest manifest);
    }

    public sealed class DeucarianBuildValidationResult
    {
        private readonly List<string> _issues = new List<string>();

        public IReadOnlyList<string> Issues => _issues;
        public bool IsValid => _issues.Count == 0;

        public void Add(string issue)
        {
            if (!string.IsNullOrWhiteSpace(issue))
            {
                _issues.Add(issue);
            }
        }

        public void AddRange(IEnumerable<string> issues)
        {
            if (issues == null)
            {
                return;
            }

            foreach (string issue in issues)
            {
                Add(issue);
            }
        }

        public string Format(string heading)
        {
            if (IsValid)
            {
                return heading + ": valid.";
            }

            return heading + ":\n- " + string.Join("\n- ", _issues);
        }
    }

    public sealed class DeucarianBuildResult
    {
        public BuildReport BuildReport { get; internal set; }
        public DeucarianBuildArtifactManifest ArtifactManifest { get; internal set; }
    }
}
