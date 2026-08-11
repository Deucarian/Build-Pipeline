using NUnit.Framework;

namespace Deucarian.BuildPipeline.Tests
{
    public sealed class DeucarianAotSafetyBuildStateTests
    {
        [TearDown]
        public void TearDown()
        {
            DeucarianAotSafetyBuildState.Clear();
        }

        [Test]
        public void BeginPublishesModeOutsideThreadLocalExecutionScope()
        {
            Assert.That(DeucarianAotSafetyBuildState.CurrentMode, Is.Null);

            DeucarianAotSafetyBuildState.Begin(
                DeucarianAotSafetyMode.Enforce,
                new DeucarianAotSafetyReport());

            Assert.That(
                DeucarianAotSafetyBuildState.CurrentMode,
                Is.EqualTo(DeucarianAotSafetyMode.Enforce));
            Assert.That(
                DeucarianAotSafetyBuildState.Snapshot().mode,
                Is.EqualTo("Enforce"));
        }

        [Test]
        public void ClearRemovesPublishedModeAndReport()
        {
            DeucarianAotSafetyBuildState.Begin(
                DeucarianAotSafetyMode.Audit,
                new DeucarianAotSafetyReport());

            DeucarianAotSafetyBuildState.Clear();

            Assert.That(DeucarianAotSafetyBuildState.CurrentMode, Is.Null);
            Assert.That(
                DeucarianAotSafetyBuildState.Snapshot().mode,
                Is.EqualTo("Audit"));
        }
    }
}
