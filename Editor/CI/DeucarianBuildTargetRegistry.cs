using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;

namespace Deucarian.BuildPipeline
{
    [Serializable]
    public sealed class DeucarianBuildTargetDescriptor
    {
        public string key;
        public string providerId;
        public string providerDisplayName;
        public string targetId;
        public string targetDisplayName;
        public string description;
        public string buildProfileAssetPath;
        public string environment;
        public string defaultOutputPath;
    }

    [Serializable]
    public sealed class DeucarianBuildTargetCatalog
    {
        public bool valid;
        public List<string> issues = new List<string>();
        public List<DeucarianBuildTargetDescriptor> targets =
            new List<DeucarianBuildTargetDescriptor>();
    }

    /// <summary>
    /// Stable programmatic and command-line access to project-owned build workflows.
    /// Uses the same provider registration, validation, and callbacks as the Build
    /// Pipeline Manager and Unity Build Profiles.
    /// </summary>
    public static class DeucarianBuildTargetRegistry
    {
        public static DeucarianBuildTargetCatalog GetCatalog()
        {
            return CreateCatalog(
                DeucarianBuildManagerDiscovery.Discover());
        }

        public static DeucarianBuildValidationResult Validate(
            string targetKey,
            string outputPath = null,
            BuildOptions additionalBuildOptions = BuildOptions.None,
            DeucarianAotSafetyMode aotSafetyMode =
                DeucarianAotSafetyMode.Inherit)
        {
            DeucarianBuildManagerProviderEntry entry =
                ResolveEntry(targetKey);
            DeucarianBuildInvocation invocation = CreateInvocation(
                entry.Target,
                outputPath,
                additionalBuildOptions,
                DeucarianBuildInvocationSource.Programmatic,
                aotSafetyMode);
            DeucarianBuildValidationResult result =
                DeucarianBuildDispatcher.Validate(
                    entry.Target,
                    invocation);

            try
            {
                DeucarianBuildRunner.ValidateActiveBuildTarget(
                    invocation.BuildProfile,
                    EditorUserBuildSettings.activeBuildTarget);
            }
            catch (Exception exception)
            {
                result.Add(exception.GetBaseException().Message);
            }

            return result;
        }

        public static DeucarianBuildResult Build(
            string targetKey,
            string outputPath = null,
            BuildOptions additionalBuildOptions = BuildOptions.None,
            DeucarianAotSafetyMode aotSafetyMode =
                DeucarianAotSafetyMode.Inherit,
            DeucarianBuildInvocationSource source =
                DeucarianBuildInvocationSource.Programmatic)
        {
            DeucarianBuildManagerProviderEntry entry =
                ResolveEntry(targetKey);
            DeucarianBuildInvocation invocation = CreateInvocation(
                entry.Target,
                outputPath,
                additionalBuildOptions,
                source,
                aotSafetyMode);
            DeucarianBuildRunner.ValidateActiveBuildTarget(
                invocation.BuildProfile,
                EditorUserBuildSettings.activeBuildTarget);
            return DeucarianBuildDispatcher.Build(
                entry.Target,
                invocation);
        }

        internal static DeucarianBuildTargetCatalog CreateCatalog(
            DeucarianBuildManagerDiscoveryResult discovery)
        {
            DeucarianBuildTargetCatalog catalog =
                new DeucarianBuildTargetCatalog();
            if (discovery == null)
            {
                catalog.issues.Add(
                    "Build target discovery returned no result.");
                return catalog;
            }

            catalog.issues.AddRange(discovery.Issues);
            for (int i = 0; i < discovery.Entries.Count; i++)
            {
                DeucarianBuildManagerProviderEntry entry =
                    discovery.Entries[i];
                catalog.targets.Add(new DeucarianBuildTargetDescriptor
                {
                    key = entry.Key,
                    providerId = entry.Provider.Id,
                    providerDisplayName = entry.Provider.DisplayName,
                    targetId = entry.Target.Id,
                    targetDisplayName = entry.Target.DisplayName,
                    description = entry.Target.Description,
                    buildProfileAssetPath =
                        entry.Target.BuildProfileAssetPath,
                    environment = entry.Target.Environment.ToString(),
                    defaultOutputPath = entry.Target.OutputPath
                });
            }

            catalog.valid = catalog.issues.Count == 0;
            return catalog;
        }

        internal static DeucarianBuildManagerProviderEntry ResolveEntry(
            string targetKey,
            DeucarianBuildManagerDiscoveryResult discovery = null)
        {
            if (string.IsNullOrWhiteSpace(targetKey))
            {
                throw new ArgumentException(
                    "A registered build target key is required.",
                    nameof(targetKey));
            }

            DeucarianBuildManagerDiscoveryResult resolvedDiscovery =
                discovery ?? DeucarianBuildManagerDiscovery.Discover();
            if (resolvedDiscovery == null)
            {
                throw new BuildFailedException(
                    "Build target discovery returned no result.");
            }

            if (resolvedDiscovery.Issues.Count > 0)
            {
                throw new BuildFailedException(
                    "Registered build target discovery failed:\n- "
                    + string.Join("\n- ", resolvedDiscovery.Issues));
            }

            string normalizedKey = targetKey.Trim();
            DeucarianBuildManagerProviderEntry match = null;
            for (int i = 0; i < resolvedDiscovery.Entries.Count; i++)
            {
                DeucarianBuildManagerProviderEntry entry =
                    resolvedDiscovery.Entries[i];
                if (!string.Equals(
                        entry.Key,
                        normalizedKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (match != null)
                {
                    throw new BuildFailedException(
                        "Multiple registered build targets match '"
                        + normalizedKey + "'.");
                }

                match = entry;
            }

            if (match == null)
            {
                throw new BuildFailedException(
                    "No registered Deucarian build target matches '"
                    + normalizedKey + "'.");
            }

            return match;
        }

        private static DeucarianBuildInvocation CreateInvocation(
            DeucarianBuildManagerTarget target,
            string outputPath,
            BuildOptions additionalBuildOptions,
            DeucarianBuildInvocationSource source,
            DeucarianAotSafetyMode aotSafetyMode)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            string resolvedOutput = string.IsNullOrWhiteSpace(outputPath)
                ? target.OutputPath
                : outputPath.Trim();
            BuildOptions resolvedOptions =
                target.DefaultBuildOptions | additionalBuildOptions;
            bool hasOutputOverride = !string.Equals(
                resolvedOutput,
                target.OutputPath,
                StringComparison.Ordinal);
            bool hasOptionsOverride =
                resolvedOptions != target.DefaultBuildOptions;
            if (!target.SupportsInvocationOverrides
                && (hasOutputOverride || hasOptionsOverride))
            {
                throw new BuildFailedException(
                    "Registered target '" + target.Id
                    + "' uses the legacy build callback and cannot accept "
                    + "command-line output or option overrides.");
            }

            BuildProfile profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(
                target.BuildProfileAssetPath);
            if (profile == null)
            {
                throw new BuildFailedException(
                    "Registered Build Profile is missing at '"
                    + target.BuildProfileAssetPath + "'.");
            }

            return new DeucarianBuildInvocation(
                profile,
                resolvedOutput,
                resolvedOptions,
                source,
                aotSafetyMode);
        }
    }
}
