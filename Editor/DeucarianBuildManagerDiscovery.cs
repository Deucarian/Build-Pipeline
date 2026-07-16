using System;
using System.Collections.Generic;
using UnityEditor;

namespace Deucarian.BuildPipeline
{
    internal sealed class DeucarianBuildManagerProviderEntry
    {
        public IDeucarianBuildManagerProvider Provider { get; set; }
        public DeucarianBuildManagerTarget Target { get; set; }
        public string Key { get; set; }
        public string Label { get; set; }
    }

    internal sealed class DeucarianBuildManagerDiscoveryResult
    {
        public List<DeucarianBuildManagerProviderEntry> Entries { get; } =
            new List<DeucarianBuildManagerProviderEntry>();

        public List<string> Issues { get; } = new List<string>();
    }

    internal static class DeucarianBuildManagerDiscovery
    {
        public static DeucarianBuildManagerDiscoveryResult Discover()
        {
            return DiscoverFromTypes(TypeCache.GetTypesDerivedFrom<IDeucarianBuildManagerProvider>());
        }

        internal static DeucarianBuildManagerDiscoveryResult DiscoverFromTypes(
            IEnumerable<Type> providerTypes)
        {
            DeucarianBuildManagerDiscoveryResult result =
                new DeucarianBuildManagerDiscoveryResult();
            if (providerTypes == null)
            {
                return result;
            }

            List<IDeucarianBuildManagerProvider> providers =
                new List<IDeucarianBuildManagerProvider>();
            HashSet<string> providerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<Type> sortedTypes = new List<Type>(providerTypes);
            sortedTypes.Sort((left, right) => string.Compare(
                left != null ? left.FullName : string.Empty,
                right != null ? right.FullName : string.Empty,
                StringComparison.Ordinal));

            for (int i = 0; i < sortedTypes.Count; i++)
            {
                Type type = sortedTypes[i];
                if (type == null || type.IsAbstract || type.IsInterface || type.ContainsGenericParameters)
                {
                    continue;
                }

                try
                {
                    IDeucarianBuildManagerProvider provider =
                        Activator.CreateInstance(type) as IDeucarianBuildManagerProvider;
                    if (provider == null)
                    {
                        result.Issues.Add("Could not instantiate build provider '" + type.FullName + "'.");
                        continue;
                    }

                    string providerId = Normalize(provider.Id);
                    if (string.IsNullOrEmpty(providerId))
                    {
                        result.Issues.Add("Build provider '" + type.FullName + "' has no stable ID.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(provider.DisplayName))
                    {
                        result.Issues.Add("Build provider '" + providerId + "' has no display name.");
                        continue;
                    }

                    if (!providerIds.Add(providerId))
                    {
                        result.Issues.Add("Duplicate build provider ID '" + providerId + "' was ignored.");
                        continue;
                    }

                    providers.Add(provider);
                }
                catch (Exception exception)
                {
                    result.Issues.Add(
                        "Build provider '" + type.FullName + "' failed to initialize: "
                        + exception.GetBaseException().Message);
                }
            }

            providers.Sort(CompareProviders);
            for (int providerIndex = 0; providerIndex < providers.Count; providerIndex++)
            {
                AddProviderTargets(result, providers[providerIndex]);
            }

            return result;
        }

        private static void AddProviderTargets(
            DeucarianBuildManagerDiscoveryResult result,
            IDeucarianBuildManagerProvider provider)
        {
            IReadOnlyList<DeucarianBuildManagerTarget> targets;
            try
            {
                targets = provider.GetTargets();
            }
            catch (Exception exception)
            {
                result.Issues.Add(
                    "Build provider '" + provider.Id + "' failed to enumerate targets: "
                    + exception.GetBaseException().Message);
                return;
            }

            if (targets == null)
            {
                result.Issues.Add("Build provider '" + provider.Id + "' returned no target collection.");
                return;
            }

            List<DeucarianBuildManagerTarget> sortedTargets =
                new List<DeucarianBuildManagerTarget>();
            HashSet<string> targetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < targets.Count; i++)
            {
                DeucarianBuildManagerTarget target = targets[i];
                if (target == null)
                {
                    result.Issues.Add("Build provider '" + provider.Id + "' returned a null target.");
                    continue;
                }

                string targetId = Normalize(target.Id);
                if (!targetIds.Add(targetId))
                {
                    result.Issues.Add(
                        "Build provider '" + provider.Id + "' returned duplicate target ID '"
                        + targetId + "'.");
                    continue;
                }

                sortedTargets.Add(target);
            }

            sortedTargets.Sort((left, right) =>
            {
                int display = string.Compare(
                    left.DisplayName,
                    right.DisplayName,
                    StringComparison.OrdinalIgnoreCase);
                return display != 0
                    ? display
                    : string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
            });

            for (int i = 0; i < sortedTargets.Count; i++)
            {
                DeucarianBuildManagerTarget target = sortedTargets[i];
                result.Entries.Add(new DeucarianBuildManagerProviderEntry
                {
                    Provider = provider,
                    Target = target,
                    Key = Normalize(provider.Id) + "/" + Normalize(target.Id),
                    Label = provider.DisplayName.Trim() + " — " + target.DisplayName
                });
            }
        }

        private static int CompareProviders(
            IDeucarianBuildManagerProvider left,
            IDeucarianBuildManagerProvider right)
        {
            int order = left.Order.CompareTo(right.Order);
            if (order != 0)
            {
                return order;
            }

            int display = string.Compare(
                left.DisplayName,
                right.DisplayName,
                StringComparison.OrdinalIgnoreCase);
            return display != 0
                ? display
                : string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
