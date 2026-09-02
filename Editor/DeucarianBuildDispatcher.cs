using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;

namespace Deucarian.BuildPipeline
{
    /// <summary>
    /// Shared dispatch path used by the manager, Unity Build Profiles, and project CI entry
    /// points. It validates the registered target before invoking project-owned build work.
    /// </summary>
    public static class DeucarianBuildDispatcher
    {
        public static DeucarianBuildResult Build(
            DeucarianBuildManagerTarget target,
            DeucarianBuildInvocation invocation)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (invocation == null)
            {
                throw new ArgumentNullException(nameof(invocation));
            }

            DeucarianBuildValidationResult validation = Validate(target, invocation);
            if (!validation.IsValid)
            {
                throw new BuildFailedException(
                    validation.Format("Registered Deucarian build validation failed"));
            }

            DeucarianBuildResult result =
                target.InvocationBuildAction(invocation);
            if (result == null)
            {
                throw new BuildFailedException(
                    "The registered Deucarian build callback returned no result.");
            }

            if (target.RequireCompleteResult
                && (ReferenceEquals(result.BuildReport, null)
                    || result.ArtifactManifest == null))
            {
                throw new BuildFailedException(
                    "The registered Deucarian build callback did not return a complete "
                    + "runner result with both a Build Report and artifact manifest.");
            }

            return result;
        }

        public static DeucarianBuildResult BuildDefault(
            DeucarianBuildManagerTarget target,
            DeucarianBuildInvocationSource source =
                DeucarianBuildInvocationSource.Programmatic)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            BuildProfile profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(
                target.BuildProfileAssetPath);
            if (profile == null)
            {
                throw new BuildFailedException(
                    "Registered Build Profile is missing at '"
                    + target.BuildProfileAssetPath + "'.");
            }

            return Build(
                target,
                new DeucarianBuildInvocation(
                    profile,
                    target.OutputPath,
                    target.DefaultBuildOptions,
                    source));
        }

        internal static DeucarianBuildValidationResult Validate(
            DeucarianBuildManagerTarget target,
            DeucarianBuildInvocation invocation)
        {
            DeucarianBuildValidationResult result = new DeucarianBuildValidationResult();
            string actualPath = AssetDatabase.GetAssetPath(invocation.BuildProfile);
            if (!string.Equals(
                    NormalizePath(actualPath),
                    NormalizePath(target.BuildProfileAssetPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                result.Add(
                    "The invocation profile '" + actualPath
                    + "' does not match registered profile '"
                    + target.BuildProfileAssetPath + "'.");
                return result;
            }

            result.AddRange(ValidateRequest(
                new DeucarianBuildRequest(
                    invocation.BuildProfile,
                    target.Environment,
                    invocation.OutputPath,
                    invocation.AdditionalBuildOptions),
                target.ProjectValidation,
                requireProjectRelativeOutput: false).Issues);
            return result;
        }

        internal static DeucarianBuildValidationResult ValidateRequest(
            DeucarianBuildRequest request,
            Func<DeucarianBuildValidationResult> projectValidationCallback,
            bool requireProjectRelativeOutput)
        {
            DeucarianBuildValidationResult result =
                new DeucarianBuildValidationResult();
            try
            {
                result.AddRange(DeucarianBuildRunner.Validate(request).Issues);
            }
            catch (Exception exception)
            {
                result.Add(
                    "Build validation failed ("
                    + DeucarianBuildLifecycleDiscovery.GetExceptionName(exception)
                    + ").");
            }

            if (requireProjectRelativeOutput
                && request != null
                && !string.IsNullOrWhiteSpace(request.OutputPath)
                && Path.IsPathRooted(request.OutputPath))
            {
                result.Add("The build output path must be project-relative.");
            }

            if (projectValidationCallback == null)
            {
                return result;
            }

            try
            {
                DeucarianBuildValidationResult projectValidation =
                    projectValidationCallback();
                if (projectValidation == null)
                {
                    result.Add("The project validation callback returned no result.");
                }
                else
                {
                    result.AddRange(projectValidation.Issues);
                }
            }
            catch (Exception exception)
            {
                result.Add(
                    "Project validation failed: "
                    + exception.GetBaseException().Message);
            }

            return result;
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Trim().Replace('\\', '/');
        }
    }
}
