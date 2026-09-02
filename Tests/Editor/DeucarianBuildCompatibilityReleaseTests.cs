using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Deucarian.BuildPipeline.Tests
{
    public sealed class DeucarianBuildCompatibilityReleaseTests
    {
        private const string TestFolder =
            "Assets/__DeucarianBuildCompatibilityReleaseTests";
        private const string ProfilePath = TestFolder + "/WebGL.asset";
        private const string ScenePath = TestFolder + "/Compatibility.unity";
        private const string DataPath =
            TestFolder + "/Nested/Resources/CompatibilityData.txt";
        private const string LifecycleDataPath =
            TestFolder + "/Nested/StreamingAssets/LifecycleContext.json";
        private const string ScriptLikeStreamingDataPath =
            TestFolder + "/Nested/StreamingAssets/RuntimeCatalog.dll";
        private const string CleanScenePath =
            "Packages/com.deucarian.build-pipeline/Tests/Editor/Fixtures/"
            + "CleanScene.unity";

        private readonly List<string> temporaryOutputs = new List<string>();

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.OpenScene(
                CleanScenePath,
                OpenSceneMode.Single);
            if (AssetDatabase.IsValidFolder(TestFolder))
            {
                AssetDatabase.DeleteAsset(TestFolder);
            }

            for (int index = 0; index < temporaryOutputs.Count; index++)
            {
                if (Directory.Exists(temporaryOutputs[index]))
                {
                    Directory.Delete(temporaryOutputs[index], true);
                }
            }

            temporaryOutputs.Clear();
        }

        [Test]
        public void CompatibilityManifestIsSchemaVersioned()
        {
            CompatibilityFixture fixture = CreateFixture();
            DeucarianBuildArtifactManifest manifest = WriteCompatibleManifest(
                fixture.Output,
                fixture.NormalRequest);

            Assert.That(
                DeucarianBuildArtifactManifest.CurrentSchemaVersion,
                Is.EqualTo(3));
            Assert.That(manifest.schemaVersion, Is.EqualTo(3));
            Assert.That(manifest.compatibilityFingerprint, Has.Length.EqualTo(64));
            Assert.That(
                DeucarianBuildOutputUtility.ValidatePreparation(fixture.Request).IsValid,
                Is.True);
        }

        [Test]
        public void ScriptsOnlyRejectsBuildOptionDrift()
        {
            CompatibilityFixture fixture = CreateFixture();
            WriteCompatibleManifest(fixture.Output, fixture.NormalRequest);
            DeucarianBuildRequest changed = new DeucarianBuildRequest(
                fixture.Profile,
                DeucarianBuildEnvironment.Development,
                fixture.Output,
                BuildOptions.BuildScriptsOnly | BuildOptions.AllowDebugging);

            AssertCompatibilityRejected(changed);
        }

        [Test]
        public void ScriptsOnlyRejectsBuildProfileDrift()
        {
            CompatibilityFixture fixture = CreateFixture();
            WriteCompatibleManifest(fixture.Output, fixture.NormalRequest);
            BuildProfile profile = fixture.Profile;
            profile.name = "Changed compatibility profile";
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            AssertCompatibilityRejected(fixture.Request);
        }

        [Test]
        public void ScriptsOnlyRejectsSceneDrift()
        {
            CompatibilityFixture fixture = CreateFixture();
            Assert.That(fixture.Profile.scenes, Has.Length.EqualTo(1));
            Assert.That(fixture.Profile.scenes[0].path, Is.EqualTo(ScenePath));
            string originalSceneContents = File.ReadAllText(
                ToProjectPath(ScenePath));
            WriteCompatibleManifest(fixture.Output, fixture.NormalRequest);
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            new GameObject("Changed scene data");
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            Assert.That(
                File.ReadAllText(ToProjectPath(ScenePath)),
                Is.Not.EqualTo(originalSceneContents),
                "The fixture must persist scene drift before compatibility is checked.");

            AssertCompatibilityRejected(fixture.Request);
        }

        [Test]
        public void ScriptsOnlyRejectsResourceDataDrift()
        {
            CompatibilityFixture fixture = CreateFixture();
            WriteCompatibleManifest(fixture.Output, fixture.NormalRequest);
            File.WriteAllText(ToProjectPath(DataPath), "changed data");
            AssetDatabase.ImportAsset(DataPath, ImportAssetOptions.ForceUpdate);

            AssertCompatibilityRejected(fixture.Request);
        }

        [Test]
        public void LifecycleAwarePreparationAcceptsRecreatedStreamingAsset()
        {
            CompatibilityFixture fixture = CreateFixture();
            CreateLifecycleData("same context");
            WriteCompatibleManifest(fixture.Output, fixture.NormalRequest);
            DeleteLifecycleData();

            CreateLifecycleData("same context");
            bool buildRan = false;
            int result = DeucarianBuildRunner.ExecuteBuildAttempt(
                fixture.Output,
                ScopeThatDeletesLifecycleData(),
                () => DeucarianBuildOutputUtility.Prepare(fixture.Request),
                () =>
                {
                    buildRan = true;
                    return 42;
                },
                _ => { });

            Assert.That(result, Is.EqualTo(42));
            Assert.That(buildRan, Is.True);
            Assert.That(File.Exists(ToProjectPath(LifecycleDataPath)), Is.False);
        }

        [Test]
        public void LifecycleAwarePreparationRejectsChangedStreamingAsset()
        {
            CompatibilityFixture fixture = CreateFixture();
            CreateLifecycleData("original context");
            WriteCompatibleManifest(fixture.Output, fixture.NormalRequest);
            DeleteLifecycleData();

            CreateLifecycleData("changed context");
            bool buildRan = false;
            BuildFailedException failure = Assert.Throws<BuildFailedException>(
                () => DeucarianBuildRunner.ExecuteBuildAttempt(
                    fixture.Output,
                    ScopeThatDeletesLifecycleData(),
                    () => DeucarianBuildOutputUtility.Prepare(fixture.Request),
                    () =>
                    {
                        buildRan = true;
                        return 42;
                    },
                    _ => { }));

            Assert.That(buildRan, Is.False);
            Assert.That(
                failure.Message,
                Does.Contain("profile, scene, data, or build-option inputs"));
            Assert.That(File.Exists(ToProjectPath(LifecycleDataPath)), Is.False);
        }

        [Test]
        public void ScriptsOnlyRejectsScriptLikeStreamingAssetDrift()
        {
            CompatibilityFixture fixture = CreateFixture();
            CreateAssetData(ScriptLikeStreamingDataPath, "original raw data");
            WriteCompatibleManifest(fixture.Output, fixture.NormalRequest);

            CreateAssetData(ScriptLikeStreamingDataPath, "changed raw data");

            AssertCompatibilityRejected(fixture.Request);
        }

        [Test]
        public void WorkingRequestReloadsProfileWithoutMutatingCallerRequest()
        {
            CompatibilityFixture fixture = CreateFixture();
            DeucarianBuildRequest callerRequest = fixture.NormalRequest;
            BuildProfile callerProfile = callerRequest.BuildProfile;
            DeucarianBuildEnvironment callerEnvironment =
                callerRequest.Environment;
            string callerOutput = callerRequest.OutputPath;
            BuildOptions callerOptions = callerRequest.AdditionalBuildOptions;
            DeucarianBuildRequest workingRequest =
                DeucarianBuildRunner.CreateWorkingRequest(callerRequest);

            CreateAssetData(
                ScriptLikeStreamingDataPath,
                "lifecycle preparation refresh");
            DeucarianBuildRunner.ReloadBuildProfileReference(
                workingRequest,
                ProfilePath);

            Assert.That(workingRequest, Is.Not.SameAs(callerRequest));
            Assert.That(workingRequest.BuildProfile, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(workingRequest.BuildProfile),
                Is.EqualTo(ProfilePath));
            Assert.That(
                ReferenceEquals(callerRequest.BuildProfile, callerProfile),
                Is.True);
            Assert.That(workingRequest.Environment, Is.EqualTo(callerEnvironment));
            Assert.That(workingRequest.OutputPath, Is.EqualTo(callerOutput));
            Assert.That(workingRequest.AdditionalBuildOptions, Is.EqualTo(callerOptions));
            Assert.That(callerRequest.Environment, Is.EqualTo(callerEnvironment));
            Assert.That(callerRequest.OutputPath, Is.EqualTo(callerOutput));
            Assert.That(callerRequest.AdditionalBuildOptions, Is.EqualTo(callerOptions));
        }

        private CompatibilityFixture CreateFixture()
        {
            if (!AssetDatabase.IsValidFolder(TestFolder))
            {
                AssetDatabase.CreateFolder(
                    "Assets",
                    "__DeucarianBuildCompatibilityReleaseTests");
            }

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            new GameObject("Baseline scene data");
            Assert.That(EditorSceneManager.SaveScene(scene, ScenePath), Is.True);
            AssetDatabase.ImportAsset(
                ScenePath,
                ImportAssetOptions.ForceUpdate);
            CreateAssetData(DataPath, "baseline data");

            BuildProfile profile = DeucarianBuildProfileUtility.CreateProfile(
                BuildTarget.WebGL,
                ProfilePath);
            new DeucarianWebGLBuildPolicy().ApplySettings(
                profile,
                DeucarianBuildEnvironment.Development);
            profile = LoadProfile();
            DeucarianBuildProfileUtility.ApplySceneOverride(
                profile,
                new EditorBuildSettingsScene(ScenePath, true));
            LoadProfile();

            string output = Path.Combine(
                ProjectRoot,
                "Temp",
                "__DeucarianBuildCompatibilityReleaseTests-"
                + Guid.NewGuid().ToString("N"));
            temporaryOutputs.Add(output);
            Directory.CreateDirectory(output);
            return new CompatibilityFixture(output);
        }

        private static BuildProfile LoadProfile()
        {
            BuildProfile profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(
                ProfilePath);
            Assert.That(profile, Is.Not.Null);
            return profile;
        }

        private static DeucarianBuildArtifactManifest WriteCompatibleManifest(
            string output,
            DeucarianBuildRequest request)
        {
            string profilePath = AssetDatabase.GetAssetPath(request.BuildProfile);
            DeucarianBuildArtifactManifest manifest =
                new DeucarianBuildArtifactManifest
                {
                    packageVersion = DeucarianBuildPackage.Version,
                    unityVersion = Application.unityVersion,
                    environment = request.Environment.ToString(),
                    buildProfileGuid = AssetDatabase.AssetPathToGUID(profilePath),
                    buildGuid = Guid.NewGuid().ToString("N"),
                    settingsFingerprint = new DeucarianWebGLBuildPolicy()
                        .GetSettingsFingerprint(request.Environment),
                    compatibilityFingerprint =
                        DeucarianBuildCompatibility.CreateFingerprint(
                            request,
                            DeucarianBuildCompatibility.GetEffectiveOptions(request))
                };
            manifest.WriteTo(output);
            return manifest;
        }

        private static void CreateLifecycleData(string contents)
        {
            CreateAssetData(LifecycleDataPath, contents);
        }

        private static void CreateAssetData(string assetPath, string contents)
        {
            string fullPath = ToProjectPath(assetPath);
            Directory.CreateDirectory(
                Path.GetDirectoryName(fullPath) ?? string.Empty);
            File.WriteAllText(fullPath, contents);
            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceUpdate);
        }

        private static void DeleteLifecycleData()
        {
            AssetDatabase.DeleteAsset(LifecycleDataPath);
        }

        private static DeucarianBuildLifecycleScopeSet
            ScopeThatDeletesLifecycleData()
        {
            return new DeucarianBuildLifecycleScopeSet(
                new List<DeucarianBuildLifecycleScopeSet.PreparedScope>
                {
                    new DeucarianBuildLifecycleScopeSet.PreparedScope
                    {
                        Id = "tests.lifecycle-streaming-asset",
                        Scope = new ActionScope(DeleteLifecycleData)
                    }
                });
        }

        private static void AssertCompatibilityRejected(
            DeucarianBuildRequest request)
        {
            DeucarianBuildValidationResult validation =
                DeucarianBuildOutputUtility.ValidatePreparation(request);
            Assert.That(validation.IsValid, Is.False);
            Assert.That(
                validation.Issues,
                Has.Some.Contains("profile, scene, data, or build-option inputs"));
        }

        private static string ToProjectPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(
                ProjectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string ProjectRoot => Path.GetFullPath(
            Path.GetDirectoryName(Application.dataPath) ?? string.Empty);

        private sealed class CompatibilityFixture
        {
            internal CompatibilityFixture(string output)
            {
                Output = output;
            }

            internal BuildProfile Profile => LoadProfile();

            internal DeucarianBuildRequest NormalRequest =>
                new DeucarianBuildRequest(
                    Profile,
                    DeucarianBuildEnvironment.Development,
                    Output,
                    BuildOptions.None);

            internal DeucarianBuildRequest Request =>
                new DeucarianBuildRequest(
                    Profile,
                    DeucarianBuildEnvironment.Development,
                    Output,
                    BuildOptions.BuildScriptsOnly);

            internal string Output { get; }
        }

        private sealed class ActionScope : IDisposable
        {
            private readonly Action action;
            private bool disposed;

            internal ActionScope(Action action)
            {
                this.action = action;
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                action();
            }
        }
    }
}
