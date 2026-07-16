using System;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace Deucarian.BuildPipeline
{
    public static class DeucarianBuildEditorCommands
    {
        [MenuItem(
            "Tools/Deucarian/Build Pipeline/Apply Policy/Development to Selected Profile",
            false,
            100)]
        public static void ApplyDevelopment()
        {
            Apply(DeucarianBuildEnvironment.Development);
        }

        [MenuItem(
            "Tools/Deucarian/Build Pipeline/Apply Policy/Production to Selected Profile",
            false,
            101)]
        public static void ApplyProduction()
        {
            Apply(DeucarianBuildEnvironment.Production);
        }

        [MenuItem(
            "Tools/Deucarian/Build Pipeline/Validate Policy/Development Selected Profile",
            false,
            110)]
        public static void ValidateDevelopment()
        {
            Validate(DeucarianBuildEnvironment.Development);
        }

        [MenuItem(
            "Tools/Deucarian/Build Pipeline/Validate Policy/Production Selected Profile",
            false,
            111)]
        public static void ValidateProduction()
        {
            Validate(DeucarianBuildEnvironment.Production);
        }

        private static void Apply(DeucarianBuildEnvironment environment)
        {
            BuildProfile profile = RequireSelectedProfile();
            DeucarianBuildRunner.ApplyPolicy(profile, environment);
            Debug.Log(
                "Applied the Deucarian " + environment + " policy to '"
                + AssetDatabase.GetAssetPath(profile) + "'. Review and commit the profile asset.");
        }

        private static void Validate(DeucarianBuildEnvironment environment)
        {
            BuildProfile profile = RequireSelectedProfile();
            IDeucarianPlatformBuildPolicy policy = DeucarianBuildRunner.GetPolicy(profile);
            DeucarianBuildValidationResult result = policy.ValidateProfile(profile, environment);
            if (!result.IsValid)
            {
                throw new InvalidOperationException(result.Format("Build Profile validation failed"));
            }

            Debug.Log(result.Format("Build Profile validation"));
        }

        private static BuildProfile RequireSelectedProfile()
        {
            BuildProfile profile = Selection.activeObject as BuildProfile;
            if (profile == null)
            {
                throw new InvalidOperationException(
                    "Select a Unity Build Profile asset before running this command.");
            }

            return profile;
        }
    }
}
