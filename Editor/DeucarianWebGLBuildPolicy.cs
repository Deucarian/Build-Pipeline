using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace Deucarian.BuildPipeline
{
    public sealed class DeucarianWebGLBuildPolicy : IDeucarianPlatformBuildPolicy
    {
        public const long ProductionBootstrapBudgetBytes = 20L * 1024L * 1024L;

        private static readonly Regex HashedPayloadName = new Regex(
            @"(^|[._-])[0-9a-f]{8,}([._-]|$)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public BuildTarget Target => BuildTarget.WebGL;

        public void ApplySettings(BuildProfile profile, DeucarianBuildEnvironment environment)
        {
            ValidateProfileTarget(profile);
            DeucarianBuildProfileUtility.EnsurePlayerSettingsOverride(profile);

            using (DeucarianBuildProfileUtility.ActivateTemporarily(profile))
            {
                bool development = environment == DeucarianBuildEnvironment.Development;
                PlayerSettings.WebGL.compressionFormat = development
                    ? WebGLCompressionFormat.Disabled
                    : WebGLCompressionFormat.Brotli;
                PlayerSettings.WebGL.nameFilesAsHashes = !development;
                PlayerSettings.WebGL.dataCaching = !development;
                PlayerSettings.WebGL.decompressionFallback = false;
                PlayerSettings.WebGL.debugSymbolMode = development
                    ? WebGLDebugSymbolMode.External
                    : WebGLDebugSymbolMode.Off;
                PlayerSettings.WebGL.showDiagnostics = development;
                PlayerSettings.WebGL.exceptionSupport = development
                    ? WebGLExceptionSupport.FullWithStacktrace
                    : WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
                PlayerSettings.SetManagedStrippingLevel(
                    NamedBuildTarget.WebGL,
                    development ? ManagedStrippingLevel.Minimal : ManagedStrippingLevel.High);
                PlayerSettings.SetIl2CppCodeGeneration(
                    NamedBuildTarget.WebGL,
                    Il2CppCodeGeneration.OptimizeSize);
                PlayerSettings.stripEngineCode = true;
                PlayerSettings.WebGL.wasm2023 = true;
                PlayerSettings.WebGL.threadsSupport = false;
                PlayerSettings.SetApiCompatibilityLevel(
                    NamedBuildTarget.WebGL,
                    ApiCompatibilityLevel.NET_Standard);
                DeucarianBuildProfileUtility.PersistPlayerSettingsOverride(profile);
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        public DeucarianBuildValidationResult ValidateProfile(
            BuildProfile profile,
            DeucarianBuildEnvironment environment)
        {
            DeucarianBuildValidationResult result = new DeucarianBuildValidationResult();
            if (profile == null)
            {
                result.Add("A Build Profile is required.");
                return result;
            }

            BuildTarget target = DeucarianBuildProfileUtility.GetTarget(profile);
            if (target != BuildTarget.WebGL)
            {
                result.Add("The selected profile targets " + target + " instead of WebGL.");
                return result;
            }

            if (!DeucarianBuildProfileSettingsSnapshot.TryCreate(
                    profile,
                    out DeucarianBuildProfileSettingsSnapshot settings,
                    out string issue))
            {
                result.Add(issue);
                return result;
            }

            bool development = environment == DeucarianBuildEnvironment.Development;
            ExpectEnum(
                result,
                settings,
                "WebGL compression",
                "webGLCompressionFormat",
                development ? WebGLCompressionFormat.Disabled : WebGLCompressionFormat.Brotli);
            ExpectBool(result, settings, "hashed filenames", "webGLNameFilesAsHashes", !development);
            ExpectBool(result, settings, "data caching", "webGLDataCaching", !development);
            ExpectBool(result, settings, "decompression fallback", "webGLDecompressionFallback", false);
            ExpectEnum(
                result,
                settings,
                "debug symbol mode",
                "webGLDebugSymbols",
                development ? WebGLDebugSymbolMode.External : WebGLDebugSymbolMode.Off);
            ExpectBool(result, settings, "diagnostics", "webGLShowDiagnostics", development);
            ExpectEnum(
                result,
                settings,
                "exception support",
                "webGLExceptionSupport",
                development
                    ? WebGLExceptionSupport.FullWithStacktrace
                    : WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly);
            ExpectSectionEnum(
                result,
                settings,
                "managed stripping",
                "managedStrippingLevel",
                "WebGL",
                development ? ManagedStrippingLevel.Minimal : ManagedStrippingLevel.High);
            ExpectSectionEnum(
                result,
                settings,
                "IL2CPP code generation",
                "il2cppCodeGeneration",
                "WebGL",
                Il2CppCodeGeneration.OptimizeSize);
            ExpectBool(result, settings, "engine stripping", "stripEngineCode", true);
            ExpectBool(result, settings, "WebAssembly 2023", "webWasm2023", true);
            ExpectBool(result, settings, "threads", "webGLThreadsSupport", false);
            ExpectEnum(
                result,
                settings,
                "API compatibility",
                "apiCompatibilityLevel",
                ApiCompatibilityLevel.NET_Standard);

            return result;
        }

        public DeucarianBuildValidationResult ValidateGeneratedArtifacts(
            DeucarianBuildRequest request,
            DeucarianBuildArtifactManifest manifest)
        {
            DeucarianBuildValidationResult result = new DeucarianBuildValidationResult();
            if (request.Environment != DeucarianBuildEnvironment.Production)
            {
                return result;
            }

            int compressedPayloadCount = 0;
            for (int i = 0; i < manifest.artifacts.Count; i++)
            {
                DeucarianBuildArtifact artifact = manifest.artifacts[i];
                string path = artifact.relativePath.Replace('\\', '/');
                string lower = path.ToLowerInvariant();
                bool generatedPayload = artifact.classification == "wasm"
                                        || artifact.classification == "data"
                                        || artifact.classification == "framework";

                if (generatedPayload)
                {
                    if (artifact.encoding != "br")
                    {
                        result.Add("Generated production payload is not Brotli encoded: " + path + ".");
                    }
                    else
                    {
                        compressedPayloadCount++;
                    }

                    string fileName = Path.GetFileName(path);
                    if (!HashedPayloadName.IsMatch(fileName))
                    {
                        result.Add("Generated production payload is not hash-named: " + path + ".");
                    }
                }

                if (artifact.classification == "debug-symbols"
                    || lower.EndsWith(".symbols.json", StringComparison.Ordinal)
                    || lower.EndsWith(".map", StringComparison.Ordinal))
                {
                    result.Add("Production output contains debug symbols: " + path + ".");
                }

                if (lower.Contains("dev-context") || lower.Contains("development-context"))
                {
                    result.Add("Production output contains a development context: " + path + ".");
                }
            }

            if (compressedPayloadCount < 3)
            {
                result.Add("Production output is missing one or more Brotli data, framework, or WebAssembly payloads.");
            }

            if (!manifest.budget.passed)
            {
                result.Add(
                    "Encoded pre-engine bootstrap is "
                    + FormatBytes(manifest.budget.encodedBootstrapBytes)
                    + ", above the " + FormatBytes(manifest.budget.limitBytes) + " production limit.");
            }

            return result;
        }

        public string GetSettingsFingerprint(DeucarianBuildEnvironment environment)
        {
            string canonical = string.Join(
                "\n",
                GetExpectedSettings(environment));
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                StringBuilder result = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                {
                    result.Append(bytes[i].ToString("x2"));
                }

                return result.ToString();
            }
        }

        internal BuildOptions GetRequiredBuildOptions(DeucarianBuildEnvironment environment)
        {
            return environment == DeucarianBuildEnvironment.Development
                ? BuildOptions.Development | BuildOptions.DetailedBuildReport
                : BuildOptions.StrictMode | BuildOptions.DetailedBuildReport;
        }

        internal IReadOnlyList<string> GetExpectedSettings(DeucarianBuildEnvironment environment)
        {
            bool development = environment == DeucarianBuildEnvironment.Development;
            return new[]
            {
                "apiCompatibility=NET_Standard_2_1",
                "compression=" + (development ? "Disabled" : "Brotli"),
                "dataCaching=" + (!development),
                "debugSymbols=" + (development ? "External" : "Off"),
                "decompressionFallback=False",
                "diagnostics=" + development,
                "engineStripping=True",
                "exceptionSupport=" + (development ? "FullWithStacktrace" : "ExplicitlyThrownExceptionsOnly"),
                "hashedFilenames=" + (!development),
                "il2cpp=OptimizeSize",
                "managedStripping=" + (development ? "Minimal" : "High"),
                "threads=False",
                "wasm2023=True"
            };
        }

        private static void ValidateProfileTarget(BuildProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            BuildTarget target = DeucarianBuildProfileUtility.GetTarget(profile);
            if (target != BuildTarget.WebGL)
            {
                throw new InvalidOperationException(
                    "Build Profile targets " + target + ", but the WebGL policy requires WebGL.");
            }
        }

        private static void Expect<T>(
            DeucarianBuildValidationResult result,
            string setting,
            T expected,
            T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                result.Add(setting + " drifted: expected " + expected + ", found " + actual + ".");
            }
        }

        private static void ExpectBool(
            DeucarianBuildValidationResult result,
            DeucarianBuildProfileSettingsSnapshot settings,
            string setting,
            string key,
            bool expected)
        {
            if (!settings.TryGetBool(key, out bool actual))
            {
                result.Add(setting + " could not be read from the Build Profile override.");
                return;
            }

            Expect(result, setting, expected, actual);
        }

        private static void ExpectEnum<T>(
            DeucarianBuildValidationResult result,
            DeucarianBuildProfileSettingsSnapshot settings,
            string setting,
            string key,
            T expected)
            where T : struct
        {
            if (!settings.TryGetInt(key, out int serialized))
            {
                result.Add(setting + " could not be read from the Build Profile override.");
                return;
            }

            T actual = (T)Enum.ToObject(typeof(T), serialized);
            Expect(result, setting, expected, actual);
        }

        private static void ExpectSectionEnum<T>(
            DeucarianBuildValidationResult result,
            DeucarianBuildProfileSettingsSnapshot settings,
            string setting,
            string section,
            string key,
            T expected)
            where T : struct
        {
            if (!settings.TryGetSectionInt(section, key, out int serialized))
            {
                result.Add(setting + " could not be read from the Build Profile override.");
                return;
            }

            T actual = (T)Enum.ToObject(typeof(T), serialized);
            Expect(result, setting, expected, actual);
        }

        private static string FormatBytes(long bytes)
        {
            return (bytes / (1024d * 1024d)).ToString("0.00") + " MiB";
        }
    }
}
