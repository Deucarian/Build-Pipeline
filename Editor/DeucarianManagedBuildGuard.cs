using UnityEditor.Build;
using UnityEditor.Build.Profile;

namespace Deucarian.BuildPipeline
{
    internal sealed class DeucarianManagedBuildGuard : BuildPlayerProcessor
    {
        public override int callbackOrder => int.MinValue;

        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (DeucarianBuildExecutionScope.IsActive)
            {
                return;
            }

            BuildProfile activeProfile = BuildProfile.GetActiveBuildProfile();
            if (activeProfile == null
                || !DeucarianUnityBuildBridge.IsProfileRegistered(activeProfile))
            {
                return;
            }

            throw new BuildFailedException(
                "This Build Profile is managed by the Deucarian Build Pipeline. "
                + "Use Unity's Build or Build And Run button, the Build Pipeline Manager, "
                + "or a registered Deucarian command-line entry point.");
        }
    }
}
