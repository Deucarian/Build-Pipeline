using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;

namespace Deucarian.BuildPipeline
{
    public static partial class DeucarianBuildRunner
    {
        internal static T ExecuteWithLifecycleRestoration<T>(
            DeucarianBuildLifecycleScopeSet lifecycleScopes,
            Func<T> action)
        {
            return ExecuteWithLifecycleRestoration(
                lifecycleScopes,
                action,
                null);
        }

        internal static T ExecuteWithLifecycleRestoration<T>(
            DeucarianBuildLifecycleScopeSet lifecycleScopes,
            Func<T> action,
            Action<T> completeAfterRestoration)
        {
            if (lifecycleScopes == null)
            {
                throw new ArgumentNullException(nameof(lifecycleScopes));
            }

            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            T value = default(T);
            Exception primaryFailure = null;
            try
            {
                value = action();
            }
            catch (Exception exception)
            {
                primaryFailure = exception;
            }

            DeucarianBuildLifecycleRestorationResult restoration =
                lifecycleScopes.RestoreDetailed();
            if (primaryFailure != null)
            {
                if (!restoration.Validation.IsValid)
                {
                    List<Exception> failures = new List<Exception>
                    {
                        primaryFailure
                    };
                    failures.AddRange(restoration.Failures);
                    throw DeucarianSanitizedBuildFailure.From(
                        "The build failed (" +
                        DeucarianBuildLifecycleDiscovery.GetExceptionName(
                            primaryFailure) +
                        ").\n" + restoration.Validation.Format(
                            "Build lifecycle restoration also failed"),
                        new AggregateException(
                            "The build failed and lifecycle restoration also failed.",
                            failures));
                }

                ExceptionDispatchInfo.Capture(primaryFailure).Throw();
                throw new InvalidOperationException(
                    "The primary build failure was not rethrown.");
            }

            if (!restoration.Validation.IsValid)
            {
                throw DeucarianSanitizedBuildFailure.From(
                    restoration.Validation.Format(
                        "Build lifecycle restoration failed"),
                    restoration.ToException(
                        "Build lifecycle restoration failed"));
            }

            completeAfterRestoration?.Invoke(value);
            return value;
        }

        internal static T ExecuteBuildAttempt<T>(
            string outputPath,
            DeucarianBuildLifecycleScopeSet lifecycleScopes,
            Func<T> action,
            Action<T> completeAfterRestoration)
        {
            return ExecuteBuildAttempt(
                outputPath,
                lifecycleScopes,
                null,
                action,
                completeAfterRestoration);
        }

        internal static T ExecuteBuildAttempt<T>(
            string outputPath,
            DeucarianBuildLifecycleScopeSet lifecycleScopes,
            Action prepareOutput,
            Func<T> action,
            Action<T> completeAfterRestoration)
        {
            return ExecuteWithLifecycleRestoration(
                lifecycleScopes,
                () =>
                {
                    prepareOutput?.Invoke();
                    DeucarianBuildManifestStore.Invalidate(outputPath);
                    return action();
                },
                completeAfterRestoration);
        }

        internal static DeucarianBuildRequest CreateWorkingRequest(
            DeucarianBuildRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return new DeucarianBuildRequest(
                request.BuildProfile,
                request.Environment,
                request.OutputPath,
                request.AdditionalBuildOptions);
        }

        internal static void ReloadBuildProfileReference(
            DeucarianBuildRequest request,
            string buildProfileAssetPath)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(buildProfileAssetPath))
            {
                if (request.BuildProfile == null)
                {
                    throw new BuildFailedException(
                        "The Build Profile became unavailable during build preparation.");
                }

                return;
            }

            BuildProfile profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(
                buildProfileAssetPath);
            if (profile == null)
            {
                throw new BuildFailedException(
                    "The Build Profile became unavailable during build preparation at '"
                    + buildProfileAssetPath
                    + "'.");
            }

            request.BuildProfile = profile;
        }
    }
}
