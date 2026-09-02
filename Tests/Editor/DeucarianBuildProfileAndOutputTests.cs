using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace Deucarian.BuildPipeline.Tests
{
    public sealed class DeucarianBuildProfileAndOutputTests
    {
        private const string TestFolder =
            "Assets/__DeucarianBuildProfileAndOutputTests";
        private const string TestProfilePath = TestFolder + "/WebGL.asset";
        private const string OtherProfilePath = TestFolder + "/OtherWebGL.asset";

        private readonly List<string> temporaryOutputs = new List<string>();

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestFolder))
            {
                AssetDatabase.DeleteAsset(TestFolder);
            }

            for (int i = 0; i < temporaryOutputs.Count; i++)
            {
                string path = temporaryOutputs[i];
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            temporaryOutputs.Clear();
        }

        [Test]
        public void PlayerSettingsApplyAndValidateWithoutChangingActiveProfile()
        {
            BuildProfile profile = CreateProfile();
            BuildProfile activeBefore = BuildProfile.GetActiveBuildProfile();
            DeucarianBuildProfilePlayerSettings expected =
                new DeucarianBuildProfilePlayerSettings(
                    "local-test-product",
                    true,
                    InsecureHttpOption.DevelopmentOnly);

            DeucarianBuildProfileUtility.ApplyPlayerSettings(profile, expected);
            DeucarianBuildValidationResult validation =
                DeucarianBuildProfileUtility.ValidatePlayerSettings(
                    profile,
                    expected);

            Assert.That(validation.IsValid, Is.True, validation.Format("settings"));
            Assert.That(BuildProfile.GetActiveBuildProfile(), Is.SameAs(activeBefore));
            string serialized = File.ReadAllText(TestProfilePath);
            StringAssert.Contains("bundleVersion: local-test-product", serialized);
            StringAssert.Contains("runInBackground: 1", serialized);
            StringAssert.Contains(
                "insecureHttpOption: "
                + (int)InsecureHttpOption.DevelopmentOnly,
                serialized);
        }

        [Test]
        public void PassivePlayerSettingsValidationReportsEveryDrift()
        {
            BuildProfile profile = CreateProfile();
            DeucarianBuildProfileUtility.ApplyPlayerSettings(
                profile,
                new DeucarianBuildProfilePlayerSettings(
                    "1.0",
                    true,
                    InsecureHttpOption.NotAllowed));
            BuildProfile activeBefore = BuildProfile.GetActiveBuildProfile();

            DeucarianBuildValidationResult validation =
                DeucarianBuildProfileUtility.ValidatePlayerSettings(
                    profile,
                    new DeucarianBuildProfilePlayerSettings(
                        "2.0",
                        false,
                        InsecureHttpOption.DevelopmentOnly));

            Assert.That(validation.Issues.Count, Is.EqualTo(3));
            Assert.That(BuildProfile.GetActiveBuildProfile(), Is.SameAs(activeBefore));
        }

        [Test]
        public void ScriptsOnlyRequiresManifestAndPreservesOwnedOutput()
        {
            BuildProfile profile = CreateProfile();
            string path = CreateOutput("scripts-only");
            File.WriteAllText(Path.Combine(path, "existing.txt"), "keep");
            DeucarianBuildRequest request = new DeucarianBuildRequest(
                profile,
                DeucarianBuildEnvironment.Development,
                path,
                BuildOptions.BuildScriptsOnly);

            DeucarianBuildValidationResult missing =
                DeucarianBuildOutputUtility.ValidatePreparation(request);
            Assert.That(missing.IsValid, Is.False);

            WriteCompatibleManifest(path, request);
            DeucarianBuildValidationResult valid =
                DeucarianBuildOutputUtility.ValidatePreparation(request);
            Assert.That(valid.IsValid, Is.True, valid.Format("output"));

            DeucarianBuildOutputUtility.Prepare(request);
            Assert.That(File.Exists(Path.Combine(path, "existing.txt")), Is.True);
        }

        [Test]
        public void ScriptsOnlyRequiresRequestAwareCompatibility()
        {
            BuildProfile profile = CreateProfile();
            string path = CreateOutput("scripts-only-context");
            DeucarianBuildRequest development = new DeucarianBuildRequest(
                profile,
                DeucarianBuildEnvironment.Development,
                path,
                BuildOptions.BuildScriptsOnly);
            WriteCompatibleManifest(path, development);

            Assert.That(
                DeucarianBuildOutputUtility.ValidatePreparation(
                    path,
                    BuildOptions.BuildScriptsOnly).IsValid,
                Is.False,
                "The path-only overload cannot prove scripts-only compatibility.");

            DeucarianBuildRequest wrongEnvironment =
                new DeucarianBuildRequest(
                    profile,
                    DeucarianBuildEnvironment.Production,
                    path,
                    BuildOptions.BuildScriptsOnly);
            DeucarianBuildValidationResult environmentValidation =
                DeucarianBuildOutputUtility.ValidatePreparation(
                    wrongEnvironment);
            Assert.That(environmentValidation.IsValid, Is.False);
            Assert.That(
                environmentValidation.Issues,
                Has.Some.Contains("different build environment"));

            BuildProfile otherProfile =
                DeucarianBuildProfileUtility.CreateProfile(
                    BuildTarget.WebGL,
                    OtherProfilePath);
            new DeucarianWebGLBuildPolicy().ApplySettings(
                otherProfile,
                DeucarianBuildEnvironment.Development);
            DeucarianBuildValidationResult profileValidation =
                DeucarianBuildOutputUtility.ValidatePreparation(
                    new DeucarianBuildRequest(
                        otherProfile,
                        DeucarianBuildEnvironment.Development,
                        path,
                        BuildOptions.BuildScriptsOnly));
            Assert.That(profileValidation.IsValid, Is.False);
            Assert.That(
                profileValidation.Issues,
                Has.Some.Contains("different Build Profile"));
        }

        [Test]
        public void ScriptsOnlyRejectsInvalidManifest()
        {
            BuildProfile profile = CreateProfile();
            string path = CreateOutput("invalid-manifest");
            File.WriteAllText(
                Path.Combine(path, DeucarianBuildArtifactManifest.FileName),
                "{\"schemaVersion\":1}");

            DeucarianBuildValidationResult validation =
                DeucarianBuildOutputUtility.ValidatePreparation(
                    new DeucarianBuildRequest(
                        profile,
                        DeucarianBuildEnvironment.Development,
                        path,
                        BuildOptions.BuildScriptsOnly));

            Assert.That(validation.IsValid, Is.False);
        }

        [Test]
        public void NormalPreparationReplacesProjectBuildOutput()
        {
            string relative = "Builds/__DeucarianOutputTests-"
                              + Guid.NewGuid().ToString("N");
            string path = Path.Combine(ProjectRoot, relative);
            temporaryOutputs.Add(path);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "unowned.txt"), "replace");

            DeucarianBuildOutputUtility.Prepare(relative, BuildOptions.None);

            Assert.That(Directory.Exists(path), Is.False);
        }

        [Test]
        public void NormalPreparationPreservesEmptyAndReplacesManifestOwnedOutput()
        {
            string empty = CreateOutput("empty");
            Assert.That(
                DeucarianBuildOutputUtility.ValidatePreparation(
                    empty,
                    BuildOptions.None).IsValid,
                Is.True);
            DeucarianBuildOutputUtility.Prepare(empty, BuildOptions.None);
            Assert.That(Directory.Exists(empty), Is.True,
                "An empty non-Builds directory needs no destructive preparation.");

            string owned = CreateOutput("owned");
            File.WriteAllText(Path.Combine(owned, "artifact.txt"), "owned");
            WriteValidManifest(owned);
            DeucarianBuildOutputUtility.Prepare(owned, BuildOptions.None);
            Assert.That(Directory.Exists(owned), Is.False);
        }

        [Test]
        public void NonexistentInsideProjectOutputIsSafeWithoutDeletion()
        {
            string path = Path.Combine(
                ProjectRoot,
                "Temp",
                "__DeucarianOutputTests-new-" + Guid.NewGuid().ToString("N"));
            temporaryOutputs.Add(path);

            DeucarianBuildValidationResult validation =
                DeucarianBuildOutputUtility.ValidatePreparation(
                    path,
                    BuildOptions.None);

            Assert.That(validation.IsValid, Is.True, validation.Format("output"));
            Assert.DoesNotThrow(() =>
                DeucarianBuildOutputUtility.Prepare(path, BuildOptions.None));
            Assert.That(Directory.Exists(path), Is.False);
        }

        [Test]
        public void OutsideProjectOutputFailsWhenMissingEmptyOrManifestOwned()
        {
            string external = Path.Combine(
                Path.GetTempPath(),
                "__DeucarianOutputTests-external-"
                + Guid.NewGuid().ToString("N"));
            temporaryOutputs.Add(external);

            Assert.That(
                DeucarianBuildOutputUtility.ValidatePreparation(
                    external,
                    BuildOptions.None).IsValid,
                Is.False,
                "A nonexistent outside-project path must fail.");

            Directory.CreateDirectory(external);
            Assert.That(
                DeucarianBuildOutputUtility.ValidatePreparation(
                    external,
                    BuildOptions.None).IsValid,
                Is.False,
                "An empty outside-project path must fail.");

            File.WriteAllText(Path.Combine(external, "artifact.txt"), "owned");
            WriteValidManifest(external);
            Assert.That(
                DeucarianBuildOutputUtility.ValidatePreparation(
                    external,
                    BuildOptions.None).IsValid,
                Is.False,
                "A manifest must not bypass the project boundary.");
            Assert.Throws<BuildFailedException>(() =>
                DeucarianBuildOutputUtility.Prepare(
                    external,
                    BuildOptions.None));
            Assert.That(Directory.Exists(external), Is.True);
        }

        [Test]
        public void TraversalSiblingPrefixAndProjectRootFailClosed()
        {
            string traversalName = "__DeucarianOutputTests-traversal-"
                                   + Guid.NewGuid().ToString("N");
            string traversal = "../" + traversalName;
            temporaryOutputs.Add(Path.GetFullPath(
                Path.Combine(ProjectRoot, traversal)));
            Assert.That(
                DeucarianBuildOutputUtility.ValidatePreparation(
                    traversal,
                    BuildOptions.None).IsValid,
                Is.False);

            string sibling = ProjectRoot + "-Sibling-"
                             + Guid.NewGuid().ToString("N");
            temporaryOutputs.Add(sibling);
            Directory.CreateDirectory(sibling);
            Assert.That(
                DeucarianBuildOutputUtility.ValidatePreparation(
                    sibling,
                    BuildOptions.None).IsValid,
                Is.False,
                "A textual project-root prefix is not project containment.");

            Assert.That(
                DeucarianBuildOutputUtility.ValidatePreparation(
                    ProjectRoot,
                    BuildOptions.None).IsValid,
                Is.False);

            string[] rootAliases =
            {
                "." + Path.DirectorySeparatorChar,
                "." + Path.AltDirectorySeparatorChar,
                ProjectRoot + Path.DirectorySeparatorChar,
                Path.Combine("Builds", "..")
                    + Path.DirectorySeparatorChar
            };
            for (int i = 0; i < rootAliases.Length; i++)
            {
                DeucarianBuildValidationResult rootAliasValidation =
                    DeucarianBuildOutputUtility.ValidatePreparation(
                        rootAliases[i],
                        BuildOptions.None);
                Assert.That(
                    rootAliasValidation.IsValid,
                    Is.False,
                    "Project-root alias must fail: " + rootAliases[i]);
                Assert.That(
                    rootAliasValidation.Issues,
                    Has.Some.Contains("project root"),
                    "Alias must be rejected by the root boundary itself: "
                    + rootAliases[i]);
            }
        }

        [Test]
        public void FileTargetOrFileParentFailsClosed()
        {
            string parent = CreateOutput("file-target");
            string file = Path.Combine(parent, "output.file");
            File.WriteAllText(file, "keep");

            Assert.That(
                DeucarianBuildOutputUtility.ValidatePreparation(
                    file,
                    BuildOptions.None).IsValid,
                Is.False);
            Assert.That(
                DeucarianBuildOutputUtility.ValidatePreparation(
                    Path.Combine(file, "child"),
                    BuildOptions.None).IsValid,
                Is.False);
        }

        private static string ProjectRoot => Path.GetFullPath(
            Path.GetDirectoryName(Application.dataPath) ?? string.Empty);

        private BuildProfile CreateProfile()
        {
            BuildProfile profile = DeucarianBuildProfileUtility.CreateProfile(
                BuildTarget.WebGL,
                TestProfilePath);
            new DeucarianWebGLBuildPolicy().ApplySettings(
                profile,
                DeucarianBuildEnvironment.Development);
            return profile;
        }

        private string CreateOutput(string label)
        {
            string path = Path.Combine(
                ProjectRoot,
                "Temp",
                "__DeucarianOutputTests-" + label + "-"
                + Guid.NewGuid().ToString("N"));
            temporaryOutputs.Add(path);
            Directory.CreateDirectory(path);
            return path;
        }

        private static void WriteValidManifest(string path)
        {
            DeucarianBuildArtifactManifest manifest =
                new DeucarianBuildArtifactManifest
                {
                    packageVersion = "0.6.0",
                    buildGuid = Guid.NewGuid().ToString("N")
                };
            File.WriteAllText(
                Path.Combine(path, DeucarianBuildArtifactManifest.FileName),
                manifest.ToJson());
        }

        private static void WriteCompatibleManifest(
            string path,
            DeucarianBuildRequest request)
        {
            string profilePath = AssetDatabase.GetAssetPath(
                request.BuildProfile);
            DeucarianBuildArtifactManifest manifest =
                new DeucarianBuildArtifactManifest
                {
                    packageVersion = DeucarianBuildPackage.Version,
                    unityVersion = Application.unityVersion,
                    environment = request.Environment.ToString(),
                    buildProfileGuid =
                        AssetDatabase.AssetPathToGUID(profilePath),
                    buildGuid = Guid.NewGuid().ToString("N"),
                    settingsFingerprint =
                        new DeucarianWebGLBuildPolicy()
                            .GetSettingsFingerprint(request.Environment),
                    compatibilityFingerprint =
                        DeucarianBuildCompatibility.CreateFingerprint(
                            request,
                            DeucarianBuildCompatibility.GetEffectiveOptions(request))
                };
            File.WriteAllText(
                Path.Combine(
                    path,
                    DeucarianBuildArtifactManifest.FileName),
                manifest.ToJson());
        }
    }
}
