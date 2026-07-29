using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Profile;

namespace Deucarian.BuildPipeline
{
    public enum DeucarianBuildInvocationSource
    {
        BuildPipelineManager,
        UnityBuildProfiles,
        CommandLine,
        Programmatic
    }

    /// <summary>
    /// Describes one requested build independently of the editor surface that initiated it.
    /// Project callbacks must use these values so Unity's native Build and Build And Run
    /// actions retain their selected output and options.
    /// </summary>
    public sealed class DeucarianBuildInvocation
    {
        public DeucarianBuildInvocation(
            BuildProfile buildProfile,
            string outputPath,
            BuildOptions additionalBuildOptions,
            DeucarianBuildInvocationSource source)
        {
            BuildProfile = buildProfile != null
                ? buildProfile
                : throw new ArgumentNullException(nameof(buildProfile));
            OutputPath = Require(outputPath, nameof(outputPath));
            AdditionalBuildOptions = additionalBuildOptions;
            Source = source;
        }

        public BuildProfile BuildProfile { get; }

        public string OutputPath { get; }

        public BuildOptions AdditionalBuildOptions { get; }

        public DeucarianBuildInvocationSource Source { get; }

        private static string Require(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A non-empty value is required.", parameterName);
            }

            return value.Trim();
        }
    }

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
            Func<DeucarianBuildInvocation, DeucarianBuildResult> buildAction,
            Func<DeucarianBuildValidationResult> projectValidation = null,
            BuildOptions defaultBuildOptions = BuildOptions.None)
        {
            Id = Require(id, nameof(id));
            DisplayName = Require(displayName, nameof(displayName));
            Description = description ?? string.Empty;
            BuildProfileAssetPath = Require(buildProfileAssetPath, nameof(buildProfileAssetPath));
            OutputPath = Require(outputPath, nameof(outputPath));
            InvocationBuildAction =
                buildAction ?? throw new ArgumentNullException(nameof(buildAction));
            ProjectValidation = projectValidation;
            Environment = environment;
            DefaultBuildOptions = defaultBuildOptions;
            SupportsInvocationOverrides = true;
            BuildAction = () => DeucarianBuildDispatcher.BuildDefault(this);
        }

        [Obsolete(
            "Use the DeucarianBuildInvocation callback overload so Unity Build Profiles "
            + "can preserve the selected output path and Build options.")]
        public DeucarianBuildManagerTarget(
            string id,
            string displayName,
            string description,
            string buildProfileAssetPath,
            DeucarianBuildEnvironment environment,
            string outputPath,
            Func<DeucarianBuildResult> buildAction,
            Func<DeucarianBuildValidationResult> projectValidation = null)
            : this(
                id,
                displayName,
                description,
                buildProfileAssetPath,
                environment,
                outputPath,
                invocation => buildAction(),
                projectValidation)
        {
            if (buildAction == null)
            {
                throw new ArgumentNullException(nameof(buildAction));
            }

            SupportsInvocationOverrides = false;
        }

        public string Id { get; }

        public string DisplayName { get; }

        public string Description { get; }

        public string BuildProfileAssetPath { get; }

        public DeucarianBuildEnvironment Environment { get; }

        public string OutputPath { get; }

        public BuildOptions DefaultBuildOptions { get; }

        /// <summary>
        /// Compatibility entry point for callers using the pre-0.3 target contract.
        /// New integrations should use DeucarianBuildDispatcher.
        /// </summary>
        public Func<DeucarianBuildResult> BuildAction { get; }

        public Func<DeucarianBuildInvocation, DeucarianBuildResult>
            InvocationBuildAction { get; }

        public Func<DeucarianBuildValidationResult> ProjectValidation { get; }

        internal bool SupportsInvocationOverrides { get; private set; }

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
