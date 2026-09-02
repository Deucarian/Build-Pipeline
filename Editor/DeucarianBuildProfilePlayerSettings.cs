using System;
using UnityEditor;

namespace Deucarian.BuildPipeline
{
    /// <summary>
    /// The small, product-neutral Player Settings contract that a consuming
    /// package may explicitly persist in a Unity Build Profile.
    /// </summary>
    public sealed class DeucarianBuildProfilePlayerSettings
    {
        public DeucarianBuildProfilePlayerSettings(
            string bundleVersion,
            bool runInBackground,
            InsecureHttpOption insecureHttpOption)
        {
            if (string.IsNullOrWhiteSpace(bundleVersion))
            {
                throw new ArgumentException(
                    "A non-empty bundle version is required.",
                    nameof(bundleVersion));
            }

            BundleVersion = bundleVersion.Trim();
            RunInBackground = runInBackground;
            InsecureHttpOption = insecureHttpOption;
        }

        public string BundleVersion { get; }
        public bool RunInBackground { get; }
        public InsecureHttpOption InsecureHttpOption { get; }
    }
}
