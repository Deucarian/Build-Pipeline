using System;
using System.Collections.Generic;

namespace Deucarian.BuildPipeline
{
    /// <summary>
    /// Project-owned registration point for named build workflows shown by the shared manager.
    /// Implementations are discovered through Unity's TypeCache and must have a public
    /// parameterless constructor.
    /// </summary>
    public interface IDeucarianBuildManagerProvider
    {
        string Id { get; }

        string DisplayName { get; }

        int Order { get; }

        bool CanSynchronize { get; }

        IReadOnlyList<DeucarianBuildManagerTarget> GetTargets();

        void Synchronize();
    }

    /// <summary>
    /// Immutable description of a project-owned build workflow. The callback remains in
    /// the consuming project so its preflight, temporary state, and post-build validation
    /// are never bypassed by the package manager.
    /// </summary>
    public sealed class DeucarianBuildManagerTarget
    {
        public DeucarianBuildManagerTarget(
            string id,
            string displayName,
            string description,
            string buildProfileAssetPath,
            DeucarianBuildEnvironment environment,
            string outputPath,
            Func<DeucarianBuildResult> buildAction,
            Func<DeucarianBuildValidationResult> projectValidation = null)
        {
            DeucarianBuildEnvironmentGuard.RequireDefined(environment, nameof(environment));

            Id = Require(id, nameof(id));
            DisplayName = Require(displayName, nameof(displayName));
            Description = description ?? string.Empty;
            BuildProfileAssetPath = Require(buildProfileAssetPath, nameof(buildProfileAssetPath));
            OutputPath = Require(outputPath, nameof(outputPath));
            BuildAction = buildAction ?? throw new ArgumentNullException(nameof(buildAction));
            ProjectValidation = projectValidation;
            Environment = environment;
        }

        public string Id { get; }

        public string DisplayName { get; }

        public string Description { get; }

        public string BuildProfileAssetPath { get; }

        public DeucarianBuildEnvironment Environment { get; }

        public string OutputPath { get; }

        public Func<DeucarianBuildResult> BuildAction { get; }

        public Func<DeucarianBuildValidationResult> ProjectValidation { get; }

        private static string Require(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A non-empty value is required.", parameterName);
            }

            return value.Trim();
        }
    }
}
