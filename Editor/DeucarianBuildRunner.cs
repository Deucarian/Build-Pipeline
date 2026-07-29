using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;

namespace Deucarian.BuildPipeline
{
    public static class DeucarianBuildRunner
    {
        public static DeucarianBuildResult Build(DeucarianBuildRequest request)
        {
            ValidateRequest(request);
            IDeucarianPlatformBuildPolicy policy = GetPolicy(request.BuildProfile);
            DeucarianBuildValidationResult profileValidation = policy.ValidateProfile(
                request.BuildProfile,
                request.Environment);
            if (!profileValidation.IsValid)
            {
                throw new BuildFailedException(
                    profileValidation.Format(
                        "Build Profile drift detected. Apply the policy explicitly and commit the profile"));
            }

            BuildOptions options = request.AdditionalBuildOptions;
            string fingerprint;
            long budgetBytes = long.MaxValue;
            DeucarianWebGLBuildPolicy webPolicy = policy as DeucarianWebGLBuildPolicy;
            if (webPolicy != null)
            {
                options |= webPolicy.GetRequiredBuildOptions(request.Environment);
                fingerprint = webPolicy.GetSettingsFingerprint(request.Environment);
                budgetBytes = DeucarianWebGLBuildPolicy.ProductionBootstrapBudgetBytes;
            }
            else
            {
                fingerprint = string.Empty;
            }

            ValidateBuildOptions(request.Environment, options);
            BuildReport report;
            using (DeucarianBuildExecutionScope.Enter())
            {
                report = UnityEditor.BuildPipeline.BuildPlayer(
                    new BuildPlayerWithProfileOptions
                    {
                        buildProfile = request.BuildProfile,
                        locationPathName = request.OutputPath,
                        options = options
                    });
            }
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    "Build failed with result " + report.summary.result + ".");
            }

            DeucarianBuildArtifactManifest manifest = DeucarianBuildArtifactManifest.Create(
                request,
                report,
                fingerprint,
                budgetBytes);
            DeucarianBuildValidationResult artifactValidation = policy.ValidateGeneratedArtifacts(
                request,
                manifest);
            manifest.WriteTo(DeucarianBuildPathUtility.ToFullOutputPath(request.OutputPath));
            if (!artifactValidation.IsValid)
            {
                throw new BuildFailedException(
                    artifactValidation.Format("Generated artifact validation failed"));
            }

            return new DeucarianBuildResult
            {
                BuildReport = report,
                ArtifactManifest = manifest
            };
        }

        public static IDeucarianPlatformBuildPolicy GetPolicy(BuildProfile profile)
        {
            BuildTarget target = DeucarianBuildProfileUtility.GetTarget(profile);
            if (target == BuildTarget.WebGL)
            {
                return new DeucarianWebGLBuildPolicy();
            }

            throw new NotSupportedException(
                "No Deucarian build policy is registered for target " + target + ".");
        }

        public static void ApplyPolicy(
            BuildProfile profile,
            DeucarianBuildEnvironment environment)
        {
            IDeucarianPlatformBuildPolicy policy = GetPolicy(profile);
            policy.ApplySettings(profile, environment);
            DeucarianBuildValidationResult validation = policy.ValidateProfile(profile, environment);
            if (!validation.IsValid)
            {
                throw new BuildFailedException(validation.Format("Profile synchronization failed"));
            }
        }

        private static void ValidateRequest(DeucarianBuildRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (request.BuildProfile == null)
            {
                throw new ArgumentException("A Build Profile is required.", nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.OutputPath))
            {
                throw new ArgumentException("A build output path is required.", nameof(request));
            }

            string output = DeucarianBuildPathUtility.ToFullOutputPath(request.OutputPath);
            if (File.Exists(output))
            {
                throw new ArgumentException(
                    "The build output resolves to a file instead of a directory.",
                    nameof(request));
            }
        }

        private static void ValidateBuildOptions(
            DeucarianBuildEnvironment environment,
            BuildOptions options)
        {
            bool development = (options & BuildOptions.Development) != 0;
            if (environment == DeucarianBuildEnvironment.Production && development)
            {
                throw new BuildFailedException(
                    "Production requests cannot include BuildOptions.Development.");
            }

            if (environment == DeucarianBuildEnvironment.Development && !development)
            {
                throw new BuildFailedException(
                    "Development requests must include BuildOptions.Development.");
            }
        }
    }
}
