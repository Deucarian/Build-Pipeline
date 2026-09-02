using System;
using System.Collections.Generic;
using UnityEditor;

namespace Deucarian.BuildPipeline
{
    internal static class DeucarianBuildLifecycleDiscovery
    {
        internal static DeucarianBuildLifecycleDiscoveryResult Discover()
        {
            return DiscoverFromTypes(
                TypeCache.GetTypesDerivedFrom<IDeucarianBuildLifecycleContributor>());
        }

        internal static DeucarianBuildLifecycleDiscoveryResult DiscoverFromTypes(
            IEnumerable<Type> contributorTypes)
        {
            DeucarianBuildLifecycleDiscoveryResult result =
                new DeucarianBuildLifecycleDiscoveryResult();
            if (contributorTypes == null)
            {
                return result;
            }

            List<Type> sortedTypes = new List<Type>(contributorTypes);
            sortedTypes.Sort(CompareTypes);
            HashSet<string> ids =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sortedTypes.Count; i++)
            {
                Type type = sortedTypes[i];
                if (type == null || type.IsAbstract || type.IsInterface
                    || type.ContainsGenericParameters)
                {
                    continue;
                }

                AddContributor(result, ids, type);
            }

            result.Entries.Sort(CompareEntries);
            return result;
        }

        private static void AddContributor(
            DeucarianBuildLifecycleDiscoveryResult result,
            HashSet<string> ids,
            Type type)
        {
            IDeucarianBuildLifecycleContributor contributor;
            string id;
            int order;
            try
            {
                contributor = Activator.CreateInstance(type)
                    as IDeucarianBuildLifecycleContributor;
                if (contributor == null)
                {
                    result.Issues.Add(
                        "Could not instantiate build lifecycle contributor '"
                        + GetTypeName(type) + "'.");
                    return;
                }

                id = string.IsNullOrWhiteSpace(contributor.Id)
                    ? string.Empty
                    : contributor.Id.Trim();
                order = contributor.Order;
            }
            catch (Exception exception)
            {
                result.Issues.Add(
                    "Build lifecycle contributor '" + GetTypeName(type)
                    + "' failed to initialize ("
                    + GetExceptionName(exception) + ").");
                return;
            }

            if (string.IsNullOrEmpty(id))
            {
                result.Issues.Add(
                    "Build lifecycle contributor '" + GetTypeName(type)
                    + "' has no stable ID.");
                return;
            }

            if (!ids.Add(id))
            {
                result.Issues.Add(
                    "Duplicate build lifecycle contributor ID '" + id + "'.");
                return;
            }

            result.Entries.Add(new DeucarianBuildLifecycleEntry
            {
                Id = id,
                Order = order,
                Type = type,
                Contributor = contributor
            });
        }

        private static int CompareEntries(
            DeucarianBuildLifecycleEntry left,
            DeucarianBuildLifecycleEntry right)
        {
            int order = left.Order.CompareTo(right.Order);
            if (order != 0)
            {
                return order;
            }

            int id = string.Compare(
                left.Id,
                right.Id,
                StringComparison.OrdinalIgnoreCase);
            return id != 0 ? id : CompareTypes(left.Type, right.Type);
        }

        private static int CompareTypes(Type left, Type right)
        {
            return string.Compare(
                GetTypeName(left),
                GetTypeName(right),
                StringComparison.Ordinal);
        }

        private static string GetTypeName(Type type)
        {
            return type != null ? type.FullName ?? type.Name : string.Empty;
        }

        internal static string GetExceptionName(Exception exception)
        {
            Exception root = exception != null ? exception.GetBaseException() : null;
            return root != null ? root.GetType().Name : "unknown error";
        }
    }
}
