using Deucarian.Editor;
using NUnit.Framework;

namespace Deucarian.BuildPipeline.Tests
{
    public sealed class DeucarianBuildControlCenterTests
    {
        [Test]
        public void ValidatedRegisteredProfileShowsTargetEnvironmentAndSafeActions()
        {
            DeucarianBuildControlCenterSnapshot snapshot =
                new DeucarianBuildControlCenterSnapshot(
                    2, 0, true, "WebGL Profile", true, true,
                    "Viewer WebGL", "Production", 0);

            DeucarianControlCenterCard card =
                DeucarianBuildCardProvider.CreateCard(snapshot);

            Assert.AreEqual(DeucarianControlCenterStatus.Success, card.Status);
            Assert.That(card.Details, Has.Some.Contains("Workflow: Viewer WebGL"));
            Assert.That(card.Details, Has.Some.Contains("Environment: Production"));
            Assert.AreEqual(2, card.Actions.Count);
            Assert.AreEqual("build-pipeline.validate-active", card.Actions[1].Id);
            Assert.IsFalse(card.Actions[1].RequiresConfirmation);
        }

        [Test]
        public void AutomaticCardCaptureReportsValidationAsNotRun()
        {
            DeucarianBuildControlCenterSnapshot snapshot =
                new DeucarianBuildControlCenterSnapshot(
                    0, 0, true, "Profile", false, false,
                    string.Empty, string.Empty, 0);

            DeucarianControlCenterCard card =
                DeucarianBuildCardProvider.CreateCard(snapshot);

            Assert.AreEqual(DeucarianControlCenterStatus.Info, card.Status);
            Assert.AreEqual("Validation not run", card.StatusText);
        }
    }
}