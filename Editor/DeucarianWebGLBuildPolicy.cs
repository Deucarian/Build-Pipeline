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

            using (DeucarianBuildProfileUtility.ActivateTemporarily(profile))
            {
                bool development = environment == DeucarianBuildEnvironment.Development;
                Expect(
                    result,
                    "WebGL compression",
                    development ? WebGLCompressionFormat.Disabled : WebGLCompressionFormat.Brotli,
                    PlayerSettings.WebGL.compressionFormat);
                Expect(result, "hashed filenames", !development, PlayerSettings.WebGL.nameFilesAsHashes);
                Expect(result, "data caching", !development, PlayerSettings.WebGL.dataCaching);
                Expect(result, "decompression fallback", false, PlayerSettings.WebGL.decompressionFallback);
                Expect(
                    result,
                    "debug symbol mode",
                    development ? WebGLDebugSymbolMode.External : WebGLDebugSymbolMode.Off,
                    PlayerSettings.WebGL.debugSymbolMode);
                Expect(result, "diagnostics", development, PlayerSettings.WebGL.showDiagnostics);
                Expect(
                    result,
                    "exception support",
                    development
                        ? WebGLExceptionSupport.FullWithStacktrace
                        : WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly,
                    PlayerSettings.WebGL.exceptionSupport);
                Expect(
                    result,
                    "managed stripping",
                    development ? ManagedStrippingLevel.Minimal : ManagedStrippingLevel.High,
                    PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.WebGL));
                Expect(
                    result,
                    "IL2CPP code generation",
                    Il2CppCodeGeneration.OptimizeSize,
                    PlayerSettings.GetIl2CppCodeGeneration(NamedBuildTarget.WebGL));
                Expect(result, "engine stripping", true, PlayerSettings.stripEngineCode);
                Expect(result, "WebAssembly 2023", true, PlayerSettings.WebGL.wasm2023);
                Expect(result, "threads", false, PlayerSettings.WebGL.threadsSupport);
                Expect(
                    result,
                    "API compatibility",
                    ApiCompatibilityLevel.NET_Standard,
                    PlayerSettings.GetApiCompatibilityLevel(NamedBuildTarget.WebGL));
            }

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
                ? BuildOptions.Development | BuildOptions.AutoRunPlayer | BuildOptions.DetailedBuildReport
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

        private static string FormatBytes(long bytes)
        {
            return (bytes / (1024d * 1024d)).ToString("0.00") + " MiB";
        }
    }
}
