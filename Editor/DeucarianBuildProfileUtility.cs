using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace Deucarian.BuildPipeline
{
    public static class DeucarianBuildProfileUtility
    {
        public static BuildProfile CreateProfile(BuildTarget target, string assetPath)
        {
            if (target == BuildTarget.NoTarget)
            {
                throw new ArgumentException("A concrete build target is required.", nameof(target));
            }

            if (string.IsNullOrWhiteSpace(assetPath)
                || !assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                || !assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Build Profile paths must be project asset paths ending in .asset.",
                    nameof(assetPath));
            }

            BuildProfile existing = AssetDatabase.LoadAssetAtPath<BuildProfile>(assetPath);
            if (existing != null)
            {
                ValidateTarget(existing, target);
                return existing;
            }

            EnsureAssetFolder(assetPath);
            MethodInfo createMethod = typeof(BuildProfile).GetMethod(
                "CreateInstance",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(BuildTarget), typeof(StandaloneBuildSubtarget) },
                null);
            if (createMethod == null)
            {
                throw new InvalidOperationException(
                    "This Unity version does not expose the Build Profile factory expected by the package.");
            }

            BuildProfile classicProfile = createMethod.Invoke(
                null,
                new object[] { target, StandaloneBuildSubtarget.Player }) as BuildProfile;
            if (classicProfile == null)
            {
                throw new InvalidOperationException("Unity did not create the requested classic Build Profile.");
            }

            try
            {
                Type dataSourceType = Type.GetType(
                    "UnityEditor.Build.Profile.Handlers.BuildProfileDataSource, "
                    + "UnityEditor.BuildProfileModule");
                MethodInfo duplicateMethod = dataSourceType?.GetMethod(
                    "DuplicateAsset",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (duplicateMethod == null)
                {
                    throw new InvalidOperationException(
                        "This Unity version does not expose the custom Build Profile factory expected by the package.");
                }

                BuildProfile profile = duplicateMethod.Invoke(
                    null,
                    new object[] { classicProfile, true }) as BuildProfile;
                if (profile == null)
                {
                    throw new InvalidOperationException("Unity did not create a custom Build Profile asset.");
                }

                string createdPath = AssetDatabase.GetAssetPath(profile);
                if (!string.Equals(createdPath, assetPath, StringComparison.Ordinal))
                {
                    string moveError = AssetDatabase.MoveAsset(createdPath, assetPath);
                    if (!string.IsNullOrWhiteSpace(moveError))
                    {
                        throw new InvalidOperationException(
                            "Could not move the new Build Profile to '" + assetPath + "': " + moveError);
                    }
                }

                profile.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                ValidateTarget(profile, target);
                return profile;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(classicProfile);
            }
        }

        public static BuildTarget GetTarget(BuildProfile profile)
        {
            if (profile == null)
            {
                return BuildTarget.NoTarget;
            }

            SerializedObject serializedProfile = new SerializedObject(profile);
            SerializedProperty targetProperty = serializedProfile.FindProperty("m_BuildTarget");
            return targetProperty == null
                ? BuildTarget.NoTarget
                : (BuildTarget)targetProperty.intValue;
        }

        public static void ApplySceneOverride(
            BuildProfile profile,
            params EditorBuildSettingsScene[] scenes)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            profile.scenes = scenes ?? Array.Empty<EditorBuildSettingsScene>();
            SerializedObject serializedProfile = new SerializedObject(profile);
            SerializedProperty overrideProperty =
                serializedProfile.FindProperty("m_OverrideGlobalSceneList")
                ?? serializedProfile.FindProperty("m_OverrideGlobalScenes");
            if (overrideProperty != null)
            {
                overrideProperty.boolValue = true;
                serializedProfile.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        internal static void EnsurePlayerSettingsOverride(BuildProfile profile)
        {
            MethodInfo method = typeof(BuildProfile).GetMethod(
                "CreatePlayerSettingsFromGlobal",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new InvalidOperationException(
                    "This Unity version does not provide Build Profile Player Settings overrides.");
            }

            method.Invoke(profile, null);
        }

        internal static void PersistPlayerSettingsOverride(BuildProfile profile)
        {
            MethodInfo method = typeof(BuildProfile).GetMethod(
                "SerializePlayerSettings",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new InvalidOperationException(
                    "This Unity version cannot serialize Build Profile Player Settings overrides.");
            }

            method.Invoke(profile, null);
            EditorUtility.SetDirty(profile);
        }

        internal static ActiveBuildProfileScope ActivateTemporarily(BuildProfile profile)
        {
            return new ActiveBuildProfileScope(profile);
        }

        private static void ValidateTarget(BuildProfile profile, BuildTarget target)
        {
            BuildTarget actual = GetTarget(profile);
            if (actual != target)
            {
                throw new InvalidOperationException(
                    "Build Profile '" + AssetDatabase.GetAssetPath(profile) + "' targets "
                    + actual + ", but " + target + " was required.");
            }
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            string directory = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(directory) || directory == "Assets")
            {
                return;
            }

            string current = "Assets";
            string[] parts = directory.Substring("Assets".Length).Trim('/').Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i]))
                {
                    continue;
                }

                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }

    internal sealed class ActiveBuildProfileScope : IDisposable
    {
        private readonly BuildProfile _previous;
        private bool _disposed;

        internal ActiveBuildProfileScope(BuildProfile profile)
        {
            _previous = BuildProfile.GetActiveBuildProfile();
            if (_previous != profile)
            {
                BuildProfile.SetActiveBuildProfile(profile);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (BuildProfile.GetActiveBuildProfile() != _previous)
            {
                BuildProfile.SetActiveBuildProfile(_previous);
            }
        }
    }
}
