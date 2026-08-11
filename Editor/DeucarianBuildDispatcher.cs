using System;
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

            DeucarianBuildResult result;
            using (DeucarianBuildInvocationScope.Enter(
                       invocation.AotSafetyMode))
            {
                result = target.InvocationBuildAction(invocation);
            }

            if (result == null)
            {
                throw new BuildFailedException(
                    "The registered Deucarian build callback returned no result.");
            }

            return result;
        }

        public static DeucarianBuildResult BuildDefault(
            DeucarianBuildManagerTarget target,
            DeucarianBuildInvocationSource source =
                DeucarianBuildInvocationSource.Programmatic,
            DeucarianAotSafetyMode aotSafetyMode =
                DeucarianAotSafetyMode.Inherit)
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
                    source,
                    aotSafetyMode));
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

            try
            {
                result.AddRange(
                    DeucarianBuildRunner.GetPolicy(invocation.BuildProfile)
                        .ValidateProfile(invocation.BuildProfile, target.Environment)
                        .Issues);
            }
            catch (Exception exception)
            {
                result.Add(exception.GetBaseException().Message);
            }

            if (target.ProjectValidation == null)
            {
                return result;
            }

            try
            {
                DeucarianBuildValidationResult projectValidation =
                    target.ProjectValidation();
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
