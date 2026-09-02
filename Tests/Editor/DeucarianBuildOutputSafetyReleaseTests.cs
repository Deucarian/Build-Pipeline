using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Deucarian.BuildPipeline.Tests
{
    public sealed class DeucarianBuildOutputSafetyReleaseTests
    {
        private const string AssetFolder =
            "Assets/__DeucarianBuildOutputSafetyReleaseTests";

        private readonly List<string> temporaryOutputs = new List<string>();

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(AssetFolder))
            {
                AssetDatabase.DeleteAsset(AssetFolder);
            }

            for (int index = 0; index < temporaryOutputs.Count; index++)
            {
                string path = temporaryOutputs[index];
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }

            temporaryOutputs.Clear();
        }

        [Test]
        public void ExactBuildsRootAlwaysFailsClosed()
        {
            string buildsRoot = Path.Combine(ProjectRoot, "Builds");

            DeucarianBuildValidationResult validation =
                DeucarianBuildOutputUtility.ValidatePreparation(
                    buildsRoot,
                    BuildOptions.None);

            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Issues, Has.Some.Contains("Builds root"));
            Assert.Throws<BuildFailedException>(() =>
                DeucarianBuildOutputUtility.Prepare(
                    buildsRoot,
                    BuildOptions.None));
        }

        [Test]
        public void ReservedAssetsOutputPreservesFolderMetaAndSentinel()
        {
            AssetDatabase.CreateFolder(
                "Assets",
                "__DeucarianBuildOutputSafetyReleaseTests");
            string fullFolder = ToProjectPath(AssetFolder);
            string sentinel = Path.Combine(fullFolder, "sentinel.txt");
            File.WriteAllText(sentinel, "keep");
            AssetDatabase.ImportAsset(AssetFolder + "/sentinel.txt");
            string folderMeta = fullFolder + ".meta";
            Assert.That(File.Exists(folderMeta), Is.True);

            DeucarianBuildValidationResult validation =
                DeucarianBuildOutputUtility.ValidatePreparation(
                    AssetFolder,
                    BuildOptions.None);

            Assert.That(validation.IsValid, Is.False);
            Assert.Throws<BuildFailedException>(() =>
                DeucarianBuildOutputUtility.Prepare(
                    AssetFolder,
                    BuildOptions.None));
            Assert.That(Directory.Exists(fullFolder), Is.True);
            Assert.That(File.Exists(folderMeta), Is.True);
            Assert.That(File.ReadAllText(sentinel), Is.EqualTo("keep"));
        }

        [Test]
        public void EmptyNonBuildsDirectoryIsNeverDeleted()
        {
            string output = CreateOutput("Temp", "empty");

            DeucarianBuildOutputUtility.Prepare(output, BuildOptions.None);

            Assert.That(Directory.Exists(output), Is.True);
            Assert.That(Directory.GetFileSystemEntries(output), Is.Empty);
        }

        [Test]
        public void LinkedDescendantBlocksDeletionAndManifestTraversal()
        {
            string output = CreateOutput("Builds", "linked");
            string linkedSentinel = Path.Combine(output, "linked-sentinel");
            File.WriteAllText(linkedSentinel, "keep");
            int deletionCount = 0;
            int linkedEnumerationCount = 0;

            Func<string, FileAttributes> attributes = path =>
                string.Equals(path, linkedSentinel, StringComparison.Ordinal)
                    ? FileAttributes.Directory | FileAttributes.ReparsePoint
                    : FileAttributes.Directory;
            Func<string, string[]> entries = path =>
            {
                if (string.Equals(path, linkedSentinel, StringComparison.Ordinal))
                {
                    linkedEnumerationCount++;
                    return Array.Empty<string>();
                }

                return new[] { linkedSentinel };
            };

            BuildFailedException failure = Assert.Throws<BuildFailedException>(
                () => DeucarianBuildOutputUtility.DeleteExistingOutputWhenOwned(
                    output,
                    attributes,
                    entries,
                    (path, recursive) => deletionCount++));

            Assert.That(failure.Message, Does.Contain("filesystem link"));
            Assert.That(deletionCount, Is.Zero);
            Assert.That(linkedEnumerationCount, Is.Zero,
                "A linked directory must never be traversed.");
            Assert.That(File.ReadAllText(linkedSentinel), Is.EqualTo("keep"));

            bool collected = DeucarianBuildOutputPathSafety.TryCollectFiles(
                output,
                attributes,
                entries,
                out List<string> files,
                out string issue);
            Assert.That(collected, Is.False);
            Assert.That(issue, Does.Contain("filesystem link"));
            Assert.That(files, Is.Empty);
            Assert.That(linkedEnumerationCount, Is.Zero,
                "Artifact collection must stop before following a link.");
        }

        [Test]
        public void AncestorLinkRaceIsRecheckedAtDeletionBoundary()
        {
            string output = CreateOutput("Builds", "ancestor-link");
            string sentinel = Path.Combine(output, "sentinel.txt");
            File.WriteAllText(sentinel, "keep");
            string replacedAncestor = Path.GetDirectoryName(output);
            int deletionCount = 0;

            Func<string, FileAttributes> attributes = path =>
                string.Equals(
                    path,
                    replacedAncestor,
                    StringComparison.OrdinalIgnoreCase)
                    ? FileAttributes.Directory | FileAttributes.ReparsePoint
                    : File.Exists(path)
                        ? FileAttributes.Normal
                        : FileAttributes.Directory;
            Func<string, string[]> entries = path =>
                string.Equals(path, output, StringComparison.OrdinalIgnoreCase)
                    ? new[] { sentinel }
                    : Array.Empty<string>();

            BuildFailedException failure = Assert.Throws<BuildFailedException>(
                () => DeucarianBuildOutputUtility.DeleteExistingOutputWhenOwned(
                    output,
                    attributes,
                    entries,
                    (path, recursive) => deletionCount++));

            Assert.That(failure.Message, Does.Contain("filesystem link"));
            Assert.That(deletionCount, Is.Zero);
            Assert.That(File.ReadAllText(sentinel), Is.EqualTo("keep"));
        }

        private string CreateOutput(string root, string label)
        {
            string path = Path.Combine(
                ProjectRoot,
                root,
                "__DeucarianBuildOutputSafetyReleaseTests-" + label + "-"
                + Guid.NewGuid().ToString("N"));
            temporaryOutputs.Add(path);
            Directory.CreateDirectory(path);
            return path;
        }

        private static string ToProjectPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(
                ProjectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string ProjectRoot => Path.GetFullPath(
            Path.GetDirectoryName(Application.dataPath) ?? string.Empty);
    }
}
