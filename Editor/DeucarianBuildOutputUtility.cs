using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Deucarian.BuildPipeline
{
    /// <summary>
    /// Performs conservative, ownership-aware preparation of a build output.
    /// Callers opt in explicitly; the build runner never cleans automatically.
    /// </summary>
    public static class DeucarianBuildOutputUtility
    {
        public static DeucarianBuildValidationResult ValidatePreparation(
            string outputPath,
            BuildOptions buildOptions)
        {
            return ValidatePreparation(outputPath, buildOptions, null);
        }

        /// <summary>
        /// Validates output preparation against the request that will actually
        /// be built. Scripts-only builds require this overload so the existing
        /// output can be matched to the same profile and environment.
        /// </summary>
        public static DeucarianBuildValidationResult ValidatePreparation(
            DeucarianBuildRequest request)
        {
            if (request == null)
            {
                DeucarianBuildValidationResult missing =
                    new DeucarianBuildValidationResult();
                missing.Add("A build request is required.");
                return missing;
            }

            return ValidatePreparation(
                request.OutputPath,
                request.AdditionalBuildOptions,
                request);
        }

        private static DeucarianBuildValidationResult ValidatePreparation(
            string outputPath,
            BuildOptions buildOptions,
            DeucarianBuildRequest request)
        {
            DeucarianBuildValidationResult result =
                new DeucarianBuildValidationResult();
            string fullPath;
            try
            {
                fullPath = DeucarianBuildOutputPathSafety.NormalizeDirectoryPath(
                    DeucarianBuildPathUtility.ToFullOutputPath(outputPath));
            }
            catch (Exception)
            {
                result.Add("The build output path is missing or invalid.");
                return result;
            }

            if (!DeucarianBuildOutputPathSafety.TryValidate(
                    fullPath,
                    out string boundaryIssue))
            {
                result.Add(boundaryIssue);
                return result;
            }

            if (File.Exists(fullPath))
            {
                result.Add(
                    "The build output resolves to a file instead of a directory.");
                return result;
            }

            bool scriptsOnly =
                (buildOptions & BuildOptions.BuildScriptsOnly) != 0;
            if (!Directory.Exists(fullPath))
            {
                if (scriptsOnly)
                {
                    result.Add(
                        "A scripts-only build requires an existing Deucarian-owned "
                        + "output with a valid build manifest.");
                }

                return result;
            }

            bool hasManifest = HasValidManifest(fullPath);
            if (scriptsOnly)
            {
                if (!hasManifest)
                {
                    result.Add(
                        "A scripts-only build requires an existing Deucarian-owned "
                        + "output with a valid build manifest.");
                }
                else if (request == null)
                {
                    result.Add(
                        "A scripts-only build requires request-aware output "
                        + "validation so its profile and environment can be matched.");
                }
                else
                {
                    AddScriptsOnlyCompatibilityIssues(
                        result,
                        fullPath,
                        request);
                }

                return result;
            }

            bool empty;
            try
            {
                empty = Directory.GetFileSystemEntries(fullPath).Length == 0;
            }
            catch (Exception)
            {
                result.Add(
                    "The build output could not be inspected safely.");
                return result;
            }
            if (!DeucarianBuildOutputPathSafety.IsStrictBuildsChild(fullPath)
                && !empty && !hasManifest)
            {
                result.Add(
                    "Refusing to replace the non-empty output because it is outside "
                    + "the project Builds directory and has no valid Deucarian manifest.");
            }

            return result;
        }

        public static void Prepare(string outputPath, BuildOptions buildOptions)
        {
            DeucarianBuildValidationResult validation =
                ValidatePreparation(outputPath, buildOptions);
            if (!validation.IsValid)
            {
                throw new BuildFailedException(
                    validation.Format("Build output preparation failed"));
            }

            if ((buildOptions & BuildOptions.BuildScriptsOnly) != 0)
            {
                return;
            }

            string fullPath = DeucarianBuildOutputPathSafety.NormalizeDirectoryPath(
                DeucarianBuildPathUtility.ToFullOutputPath(outputPath));
            DeleteExistingOutputWhenOwned(fullPath);
        }

        /// <summary>
        /// Prepares the output selected by an actual build request. Use this
        /// overload for scripts-only builds so manifest compatibility is
        /// validated before the existing output is preserved.
        /// </summary>
        public static void Prepare(DeucarianBuildRequest request)
        {
            DeucarianBuildValidationResult validation =
                ValidatePreparation(request);
            if (!validation.IsValid)
            {
                throw new BuildFailedException(
                    validation.Format("Build output preparation failed"));
            }

            if ((request.AdditionalBuildOptions
                 & BuildOptions.BuildScriptsOnly) != 0)
            {
                return;
            }

            string fullPath = DeucarianBuildOutputPathSafety.NormalizeDirectoryPath(
                DeucarianBuildPathUtility.ToFullOutputPath(
                    request.OutputPath));
            DeleteExistingOutputWhenOwned(fullPath);
        }

        internal static bool HasValidManifest(string outputFullPath)
        {
            return TryReadOwnedManifest(outputFullPath, out _);
        }

        private static void AddScriptsOnlyCompatibilityIssues(
            DeucarianBuildValidationResult result,
            string outputFullPath,
            DeucarianBuildRequest request)
        {
            if (!TryReadOwnedManifest(
                    outputFullPath,
                    out DeucarianBuildArtifactManifest manifest))
            {
                result.Add(
                    "A scripts-only build requires an existing Deucarian-owned "
                    + "output with a valid build manifest.");
                return;
            }

            string profilePath = request.BuildProfile == null
                ? string.Empty
                : AssetDatabase.GetAssetPath(request.BuildProfile);
            string profileGuid = string.IsNullOrWhiteSpace(profilePath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(profilePath);
            if (string.IsNullOrWhiteSpace(profileGuid))
            {
                result.Add(
                    "A scripts-only build requires a persistent Build Profile asset.");
                return;
            }

            string fingerprint;
            string compatibilityFingerprint;
            try
            {
                IDeucarianPlatformBuildPolicy policy =
                    DeucarianBuildRunner.GetPolicy(request.BuildProfile);
                DeucarianWebGLBuildPolicy webPolicy =
                    policy as DeucarianWebGLBuildPolicy;
                fingerprint = webPolicy == null
                    ? string.Empty
                    : webPolicy.GetSettingsFingerprint(request.Environment);
                compatibilityFingerprint =
                    DeucarianBuildCompatibility.CreateFingerprint(
                        request,
                        DeucarianBuildCompatibility.GetEffectiveOptions(request));
            }
            catch (Exception)
            {
                result.Add(
                    "The scripts-only build policy could not be resolved.");
                return;
            }

            if (manifest.schemaVersion !=
                DeucarianBuildArtifactManifest.CurrentSchemaVersion)
            {
                result.Add(
                    "The existing output uses an incompatible manifest schema.");
            }

            if (!string.Equals(
                    manifest.packageVersion,
                    DeucarianBuildPackage.Version,
                    StringComparison.Ordinal))
            {
                result.Add(
                    "The existing output was built by a different Build Pipeline version.");
            }

            if (!string.Equals(
                    manifest.unityVersion,
                    Application.unityVersion,
                    StringComparison.Ordinal))
            {
                result.Add(
                    "The existing output was built by a different Unity version.");
            }

            if (!string.Equals(
                    manifest.environment,
                    request.Environment.ToString(),
                    StringComparison.Ordinal))
            {
                result.Add(
                    "The existing output belongs to a different build environment.");
            }

            if (!string.Equals(
                    manifest.buildProfileGuid,
                    profileGuid,
                    StringComparison.Ordinal))
            {
                result.Add(
                    "The existing output belongs to a different Build Profile.");
            }

            if (!string.Equals(
                    manifest.settingsFingerprint ?? string.Empty,
                    fingerprint,
                    StringComparison.Ordinal))
            {
                result.Add(
                    "The existing output uses incompatible platform settings.");
            }

            if (!string.Equals(
                    manifest.compatibilityFingerprint ?? string.Empty,
                    compatibilityFingerprint,
                    StringComparison.Ordinal))
            {
                result.Add(
                    "The existing output uses incompatible profile, scene, data, "
                    + "or build-option inputs.");
            }
        }

        private static bool TryReadOwnedManifest(
            string outputFullPath,
            out DeucarianBuildArtifactManifest manifest)
        {
            manifest = null;
            string manifestPath = Path.Combine(
                outputFullPath,
                DeucarianBuildArtifactManifest.FileName);
            if (!File.Exists(manifestPath))
            {
                return false;
            }

            try
            {
                manifest = JsonUtility.FromJson<
                    DeucarianBuildArtifactManifest>(
                    File.ReadAllText(manifestPath));
                return manifest != null
                       && manifest.schemaVersion > 0
                       && !string.IsNullOrWhiteSpace(manifest.packageVersion)
                       && !string.IsNullOrWhiteSpace(manifest.buildGuid);
            }
            catch (Exception)
            {
                manifest = null;
                return false;
            }
        }

        private static void DeleteExistingOutputWhenOwned(string fullPath)
        {
            DeleteExistingOutputWhenOwned(
                fullPath,
                File.GetAttributes,
                Directory.GetFileSystemEntries,
                Directory.Delete);
        }

        internal static void DeleteExistingOutputWhenOwned(
            string fullPath,
            Func<string, FileAttributes> getAttributes,
            Func<string, string[]> getEntries,
            Action<string, bool> deleteDirectory)
        {
            if (!Directory.Exists(fullPath)
                || (!DeucarianBuildOutputPathSafety.IsStrictBuildsChild(fullPath)
                    && !HasValidManifest(fullPath)))
            {
                return;
            }

            if (!DeucarianBuildOutputPathSafety.TryValidate(
                    fullPath,
                    getAttributes,
                    getEntries,
                    out string issue))
            {
                throw new BuildFailedException(
                    "Build output preparation failed:\n- " + issue);
            }

            if (deleteDirectory == null)
            {
                throw new ArgumentNullException(nameof(deleteDirectory));
            }

            deleteDirectory(fullPath, true);
        }
    }
}
