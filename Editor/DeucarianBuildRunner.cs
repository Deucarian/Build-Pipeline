using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;

namespace Deucarian.BuildPipeline
{
    public static partial class DeucarianBuildRunner
    {
        /// <summary>
        /// Passively validates a build request, its platform policy, and every
        /// applicable lifecycle contributor. No profiles, assets, or output
        /// files are changed.
        /// </summary>
        public static DeucarianBuildValidationResult Validate(
            DeucarianBuildRequest request)
        {
            return Evaluate(
                request,
                DeucarianBuildLifecycleDiscovery.Discover()).Validation;
        }

        public static DeucarianBuildResult Build(DeucarianBuildRequest request)
        {
            return Build(request, prepareOutput: false);
        }

        /// <summary>
        /// Explicitly prepares the requested output after applicable lifecycle
        /// contributors have installed their temporary build inputs. This is
        /// the safe entry point for scripts-only builds whose compatibility
        /// fingerprint includes contributor-owned StreamingAssets.
        /// </summary>
        public static DeucarianBuildResult BuildWithOutputPreparation(
            DeucarianBuildRequest request)
        {
            return Build(request, prepareOutput: true);
        }

        private static DeucarianBuildResult Build(
            DeucarianBuildRequest request,
            bool prepareOutput)
        {
            ValidateRequest(request);
            DeucarianBuildRequest workingRequest = CreateWorkingRequest(request);
            string buildProfileAssetPath = AssetDatabase.GetAssetPath(
                workingRequest.BuildProfile);
            BuildEvaluation evaluation = Evaluate(
                workingRequest,
                DeucarianBuildLifecycleDiscovery.Discover());
            if (!evaluation.Validation.IsValid)
            {
                throw new BuildFailedException(
                    evaluation.Validation.Format(
                        "Build validation failed"));
            }

            DeucarianBuildLifecycleScopeSet lifecycleScopes =
                DeucarianBuildLifecyclePipeline.Prepare(
                    workingRequest,
                    evaluation.Lifecycle);
            BuildExecutionResult execution = ExecuteBuildAttempt(
                workingRequest.OutputPath,
                lifecycleScopes,
                () =>
                {
                    ReloadBuildProfileReference(
                        workingRequest,
                        buildProfileAssetPath);
                    if (prepareOutput)
                    {
                        DeucarianBuildOutputUtility.Prepare(workingRequest);
                    }
                },
                () => ExecuteBuild(workingRequest, evaluation),
                value => value.Manifest.WriteTo(
                    DeucarianBuildPathUtility.ToFullOutputPath(
                        workingRequest.OutputPath)));

            return new DeucarianBuildResult
            {
                BuildReport = execution.Report,
                ArtifactManifest = execution.Manifest
            };
        }

        internal static DeucarianBuildValidationResult Validate(
            DeucarianBuildRequest request,
            DeucarianBuildLifecycleDiscoveryResult discovery)
        {
            return Evaluate(request, discovery).Validation;
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

        private static BuildExecutionResult ExecuteBuild(
            DeucarianBuildRequest request,
            BuildEvaluation evaluation)
        {
            BuildReport report;
            using (DeucarianBuildExecutionScope.Enter())
            {
                report = UnityEditor.BuildPipeline.BuildPlayer(
                    new BuildPlayerWithProfileOptions
                    {
                        buildProfile = request.BuildProfile,
                        locationPathName = request.OutputPath,
                        options = evaluation.Options
                    });
            }

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    "Build failed with result " + report.summary.result + ".");
            }

            DeucarianBuildArtifactManifest manifest =
                DeucarianBuildArtifactManifest.Create(
                    request,
                    report,
                    evaluation.SettingsFingerprint,
                    evaluation.BudgetBytes,
                    evaluation.Options);
            DeucarianBuildValidationResult artifactValidation =
                evaluation.Policy.ValidateGeneratedArtifacts(request, manifest);
            if (artifactValidation == null)
            {
                artifactValidation = new DeucarianBuildValidationResult();
                artifactValidation.Add(
                    "The platform policy returned no artifact validation result.");
            }

            artifactValidation.AddRange(
                DeucarianBuildLifecyclePipeline.ValidateArtifacts(
                    request,
                    manifest,
                    evaluation.Lifecycle).Issues);
            if (!artifactValidation.IsValid)
            {
                throw new BuildFailedException(
                    artifactValidation.Format(
                        "Generated artifact validation failed"));
            }

            return new BuildExecutionResult
            {
                Report = report,
                Manifest = manifest
            };
        }

        private static BuildEvaluation Evaluate(
            DeucarianBuildRequest request,
            DeucarianBuildLifecycleDiscoveryResult discovery)
        {
            BuildEvaluation evaluation = new BuildEvaluation();
            AddRequestValidation(evaluation.Validation, request);
            if (!evaluation.Validation.IsValid)
            {
                evaluation.Lifecycle = new DeucarianBuildLifecycleSelection();
                if (discovery == null)
                {
                    evaluation.Validation.Add(
                        "Build lifecycle discovery returned no result.");
                }
                else
                {
                    evaluation.Validation.AddRange(discovery.Issues);
                }

                return evaluation;
            }

            try
            {
                ValidateActiveBuildTarget(
                    request.BuildProfile,
                    EditorUserBuildSettings.activeBuildTarget);
            }
            catch (BuildFailedException exception)
            {
                evaluation.Validation.Add(exception.Message);
            }

            try
            {
                evaluation.Policy = GetPolicy(request.BuildProfile);
                DeucarianBuildValidationResult profileValidation =
                    evaluation.Policy.ValidateProfile(
                        request.BuildProfile,
                        request.Environment);
                if (profileValidation == null)
                {
                    evaluation.Validation.Add(
                        "The platform policy returned no profile validation result.");
                }
                else
                {
                    evaluation.Validation.AddRange(profileValidation.Issues);
                }
            }
            catch (Exception exception)
            {
                evaluation.Validation.Add(
                    "The platform build policy failed validation ("
                    + DeucarianBuildLifecycleDiscovery.GetExceptionName(exception)
                    + ").");
            }

            evaluation.Options = request.AdditionalBuildOptions;
            DeucarianWebGLBuildPolicy webPolicy =
                evaluation.Policy as DeucarianWebGLBuildPolicy;
            if (webPolicy != null)
            {
                evaluation.Options |= webPolicy.GetRequiredBuildOptions(
                    request.Environment);
                evaluation.SettingsFingerprint = webPolicy.GetSettingsFingerprint(
                    request.Environment);
                evaluation.BudgetBytes =
                    DeucarianWebGLBuildPolicy.ProductionBootstrapBudgetBytes;
            }

            try
            {
                ValidateBuildOptions(request.Environment, evaluation.Options);
            }
            catch (BuildFailedException exception)
            {
                evaluation.Validation.Add(exception.Message);
            }

            evaluation.Lifecycle =
                DeucarianBuildLifecyclePipeline.SelectAndValidate(
                    request,
                    discovery);
            evaluation.Validation.AddRange(evaluation.Lifecycle.Validation.Issues);
            return evaluation;
        }

        private static void AddRequestValidation(
            DeucarianBuildValidationResult result,
            DeucarianBuildRequest request)
        {
            if (request == null)
            {
                result.Add("A build request is required.");
                return;
            }

            if (request.BuildProfile == null)
            {
                result.Add("A Build Profile is required.");
            }

            if (string.IsNullOrWhiteSpace(request.OutputPath))
            {
                result.Add("A build output path is required.");
                return;
            }

            try
            {
                string output = DeucarianBuildPathUtility.ToFullOutputPath(
                    request.OutputPath);
                if (File.Exists(output))
                {
                    result.Add(
                        "The build output resolves to a file instead of a directory.");
                }
            }
            catch (Exception)
            {
                result.Add("The build output path is invalid.");
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

        internal static void ValidateActiveBuildTarget(
            BuildProfile profile,
            BuildTarget activeTarget)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            BuildTarget expectedTarget = DeucarianBuildProfileUtility.GetTarget(profile);
            if (activeTarget == expectedTarget)
            {
                return;
            }

            string profilePath = AssetDatabase.GetAssetPath(profile);
            string profileDisplay = string.IsNullOrWhiteSpace(profilePath)
                ? profile.name
                : profilePath;
            string activeProfileArgument = string.IsNullOrWhiteSpace(profilePath)
                ? "-activeBuildProfile \"<Build Profile asset path>\""
                : "-activeBuildProfile \"" + profilePath + "\"";
            throw new BuildFailedException(
                "The active build target is "
                + activeTarget
                + ", but Build Profile '"
                + profileDisplay
                + "' targets "
                + expectedTarget
                + ". Unity compiles target-specific code before this build method runs, "
                + "so the target cannot be switched safely here. Activate the requested Build Profile "
                + "and wait for compilation to finish. For command-line builds, start Unity with "
                + activeProfileArgument
                + " (preferred), or -buildTarget "
                + expectedTarget
                + ".");
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

        private sealed class BuildEvaluation
        {
            internal IDeucarianPlatformBuildPolicy Policy { get; set; }
            internal BuildOptions Options { get; set; }
            internal string SettingsFingerprint { get; set; } = string.Empty;
            internal long BudgetBytes { get; set; } = long.MaxValue;
            internal DeucarianBuildLifecycleSelection Lifecycle { get; set; }
            internal DeucarianBuildValidationResult Validation { get; } =
                new DeucarianBuildValidationResult();
        }

        private sealed class BuildExecutionResult
        {
            internal BuildReport Report { get; set; }
            internal DeucarianBuildArtifactManifest Manifest { get; set; }
        }
    }
}
