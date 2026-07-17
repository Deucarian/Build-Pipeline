using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace Deucarian.BuildPipeline.Tests
{
    public sealed class DeucarianBuildPipelineTests
    {
        private const string TestFolder = "Assets/__DeucarianBuildPipelineTests";
        private const string TestProfilePath = TestFolder + "/WebGL.asset";
        private const string SettingsFolder = "Assets/Settings";
        private const string BuildProfilesFolder = SettingsFolder + "/Build Profiles";

        private bool settingsFolderExisted;
        private bool buildProfilesFolderExisted;

        [SetUp]
        public void SetUp()
        {
            settingsFolderExisted = AssetDatabase.IsValidFolder(SettingsFolder);
            buildProfilesFolderExisted = AssetDatabase.IsValidFolder(BuildProfilesFolder);
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestFolder))
            {
                AssetDatabase.DeleteAsset(TestFolder);
            }

            DeleteFolderIfCreatedAndEmpty(
                BuildProfilesFolder,
                buildProfilesFolderExisted);
            DeleteFolderIfCreatedAndEmpty(SettingsFolder, settingsFolderExisted);
        }

        [Test]
        public void EnvironmentMappingsAreDeterministicAndDistinct()
        {
            DeucarianWebGLBuildPolicy policy = new DeucarianWebGLBuildPolicy();

            CollectionAssert.Contains(
                policy.GetExpectedSettings(DeucarianBuildEnvironment.Development),
                "compression=Disabled");
            CollectionAssert.Contains(
                policy.GetExpectedSettings(DeucarianBuildEnvironment.Production),
                "compression=Brotli");
            Assert.That(
                policy.GetSettingsFingerprint(DeucarianBuildEnvironment.Development),
                Is.Not.EqualTo(policy.GetSettingsFingerprint(DeucarianBuildEnvironment.Production)));
        }

        [Test]
        public void PublicEnvironmentEntryPointsRejectUndefinedValues()
        {
            DeucarianBuildEnvironment invalid = (DeucarianBuildEnvironment)42;
            DeucarianWebGLBuildPolicy policy = new DeucarianWebGLBuildPolicy();
            DeucarianBuildRequest request = new DeucarianBuildRequest
            {
                Environment = invalid
            };
            DeucarianBuildArtifactManifest manifest =
                new DeucarianBuildArtifactManifest();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DeucarianBuildRunner.Build(request));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DeucarianBuildRunner.ApplyPolicy(null, invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                policy.ApplySettings(null, invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                policy.ValidateProfile(null, invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                policy.ValidateGeneratedArtifacts(request, manifest));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                policy.GetSettingsFingerprint(invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new DeucarianBuildManagerTarget(
                    "invalid-environment",
                    "Invalid environment",
                    string.Empty,
                    "Assets/Settings/Build Profiles/WebGL.asset",
                    invalid,
                    "Builds/WebGL",
                    () => null));
        }

        [TestCase("0")]
        [TestCase("1")]
        [TestCase("2")]
        [TestCase("-1")]
        [TestCase("42")]
        [TestCase("unknown")]
        public void CommandLineEnvironmentParserRejectsNumericAndUnknownValues(string value)
        {
            Assert.Throws<ArgumentException>(() =>
                DeucarianBuildCommandLine.ParseEnvironment(value));
        }

        [TestCase("development", DeucarianBuildEnvironment.Development)]
        [TestCase("PRODUCTION", DeucarianBuildEnvironment.Production)]
        public void CommandLineEnvironmentParserAcceptsOnlyNamedEnvironments(
            string value,
            DeucarianBuildEnvironment expected)
        {
            Assert.That(
                DeucarianBuildCommandLine.ParseEnvironment(value),
                Is.EqualTo(expected));
        }

        [TestCase("None", BuildOptions.None)]
        [TestCase("development", BuildOptions.Development)]
        [TestCase("StrictMode", BuildOptions.StrictMode)]
        [TestCase("detailedbuildreport", BuildOptions.DetailedBuildReport)]
        public void CommandLineBuildOptionsParserAcceptsDeclaredNames(
            string value,
            BuildOptions expected)
        {
            Assert.That(
                DeucarianBuildCommandLine.ParseBuildOptions(value),
                Is.EqualTo(expected));
        }

        [Test]
        public void CommandLineBuildOptionsParserCombinesCommaSeparatedFlags()
        {
            BuildOptions expected =
                BuildOptions.Development | BuildOptions.DetailedBuildReport;
            Assert.That(
                DeucarianBuildCommandLine.ParseBuildOptions(
                    "Development, DetailedBuildReport"),
                Is.EqualTo(expected));
        }

        [TestCase("1")]
        [TestCase("-1")]
        [TestCase("987654321")]
        [TestCase("NotARealOption")]
        [TestCase("Development,1")]
        public void CommandLineBuildOptionsParserRejectsNumericAndUnknownTokens(string value)
        {
            Assert.Throws<ArgumentException>(() =>
                DeucarianBuildCommandLine.ParseBuildOptions(value));
        }

        [Test]
        public void DirectBuildApiRejectsUndeclaredBuildOptionBits()
        {
            BuildOptions unknownOption = FindUndeclaredBuildOptionBit();
            DeucarianBuildRequest request = new DeucarianBuildRequest
            {
                Environment = DeucarianBuildEnvironment.Development,
                AdditionalBuildOptions = unknownOption
            };

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DeucarianBuildRunner.Build(request));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DeucarianBuildRunner.ValidateBuildOptions(
                    DeucarianBuildEnvironment.Development,
                    BuildOptions.Development | unknownOption));
            Assert.DoesNotThrow(() =>
                DeucarianBuildRunner.ValidateBuildOptions(
                    DeucarianBuildEnvironment.Development,
                    BuildOptions.Development | BuildOptions.DetailedBuildReport));
        }

        [Test]
        public void SynchronizationAppliesPolicyAndOppositeEnvironmentDetectsDrift()
        {
            BuildProfile profile = DeucarianBuildProfileUtility.CreateProfile(
                BuildTarget.WebGL,
                TestProfilePath);
            DeucarianWebGLBuildPolicy policy = new DeucarianWebGLBuildPolicy();

            policy.ApplySettings(profile, DeucarianBuildEnvironment.Development);

            Assert.That(
                policy.ValidateProfile(profile, DeucarianBuildEnvironment.Development).IsValid,
                Is.True);
            Assert.That(
                policy.ValidateProfile(profile, DeucarianBuildEnvironment.Production).IsValid,
                Is.False);

            string serializedProfile = File.ReadAllText(TestProfilePath);
            StringAssert.Contains("webGLCompressionFormat: 2", serializedProfile);
            StringAssert.Contains("webWasm2023: 1", serializedProfile);
        }

        [Test]
        public void RequestValidationRejectsMissingRequestAndOutput()
        {
            Assert.Throws<ArgumentNullException>(() => DeucarianBuildRunner.Build(null));

            BuildProfile profile = DeucarianBuildProfileUtility.CreateProfile(
                BuildTarget.WebGL,
                TestProfilePath);
            Assert.Throws<ArgumentException>(() =>
                DeucarianBuildRunner.Build(
                    new DeucarianBuildRequest(
                        profile,
                        DeucarianBuildEnvironment.Development,
                        string.Empty)));
        }

        [Test]
        public void ArtifactClassifierReportsEncodedAndRawBrotliSizes()
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(directory, "0123456789abcdef.data.br");
            Directory.CreateDirectory(directory);
            try
            {
                byte[] raw = new byte[128 * 1024];
                for (int i = 0; i < raw.Length; i++)
                {
                    raw[i] = (byte)(i % 17);
                }

                using (FileStream output = File.Create(filePath))
                using (BrotliStream encoder = new BrotliStream(
                           output,
                           System.IO.Compression.CompressionLevel.Optimal))
                {
                    encoder.Write(raw, 0, raw.Length);
                }

                DeucarianBuildArtifact artifact =
                    DeucarianBuildArtifactClassifier.Classify(
                        filePath,
                        "Build/0123456789abcdef.data.br");

                Assert.That(artifact.classification, Is.EqualTo("data"));
                Assert.That(artifact.encoding, Is.EqualTo("br"));
                Assert.That(artifact.rawBytes, Is.EqualTo(raw.Length));
                Assert.That(artifact.encodedBytes, Is.LessThan(raw.Length));
                Assert.That(artifact.preEngineBootstrap, Is.True);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [Test]
        public void ProductionGateAcceptsHashedBrotliPayloadWithinBudget()
        {
            DeucarianBuildArtifactManifest manifest = CreatePassingProductionManifest();
            DeucarianBuildRequest request = new DeucarianBuildRequest
            {
                Environment = DeucarianBuildEnvironment.Production
            };

            DeucarianBuildValidationResult result =
                new DeucarianWebGLBuildPolicy().ValidateGeneratedArtifacts(request, manifest);

            Assert.That(result.IsValid, Is.True, result.Format("Artifact validation"));
        }

        [Test]
        public void ProductionGateRejectsBudgetDebugRawAndDevelopmentContextViolations()
        {
            DeucarianBuildArtifactManifest manifest = CreatePassingProductionManifest();
            manifest.budget.passed = false;
            manifest.budget.encodedBootstrapBytes = manifest.budget.limitBytes + 1;
            manifest.artifacts.Add(new DeucarianBuildArtifact
            {
                relativePath = "Build/player.symbols.json",
                classification = "debug-symbols",
                encoding = "identity"
            });
            manifest.artifacts.Add(new DeucarianBuildArtifact
            {
                relativePath = "Build/player.wasm",
                classification = "wasm",
                encoding = "identity"
            });
            manifest.artifacts.Add(new DeucarianBuildArtifact
            {
                relativePath = "StreamingAssets/development-context.json",
                classification = "support",
                encoding = "identity"
            });

            DeucarianBuildValidationResult result =
                new DeucarianWebGLBuildPolicy().ValidateGeneratedArtifacts(
                    new DeucarianBuildRequest
                    {
                        Environment = DeucarianBuildEnvironment.Production
                    },
                    manifest);

            Assert.That(result.IsValid, Is.False);
            StringAssert.Contains("above", result.Format("validation"));
            StringAssert.Contains("debug symbols", result.Format("validation"));
            StringAssert.Contains("not Brotli", result.Format("validation"));
            StringAssert.Contains("development context", result.Format("validation"));
        }

        [Test]
        public void ProductionGateDoesNotLetDuplicatePayloadClassesReplaceRequiredClasses()
        {
            DeucarianBuildArtifactManifest manifest = new DeucarianBuildArtifactManifest();
            manifest.budget.limitBytes = DeucarianWebGLBuildPolicy.ProductionBootstrapBudgetBytes;
            manifest.budget.encodedBootstrapBytes = 3;
            manifest.budget.passed = true;
            manifest.artifacts.Add(CreatePayload("0123456789abcdef.data.br", "data"));
            manifest.artifacts.Add(CreatePayload("1234567890abcdef.data.br", "data"));
            manifest.artifacts.Add(CreatePayload("2345678901abcdef.data.br", "data"));

            DeucarianBuildValidationResult result =
                new DeucarianWebGLBuildPolicy().ValidateGeneratedArtifacts(
                    new DeucarianBuildRequest
                    {
                        Environment = DeucarianBuildEnvironment.Production
                    },
                    manifest);

            Assert.That(result.IsValid, Is.False);
            string formatted = result.Format("validation");
            StringAssert.DoesNotContain("missing a Brotli data payload", formatted);
            StringAssert.Contains("missing a Brotli framework payload", formatted);
            StringAssert.Contains("missing a Brotli WebAssembly payload", formatted);
        }

        [Test]
        public void OutputPathResolutionRequiresASafeProjectRelativeDirectory()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            Assert.That(projectRoot, Is.Not.Null.And.Not.Empty);

            string valid = DeucarianBuildPathUtility.ToFullOutputPath(
                "Builds/WebGL/Production");
            Assert.That(
                valid,
                Is.EqualTo(Path.GetFullPath(
                    Path.Combine(projectRoot, "Builds/WebGL/Production"))));
            Assert.DoesNotThrow(() =>
                DeucarianBuildPathUtility.ToFullOutputPath("AssetsBackup/WebGL"));
            Assert.DoesNotThrow(() =>
                DeucarianBuildPathUtility.ToFullOutputPath(".github-backup/WebGL"));
            Assert.DoesNotThrow(() =>
                DeucarianBuildPathUtility.ToFullOutputPath("Builds/Documentation~/Output"));

            Assert.Throws<ArgumentException>(() =>
                DeucarianBuildPathUtility.ToFullOutputPath("../outside"));
            Assert.Throws<ArgumentException>(() =>
                DeucarianBuildPathUtility.ToFullOutputPath("Builds/../../outside"));
            Assert.Throws<ArgumentException>(() =>
                DeucarianBuildPathUtility.ToFullOutputPath("Builds/../Other"));
            Assert.Throws<ArgumentException>(() =>
                DeucarianBuildPathUtility.ToFullOutputPath("Builds\\..\\Other"));
            Assert.Throws<ArgumentException>(() =>
                DeucarianBuildPathUtility.ToFullOutputPath("."));
            Assert.Throws<ArgumentException>(() =>
                DeucarianBuildPathUtility.ToFullOutputPath(
                    Path.Combine(projectRoot, "Builds", "Absolute")));

            string projectName = new DirectoryInfo(projectRoot).Name;
            Assert.Throws<ArgumentException>(() =>
                DeucarianBuildPathUtility.ToFullOutputPath(
                    "../" + projectName + "-prefix-collision/Builds"));

            string caseToggledProjectName = ToggleAsciiCase(projectName);
            Assert.That(caseToggledProjectName, Is.Not.EqualTo(projectName));
            Assert.Throws<ArgumentException>(() =>
                DeucarianBuildPathUtility.ToFullOutputPath(
                    "../" + caseToggledProjectName + "/Builds"));
        }

        [TestCase(".git")]
        [TestCase(".git/objects")]
        [TestCase(".github/BuildOutput")]
        [TestCase(".codex/BuildOutput")]
        [TestCase(".agents/BuildOutput")]
        [TestCase("Assets")]
        [TestCase("Assets/BuildOutput")]
        [TestCase("Docs/BuildOutput")]
        [TestCase("Documentation/BuildOutput")]
        [TestCase("Documentation~/BuildOutput")]
        [TestCase("Packages/BuildOutput")]
        [TestCase("ProjectSettings/BuildOutput")]
        [TestCase("UserSettings/BuildOutput")]
        [TestCase("Library/BuildOutput")]
        [TestCase("Temp")]
        [TestCase("Temp/BuildOutput")]
        [TestCase("Logs")]
        [TestCase("Logs/BuildOutput")]
        public void OutputPathResolutionRejectsReservedProjectDirectories(string outputPath)
        {
            Assert.Throws<ArgumentException>(() =>
                DeucarianBuildPathUtility.ToFullOutputPath(outputPath));
        }

        [TestCase("Assets::$INDEX_ALLOCATION/BuildOutput")]
        [TestCase("ProjectSettings::$INDEX_ALLOCATION")]
        [TestCase("Builds/file:stream")]
        [TestCase("Builds::$DATA")]
        [TestCase(".git /objects")]
        [TestCase("Assets /BuildOutput")]
        [TestCase("Builds/Output.")]
        [TestCase("Builds/CON")]
        [TestCase("Builds/LPT9.txt")]
        [TestCase("Builds/CONIN$")]
        [TestCase("Builds/CONOUT$.txt")]
        [TestCase("Builds/CLOCK$")]
        [TestCase("Builds/COM\u00B9")]
        [TestCase("Builds/LPT\u00B3.log")]
        [TestCase("PROJEC~1/BuildOutput")]
        [TestCase("USERSE~1/BuildOutput")]
        [TestCase("Builds/OUTPUT~12.txt")]
        [TestCase("Builds/less<name")]
        [TestCase("Builds/greater>name")]
        [TestCase("Builds/pipe|name")]
        [TestCase("Builds/question?name")]
        [TestCase("Builds/star*name")]
        public void OutputPathResolutionRejectsUnsafeRelativeSegments(string outputPath)
        {
            Assert.Throws<ArgumentException>(() =>
                DeucarianBuildPathUtility.ToFullOutputPath(outputPath));
        }

        [Test]
        public void OutputPathResolutionRejectsQuoteAndControlCharacters()
        {
            Assert.Throws<ArgumentException>(() =>
                DeucarianBuildPathUtility.ToFullOutputPath(
                    string.Concat("Builds/quote", (char)34, "name")));
            Assert.Throws<ArgumentException>(() =>
                DeucarianBuildPathUtility.ToFullOutputPath(
                    "Builds/control" + (char)1 + "name"));
        }

        [Test]
        public void OutputPathResolutionRejectsLiveWindowsShortAliasesWhenPresent()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                return;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            Assert.That(projectRoot, Is.Not.Null.And.Not.Empty);
            AssertLiveWindowsAliasRejected(projectRoot, "PROJEC~1");
            AssertLiveWindowsAliasRejected(projectRoot, "USERSE~1");
        }

        [Test]
        public void OutputCleanupDeletesEmptyDirectoryAndReturnsCanonicalPath()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            Assert.That(projectRoot, Is.Not.Null.And.Not.Empty);
            string buildsRoot = Path.Combine(projectRoot, "Builds");
            bool buildsRootExisted = Directory.Exists(buildsRoot);
            string testRoot = Path.Combine(buildsRoot, "__DeucarianCleanupTests");
            bool testRootExisted = Directory.Exists(testRoot);
            string relativePath = "Builds/__DeucarianCleanupTests/"
                                  + Guid.NewGuid().ToString("N");
            string fullPath = DeucarianBuildPathUtility.ToFullOutputPath(relativePath);
            Directory.CreateDirectory(fullPath);
            try
            {
                string cleanedPath =
                    DeucarianBuildPathUtility.CleanProjectContainedOutputDirectory(
                        relativePath);

                Assert.That(cleanedPath, Is.EqualTo(fullPath));
                Assert.That(Path.IsPathRooted(cleanedPath), Is.True);
                Assert.That(Directory.Exists(fullPath), Is.False);
            }
            finally
            {
                DeleteDirectoryIfPresent(fullPath);
                DeleteEmptyDirectoryIfCreated(testRoot, testRootExisted);
                DeleteEmptyDirectoryIfCreated(buildsRoot, buildsRootExisted);
            }
        }

        [Test]
        public void OutputCleanupDeletesOnlyTheSelectedSafeDirectory()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            Assert.That(projectRoot, Is.Not.Null.And.Not.Empty);
            string buildsRoot = Path.Combine(projectRoot, "Builds");
            bool buildsRootExisted = Directory.Exists(buildsRoot);
            string testId = Guid.NewGuid().ToString("N");
            string testRoot = Path.Combine(buildsRoot, "__DeucarianCleanupTests");
            bool testRootExisted = Directory.Exists(testRoot);
            string selectedRelative = "Builds/__DeucarianCleanupTests/" + testId;
            string selectedFull = DeucarianBuildPathUtility.ToFullOutputPath(selectedRelative);
            string siblingFull = selectedFull + "-keep";
            string markerPath = Path.Combine(siblingFull, "keep.txt");

            Directory.CreateDirectory(Path.Combine(selectedFull, "Nested"));
            File.WriteAllText(Path.Combine(selectedFull, "Nested", "stale.txt"), "stale");
            WriteValidBuildManifest(selectedFull);
            Directory.CreateDirectory(siblingFull);
            File.WriteAllText(markerPath, "keep");
            try
            {
                DeucarianBuildPathUtility.CleanProjectContainedOutputDirectory(
                    selectedRelative);

                Assert.That(Directory.Exists(selectedFull), Is.False);
                Assert.That(File.Exists(markerPath), Is.True);
            }
            finally
            {
                DeleteDirectoryIfPresent(selectedFull);
                DeleteDirectoryIfPresent(siblingFull);
                DeleteEmptyDirectoryIfCreated(testRoot, testRootExisted);
                DeleteEmptyDirectoryIfCreated(buildsRoot, buildsRootExisted);
            }
        }

        [Test]
        public void OutputCleanupRefusesNonemptyUnownedDirectory()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            Assert.That(projectRoot, Is.Not.Null.And.Not.Empty);
            string buildsRoot = Path.Combine(projectRoot, "Builds");
            bool buildsRootExisted = Directory.Exists(buildsRoot);
            string testRoot = Path.Combine(buildsRoot, "__DeucarianCleanupTests");
            bool testRootExisted = Directory.Exists(testRoot);
            string relativePath = "Builds/__DeucarianCleanupTests/"
                                  + Guid.NewGuid().ToString("N");
            string fullPath = DeucarianBuildPathUtility.ToFullOutputPath(relativePath);
            string markerPath = Path.Combine(fullPath, "user-content.txt");
            Directory.CreateDirectory(fullPath);
            File.WriteAllText(markerPath, "keep");
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    DeucarianBuildPathUtility.CleanProjectContainedOutputDirectory(
                        relativePath));
                Assert.That(File.Exists(markerPath), Is.True);
            }
            finally
            {
                DeleteDirectoryIfPresent(fullPath);
                DeleteEmptyDirectoryIfCreated(testRoot, testRootExisted);
                DeleteEmptyDirectoryIfCreated(buildsRoot, buildsRootExisted);
            }
        }

        [Test]
        public void OutputCleanupRejectsInvalidOwnershipManifest()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            Assert.That(projectRoot, Is.Not.Null.And.Not.Empty);
            string buildsRoot = Path.Combine(projectRoot, "Builds");
            bool buildsRootExisted = Directory.Exists(buildsRoot);
            string testRoot = Path.Combine(buildsRoot, "__DeucarianCleanupTests");
            bool testRootExisted = Directory.Exists(testRoot);
            string relativePath = "Builds/__DeucarianCleanupTests/"
                                  + Guid.NewGuid().ToString("N");
            string fullPath = DeucarianBuildPathUtility.ToFullOutputPath(relativePath);
            string manifestPath = Path.Combine(
                fullPath,
                DeucarianBuildArtifactManifest.FileName);
            Directory.CreateDirectory(fullPath);
            File.WriteAllText(manifestPath, "{}");
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    DeucarianBuildPathUtility.CleanProjectContainedOutputDirectory(
                        relativePath));
                Assert.That(File.Exists(manifestPath), Is.True);
            }
            finally
            {
                DeleteDirectoryIfPresent(fullPath);
                DeleteEmptyDirectoryIfCreated(testRoot, testRootExisted);
                DeleteEmptyDirectoryIfCreated(buildsRoot, buildsRootExisted);
            }
        }

        [TestCase("stale-schema")]
        [TestCase("numeric-defined-environment")]
        [TestCase("numeric-undefined-environment")]
        [TestCase("undefined-environment")]
        [TestCase("null-budget")]
        [TestCase("null-artifacts")]
        public void OutputCleanupRejectsInvalidOwnershipManifestFields(string mutation)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            Assert.That(projectRoot, Is.Not.Null.And.Not.Empty);
            string buildsRoot = Path.Combine(projectRoot, "Builds");
            bool buildsRootExisted = Directory.Exists(buildsRoot);
            string testRoot = Path.Combine(buildsRoot, "__DeucarianCleanupTests");
            bool testRootExisted = Directory.Exists(testRoot);
            string relativePath = "Builds/__DeucarianCleanupTests/"
                                  + Guid.NewGuid().ToString("N");
            string fullPath = DeucarianBuildPathUtility.ToFullOutputPath(relativePath);
            string manifestPath = Path.Combine(
                fullPath,
                DeucarianBuildArtifactManifest.FileName);
            DeucarianBuildArtifactManifest manifest = CreateValidOwnershipManifest();
            switch (mutation)
            {
                case "stale-schema":
                    manifest.schemaVersion =
                        DeucarianBuildArtifactManifest.CurrentSchemaVersion + 1;
                    break;
                case "numeric-defined-environment":
                    manifest.environment = "0";
                    break;
                case "numeric-undefined-environment":
                    manifest.environment = "42";
                    break;
                case "undefined-environment":
                    manifest.environment = "Staging";
                    break;
                case "null-budget":
                    break;
                case "null-artifacts":
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown manifest mutation '" + mutation + "'.");
            }

            string manifestJson = manifest.ToJson();
            if (mutation == "null-budget")
            {
                manifestJson = ReplaceTopLevelJsonValueWithNull(
                    manifestJson, "budget", '{', '}');
                StringAssert.Contains(
                    string.Concat((char)34, "budget", (char)34) + ": null",
                    manifestJson);
            }
            else if (mutation == "null-artifacts")
            {
                manifestJson = ReplaceTopLevelJsonValueWithNull(
                    manifestJson, "artifacts", '[', ']');
                StringAssert.Contains(
                    string.Concat((char)34, "artifacts", (char)34) + ": null",
                    manifestJson);
            }

            Directory.CreateDirectory(fullPath);
            File.WriteAllText(manifestPath, manifestJson);
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    DeucarianBuildPathUtility.CleanProjectContainedOutputDirectory(
                        relativePath));
                Assert.That(File.Exists(manifestPath), Is.True);
            }
            finally
            {
                DeleteDirectoryIfPresent(fullPath);
                DeleteEmptyDirectoryIfCreated(testRoot, testRootExisted);
                DeleteEmptyDirectoryIfCreated(buildsRoot, buildsRootExisted);
            }
        }

        [Test]
        public void OutputCleanupRejectsTraversalWithoutDeletingIsolatedMarker()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            Assert.That(projectRoot, Is.Not.Null.And.Not.Empty);
            string buildsRoot = Path.Combine(projectRoot, "Builds");
            bool buildsRootExisted = Directory.Exists(buildsRoot);
            string testRoot = Path.Combine(buildsRoot, "__DeucarianCleanupTests");
            bool testRootExisted = Directory.Exists(testRoot);
            string caseRelative = "Builds/__DeucarianCleanupTests/"
                                  + Guid.NewGuid().ToString("N");
            string caseFull = DeucarianBuildPathUtility.ToFullOutputPath(caseRelative);
            string protectedFull = Path.Combine(caseFull, "Protected");
            string markerPath = Path.Combine(protectedFull, "keep.txt");
            Directory.CreateDirectory(protectedFull);
            File.WriteAllText(markerPath, "keep");
            try
            {
                Assert.Throws<ArgumentException>(() =>
                    DeucarianBuildPathUtility.CleanProjectContainedOutputDirectory(
                        caseRelative + "/Output/../Protected"));
                Assert.That(File.Exists(markerPath), Is.True);
            }
            finally
            {
                DeleteDirectoryIfPresent(caseFull);
                DeleteEmptyDirectoryIfCreated(testRoot, testRootExisted);
                DeleteEmptyDirectoryIfCreated(buildsRoot, buildsRootExisted);
            }
        }

        [Test]
        public void OutputPathResolutionRejectsDirectoryLinkAncestorWhenAvailable()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            Assert.That(projectRoot, Is.Not.Null.And.Not.Empty);
            string buildsRoot = Path.Combine(projectRoot, "Builds");
            bool buildsRootExisted = Directory.Exists(buildsRoot);
            string testRoot = Path.Combine(buildsRoot, "__DeucarianLinkTests");
            bool testRootExisted = Directory.Exists(testRoot);
            string caseRelative = "Builds/__DeucarianLinkTests/"
                                  + Guid.NewGuid().ToString("N");
            string caseFull = DeucarianBuildPathUtility.ToFullOutputPath(caseRelative);
            string targetFull = Path.Combine(caseFull, "Target");
            string linkedFull = Path.Combine(caseFull, "AncestorLink");
            Directory.CreateDirectory(Path.Combine(targetFull, "Output"));
            try
            {
                string unavailableReason;
                if (!TryCreateDirectoryLink(
                        linkedFull,
                        targetFull,
                        out unavailableReason))
                {
                    Assert.Ignore(
                        "Directory-link creation is unavailable: " + unavailableReason);
                }

                Assert.Throws<ArgumentException>(() =>
                    DeucarianBuildPathUtility.ToFullOutputPath(
                        caseRelative + "/AncestorLink/Output"));
            }
            finally
            {
                DeleteDirectoryLinkIfPresent(linkedFull);
                DeleteDirectoryIfPresent(caseFull);
                DeleteEmptyDirectoryIfCreated(testRoot, testRootExisted);
                DeleteEmptyDirectoryIfCreated(buildsRoot, buildsRootExisted);
            }
        }

        [Test]
        public void OutputCleanupRejectsDirectoryLinkDescendantWhenAvailable()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            Assert.That(projectRoot, Is.Not.Null.And.Not.Empty);
            string buildsRoot = Path.Combine(projectRoot, "Builds");
            bool buildsRootExisted = Directory.Exists(buildsRoot);
            string testRoot = Path.Combine(buildsRoot, "__DeucarianLinkTests");
            bool testRootExisted = Directory.Exists(testRoot);
            string caseRelative = "Builds/__DeucarianLinkTests/"
                                  + Guid.NewGuid().ToString("N");
            string caseFull = DeucarianBuildPathUtility.ToFullOutputPath(caseRelative);
            string outputRelative = caseRelative + "/Output";
            string outputFull =
                DeucarianBuildPathUtility.ToFullOutputPath(outputRelative);
            string targetFull = Path.Combine(caseFull, "Target");
            string linkedFull = Path.Combine(outputFull, "DescendantLink");
            string targetMarker = Path.Combine(targetFull, "keep.txt");
            Directory.CreateDirectory(outputFull);
            Directory.CreateDirectory(targetFull);
            File.WriteAllText(targetMarker, "keep");
            WriteValidBuildManifest(outputFull);
            try
            {
                string unavailableReason;
                if (!TryCreateDirectoryLink(
                        linkedFull,
                        targetFull,
                        out unavailableReason))
                {
                    Assert.Ignore(
                        "Directory-link creation is unavailable: " + unavailableReason);
                }

                Assert.Throws<ArgumentException>(() =>
                    DeucarianBuildPathUtility.CleanProjectContainedOutputDirectory(
                        outputRelative));
                Assert.That(File.Exists(targetMarker), Is.True);
                Assert.That(Directory.Exists(outputFull), Is.True);
            }
            finally
            {
                DeleteDirectoryLinkIfPresent(linkedFull);
                DeleteDirectoryIfPresent(caseFull);
                DeleteEmptyDirectoryIfCreated(testRoot, testRootExisted);
                DeleteEmptyDirectoryIfCreated(buildsRoot, buildsRootExisted);
            }
        }

        [Test]
        public void ManifestSerializationContainsVersionsFingerprintAndBudget()
        {
            DeucarianBuildArtifactManifest manifest = CreatePassingProductionManifest();
            manifest.packageVersion = "0.2.0";
            manifest.unityVersion = Application.unityVersion;
            manifest.settingsFingerprint = "abc123";

            string json = manifest.ToJson();

            StringAssert.Contains("\"packageVersion\": \"0.2.0\"", json);
            StringAssert.Contains("\"settingsFingerprint\": \"abc123\"", json);
            StringAssert.Contains("\"passed\": true", json);
        }

        private static DeucarianBuildArtifactManifest CreatePassingProductionManifest()
        {
            DeucarianBuildArtifactManifest manifest = new DeucarianBuildArtifactManifest();
            manifest.budget.limitBytes = DeucarianWebGLBuildPolicy.ProductionBootstrapBudgetBytes;
            manifest.budget.encodedBootstrapBytes = 10 * 1024 * 1024;
            manifest.budget.passed = true;
            manifest.artifacts.Add(CreatePayload("0123456789abcdef.data.br", "data"));
            manifest.artifacts.Add(CreatePayload("1234567890abcdef.framework.js.br", "framework"));
            manifest.artifacts.Add(CreatePayload("2345678901abcdef.wasm.br", "wasm"));
            return manifest;
        }

        private static DeucarianBuildArtifact CreatePayload(string fileName, string classification)
        {
            return new DeucarianBuildArtifact
            {
                relativePath = "Build/" + fileName,
                classification = classification,
                encoding = "br",
                encodedBytes = 1,
                rawBytes = 2,
                preEngineBootstrap = true
            };
        }

        private static void WriteValidBuildManifest(string outputDirectory)
        {
            CreateValidOwnershipManifest().WriteTo(outputDirectory);
        }

        private static DeucarianBuildArtifactManifest CreateValidOwnershipManifest()
        {
            return new DeucarianBuildArtifactManifest
            {
                packageVersion = "0.2.0",
                unityVersion = Application.unityVersion,
                environment = DeucarianBuildEnvironment.Production.ToString(),
                buildGuid = "test-owned-output"
            };
        }

        private static string ReplaceTopLevelJsonValueWithNull(
            string json,
            string propertyName,
            char openingToken,
            char closingToken)
        {
            string propertyMarker =
                string.Concat((char)34, propertyName, (char)34) + ":";
            int propertyIndex = json.IndexOf(
                propertyMarker,
                StringComparison.Ordinal);
            if (propertyIndex < 0)
            {
                throw new InvalidOperationException(
                    "JSON property '" + propertyName + "' was not found.");
            }

            int valueStart = propertyIndex + propertyMarker.Length;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
            {
                valueStart++;
            }

            if (valueStart >= json.Length || json[valueStart] != openingToken)
            {
                throw new InvalidOperationException(
                    "JSON property '" + propertyName + "' has an unexpected value.");
            }

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int i = valueStart; i < json.Length; i++)
            {
                char character = json[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (character == '"')
                {
                    inString = true;
                }
                else if (character == openingToken)
                {
                    depth++;
                }
                else if (character == closingToken)
                {
                    depth--;
                    if (depth == 0)
                    {
                        return json.Substring(0, valueStart)
                               + "null"
                               + json.Substring(i + 1);
                    }
                }
            }

            throw new InvalidOperationException(
                "JSON property '" + propertyName + "' did not terminate.");
        }

        private static BuildOptions FindUndeclaredBuildOptionBit()
        {
            int declaredMask = 0;
            foreach (BuildOptions option in Enum.GetValues(typeof(BuildOptions)))
            {
                declaredMask |= (int)option;
            }

            for (int bit = 0; bit < 32; bit++)
            {
                int candidate = unchecked(1 << bit);
                if ((declaredMask & candidate) == 0)
                {
                    return (BuildOptions)candidate;
                }
            }

            Assert.Fail("This Unity version exposes no undeclared BuildOptions bit.");
            return BuildOptions.None;
        }

        private static void AssertLiveWindowsAliasRejected(
            string projectRoot,
            string alias)
        {
            string aliasPath = Path.Combine(projectRoot, alias);
            if (!Directory.Exists(aliasPath))
            {
                return;
            }

            Assert.Throws<ArgumentException>(
                () => DeucarianBuildPathUtility.ToFullOutputPath(
                    alias + "/BuildOutput"),
                "Live DOS alias '" + alias + "' must be rejected.");
        }

        private static string ToggleAsciiCase(string value)
        {
            char[] characters = value.ToCharArray();
            for (int i = 0; i < characters.Length; i++)
            {
                if (characters[i] >= 'a' && characters[i] <= 'z')
                {
                    characters[i] = char.ToUpperInvariant(characters[i]);
                }
                else if (characters[i] >= 'A' && characters[i] <= 'Z')
                {
                    characters[i] = char.ToLowerInvariant(characters[i]);
                }
            }

            return new string(characters);
        }

        private static bool TryCreateDirectoryLink(
            string linkPath,
            string targetPath,
            out string unavailableReason)
        {
            try
            {
                ProcessStartInfo startInfo;
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    startInfo = new ProcessStartInfo
                    {
                        FileName =
                            Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                        Arguments = "/d /c mklink /J "
                                    + QuoteProcessArgument(linkPath)
                                    + " "
                                    + QuoteProcessArgument(targetPath)
                    };
                }
                else if (Application.platform == RuntimePlatform.OSXEditor
                         || Application.platform == RuntimePlatform.LinuxEditor)
                {
                    startInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/ln",
                        Arguments = "-s "
                                    + QuoteProcessArgument(targetPath)
                                    + " "
                                    + QuoteProcessArgument(linkPath)
                    };
                }
                else
                {
                    unavailableReason =
                        "unsupported editor platform " + Application.platform + ".";
                    return false;
                }

                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        unavailableReason = "the link process did not start.";
                        return false;
                    }

                    string standardOutput = process.StandardOutput.ReadToEnd();
                    string standardError = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        unavailableReason =
                            (standardError + " " + standardOutput).Trim();
                        return false;
                    }
                }

                if (!Directory.Exists(linkPath))
                {
                    unavailableReason = "the link command created no directory link.";
                    return false;
                }

                FileAttributes attributes = File.GetAttributes(linkPath);
                if ((attributes & FileAttributes.ReparsePoint) == 0)
                {
                    unavailableReason =
                        "the created path is not reported as a reparse point.";
                    return false;
                }

                unavailableReason = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                unavailableReason =
                    exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private static string QuoteProcessArgument(string value)
        {
            return string.Concat((char)34, value, (char)34);
        }

        private static void DeleteDirectoryLinkIfPresent(string linkPath)
        {
            if (!Directory.Exists(linkPath))
            {
                return;
            }

            try
            {
                Directory.Delete(linkPath);
            }
            catch (IOException)
            {
                File.Delete(linkPath);
            }
        }

        private static void DeleteFolderIfCreatedAndEmpty(
            string assetPath,
            bool existedBeforeTest)
        {
            if (existedBeforeTest || !AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string fullPath = Path.GetFullPath(
                Path.Combine(projectRoot ?? string.Empty, assetPath));
            if (Directory.Exists(fullPath)
                && Directory.GetFileSystemEntries(fullPath).Length == 0)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        private static void DeleteDirectoryIfPresent(string fullPath)
        {
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, true);
            }
        }

        private static void DeleteEmptyDirectoryIfCreated(
            string fullPath,
            bool existedBeforeTest)
        {
            if (!existedBeforeTest
                && Directory.Exists(fullPath)
                && Directory.GetFileSystemEntries(fullPath).Length == 0)
            {
                Directory.Delete(fullPath);
            }
        }
    }
}
