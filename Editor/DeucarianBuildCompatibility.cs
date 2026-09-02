using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace Deucarian.BuildPipeline
{
    /// <summary>
    /// Describes the non-script inputs that must remain stable when an existing
    /// player data build is reused. The fingerprint deliberately excludes C# and
    /// compiled script files so ordinary code-only iteration remains eligible.
    /// </summary>
    internal static class DeucarianBuildCompatibility
    {
        private static readonly HashSet<string> ScriptExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".asmdef",
                ".asmref",
                ".cs",
                ".dll",
                ".mdb",
                ".pdb"
            };

        internal static string CreateFingerprint(
            DeucarianBuildRequest request,
            BuildOptions effectiveBuildOptions)
        {
            if (request == null || request.BuildProfile == null)
            {
                throw new ArgumentException(
                    "A Build Profile request is required to fingerprint build compatibility.",
                    nameof(request));
            }

            BuildProfile profile = request.BuildProfile;
            string profilePath = NormalizeAssetPath(
                AssetDatabase.GetAssetPath(profile));
            SortedSet<string> dataPaths = CollectDataPaths(
                profile,
                profilePath,
                out List<string> scenes);
            StringBuilder canonical = new StringBuilder();
            canonical.Append("schema=1\n");
            canonical.Append("environment=")
                .Append(request.Environment).Append('\n');
            canonical.Append("target=")
                .Append(DeucarianBuildProfileUtility.GetTarget(profile))
                .Append('\n');
            canonical.Append("options=")
                .Append((long)NormalizeOptions(effectiveBuildOptions))
                .Append('\n');
            canonical.Append("profileGuid=")
                .Append(GetAssetGuid(profilePath)).Append('\n');

            for (int index = 0; index < scenes.Count; index++)
            {
                canonical.Append("scene=").Append(index).Append('|')
                    .Append(scenes[index]).Append('\n');
            }

            foreach (string path in dataPaths)
            {
                canonical.Append("asset=").Append(path).Append('|')
                    .Append(GetAssetContentHash(path)).Append('\n');
            }

            return HashText(canonical.ToString());
        }

        internal static BuildOptions GetEffectiveOptions(
            DeucarianBuildRequest request)
        {
            BuildOptions options = request != null
                ? request.AdditionalBuildOptions
                : BuildOptions.None;
            if (request?.BuildProfile != null
                && DeucarianBuildProfileUtility.GetTarget(request.BuildProfile)
                == BuildTarget.WebGL)
            {
                options |= new DeucarianWebGLBuildPolicy()
                    .GetRequiredBuildOptions(request.Environment);
            }

            return options;
        }

        private static SortedSet<string> CollectDataPaths(
            BuildProfile profile,
            string profilePath,
            out List<string> scenes)
        {
            SortedSet<string> paths = new SortedSet<string>(StringComparer.Ordinal);
            AddDataPath(paths, profilePath);

            scenes = new List<string>();
            EditorBuildSettingsScene[] profileScenes = GetEffectiveScenes(profile);
            for (int index = 0; index < profileScenes.Length; index++)
            {
                EditorBuildSettingsScene scene = profileScenes[index];
                if (scene == null || !scene.enabled
                    || string.IsNullOrWhiteSpace(scene.path))
                {
                    continue;
                }

                string scenePath = NormalizeAssetPath(scene.path);
                scenes.Add(scenePath);
                AddDataPath(paths, scenePath);
            }

            if (scenes.Count > 0)
            {
                string[] dependencies = AssetDatabase.GetDependencies(
                    scenes.ToArray(),
                    true);
                for (int index = 0; index < dependencies.Length; index++)
                {
                    AddDataPath(paths, dependencies[index]);
                }
            }

            string[] allPaths = AssetDatabase.GetAllAssetPaths();
            for (int index = 0; index < allPaths.Length; index++)
            {
                string path = NormalizeAssetPath(allPaths[index]);
                if (IsPlayerDataPath(path))
                {
                    AddDataPath(paths, path);
                }
            }

            return paths;
        }

        private static EditorBuildSettingsScene[] GetEffectiveScenes(
            BuildProfile profile)
        {
            SerializedObject serializedProfile = new SerializedObject(profile);
            SerializedProperty overrideProperty =
                serializedProfile.FindProperty("m_OverrideGlobalSceneList")
                ?? serializedProfile.FindProperty("m_OverrideGlobalScenes");
            if (overrideProperty != null && !overrideProperty.boolValue)
            {
                return EditorBuildSettings.scenes
                       ?? Array.Empty<EditorBuildSettingsScene>();
            }

            EditorBuildSettingsScene[] profileScenes = profile.scenes;
            if (overrideProperty != null
                || (profileScenes != null && profileScenes.Length > 0))
            {
                return profileScenes
                       ?? Array.Empty<EditorBuildSettingsScene>();
            }

            return EditorBuildSettings.scenes
                   ?? Array.Empty<EditorBuildSettingsScene>();
        }

        private static bool IsPlayerDataPath(string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)
                && !path.StartsWith("Packages/", StringComparison.Ordinal))
            {
                return false;
            }

            string wrapped = "/" + path + "/";
            return wrapped.IndexOf(
                       "/Resources/",
                       StringComparison.Ordinal) >= 0
                   || wrapped.IndexOf(
                       "/StreamingAssets/",
                       StringComparison.Ordinal) >= 0;
        }

        private static void AddDataPath(ISet<string> paths, string path)
        {
            string normalized = NormalizeAssetPath(path);
            if (string.IsNullOrWhiteSpace(normalized)
                || AssetDatabase.IsValidFolder(normalized)
                || (!IsStreamingAssetsPath(normalized) &&
                    ScriptExtensions.Contains(Path.GetExtension(normalized))))
            {
                return;
            }

            paths.Add(normalized);
        }

        private static string GetAssetContentHash(string assetPath)
        {
            string fullPath = ToProjectFullPath(assetPath);
            if (File.Exists(fullPath))
            {
                // StreamingAssets are copied as raw bytes. Their generated
                // Unity metadata is not a player-data input and may receive a
                // new GUID when a lifecycle contributor recreates a temporary
                // file for a scripts-only build.
                if (IsStreamingAssetsPath(assetPath))
                {
                    return HashFile(fullPath);
                }

                StringBuilder content = new StringBuilder();
                content.Append(HashFile(fullPath));
                string metaPath = fullPath + ".meta";
                if (File.Exists(metaPath))
                {
                    content.Append('|').Append(HashFile(metaPath));
                }

                return HashText(content.ToString());
            }

            return AssetDatabase.GetAssetDependencyHash(assetPath).ToString();
        }

        private static bool IsStreamingAssetsPath(string assetPath)
        {
            string wrapped = "/" + NormalizeAssetPath(assetPath) + "/";
            return wrapped.IndexOf(
                       "/StreamingAssets/",
                       StringComparison.Ordinal) >= 0;
        }

        private static string GetAssetGuid(string assetPath)
        {
            return string.IsNullOrWhiteSpace(assetPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(assetPath);
        }

        private static string HashFile(string path)
        {
            using (SHA256 algorithm = SHA256.Create())
            using (FileStream input = File.OpenRead(path))
            {
                return ToHex(algorithm.ComputeHash(input));
            }
        }

        private static string HashText(string value)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                return ToHex(algorithm.ComputeHash(
                    Encoding.UTF8.GetBytes(value ?? string.Empty)));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder result = new StringBuilder(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; index++)
            {
                result.Append(bytes[index].ToString("x2"));
            }

            return result.ToString();
        }

        private static BuildOptions NormalizeOptions(BuildOptions options)
        {
            return options & ~BuildOptions.BuildScriptsOnly
                           & ~BuildOptions.AutoRunPlayer;
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Trim().Replace('\\', '/');
        }

        private static string ToProjectFullPath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)
                                 ?? string.Empty;
            return Path.GetFullPath(Path.Combine(
                projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}
