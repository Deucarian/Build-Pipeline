using System;
using System.Collections.Generic;
using UnityEditor.Build;

namespace Deucarian.BuildPipeline
{
    internal static class DeucarianBuildLifecyclePipeline
    {
        internal static DeucarianBuildLifecycleSelection SelectAndValidate(
            DeucarianBuildRequest request,
            DeucarianBuildLifecycleDiscoveryResult discovery)
        {
            DeucarianBuildLifecycleSelection selection =
                new DeucarianBuildLifecycleSelection();
            if (discovery == null)
            {
                selection.Validation.Add(
                    "Build lifecycle discovery returned no result.");
                return selection;
            }

            selection.Validation.AddRange(discovery.Issues);
            for (int i = 0; i < discovery.Entries.Count; i++)
            {
                DeucarianBuildLifecycleEntry entry = discovery.Entries[i];
                bool applies;
                try
                {
                    applies = entry.Contributor.AppliesTo(request);
                }
                catch (Exception exception)
                {
                    selection.Validation.Add(
                        "Build lifecycle contributor '" + entry.Id
                        + "' failed applicability evaluation ("
                        + DeucarianBuildLifecycleDiscovery.GetExceptionName(exception)
                        + ").");
                    continue;
                }

                if (!applies)
                {
                    continue;
                }

                selection.Entries.Add(entry);
                AddContributorValidation(selection.Validation, entry, request);
            }

            return selection;
        }

        internal static DeucarianBuildLifecycleScopeSet Prepare(
            DeucarianBuildRequest request,
            DeucarianBuildLifecycleSelection selection)
        {
            List<DeucarianBuildLifecycleScopeSet.PreparedScope> prepared =
                new List<DeucarianBuildLifecycleScopeSet.PreparedScope>();
            for (int i = 0; i < selection.Entries.Count; i++)
            {
                DeucarianBuildLifecycleEntry entry = selection.Entries[i];
                try
                {
                    IDisposable scope = entry.Contributor.Prepare(request);
                    if (scope == null)
                    {
                        throw new InvalidOperationException(
                            "Contributor returned no restoration scope.");
                    }

                    prepared.Add(
                        new DeucarianBuildLifecycleScopeSet.PreparedScope
                        {
                            Id = entry.Id,
                            Scope = scope
                        });
                }
                catch (Exception exception)
                {
                    DeucarianBuildLifecycleRestorationResult cleanup =
                        DisposeReverseDetailed(prepared);
                    string message = "Build lifecycle contributor '" + entry.Id
                                     + "' failed preparation ("
                                     + DeucarianBuildLifecycleDiscovery.GetExceptionName(exception)
                                     + ").";
                    InvalidOperationException preparationFailure =
                        new InvalidOperationException(message, exception);
                    if (!cleanup.Validation.IsValid)
                    {
                        List<Exception> failures = new List<Exception>
                        {
                            preparationFailure
                        };
                        failures.AddRange(cleanup.Failures);
                        throw DeucarianSanitizedBuildFailure.From(
                            message + "\n" + cleanup.Validation.Format(
                                "Partial build lifecycle restoration also " +
                                "failed"),
                            new AggregateException(
                                message
                                + " Partial build lifecycle restoration also failed.",
                                failures));
                    }

                    throw DeucarianSanitizedBuildFailure.From(
                        message,
                        preparationFailure);
                }
            }

            return new DeucarianBuildLifecycleScopeSet(prepared);
        }

        internal static DeucarianBuildValidationResult ValidateArtifacts(
            DeucarianBuildRequest request,
            DeucarianBuildArtifactManifest manifest,
            DeucarianBuildLifecycleSelection selection)
        {
            DeucarianBuildValidationResult result =
                new DeucarianBuildValidationResult();
            for (int i = 0; i < selection.Entries.Count; i++)
            {
                DeucarianBuildLifecycleEntry entry = selection.Entries[i];
                try
                {
                    DeucarianBuildValidationResult validation =
                        entry.Contributor.ValidateGeneratedArtifacts(
                            request,
                            manifest);
                    if (validation == null)
                    {
                        result.Add(
                            "Build lifecycle contributor '" + entry.Id
                            + "' returned no artifact validation result.");
                    }
                    else
                    {
                        result.AddRange(validation.Issues);
                    }
                }
                catch (Exception exception)
                {
                    result.Add(
                        "Build lifecycle contributor '" + entry.Id
                        + "' failed artifact validation ("
                        + DeucarianBuildLifecycleDiscovery.GetExceptionName(exception)
                        + ").");
                }
            }

            return result;
        }

        internal static DeucarianBuildLifecycleRestorationResult
            DisposeReverseDetailed(
                List<DeucarianBuildLifecycleScopeSet.PreparedScope> prepared)
        {
            DeucarianBuildLifecycleRestorationResult result =
                new DeucarianBuildLifecycleRestorationResult();
            for (int i = prepared.Count - 1; i >= 0; i--)
            {
                DeucarianBuildLifecycleScopeSet.PreparedScope preparedScope =
                    prepared[i];
                try
                {
                    preparedScope.Scope.Dispose();
                }
                catch (Exception exception)
                {
                    string issue = "Build lifecycle contributor '"
                                   + preparedScope.Id
                                   + "' failed restoration ("
                                   + DeucarianBuildLifecycleDiscovery
                                       .GetExceptionName(exception)
                                   + ").";
                    result.Validation.Add(issue);
                    result.Failures.Add(
                        new InvalidOperationException(issue, exception));
                }
            }

            prepared.Clear();
            return result;
        }

        private static void AddContributorValidation(
            DeucarianBuildValidationResult aggregate,
            DeucarianBuildLifecycleEntry entry,
            DeucarianBuildRequest request)
        {
            try
            {
                DeucarianBuildValidationResult validation =
                    entry.Contributor.ValidateBeforeBuild(request);
                if (validation == null)
                {
                    aggregate.Add(
                        "Build lifecycle contributor '" + entry.Id
                        + "' returned no prebuild validation result.");
                }
                else
                {
                    aggregate.AddRange(validation.Issues);
                }
            }
            catch (Exception exception)
            {
                aggregate.Add(
                    "Build lifecycle contributor '" + entry.Id
                    + "' failed prebuild validation ("
                    + DeucarianBuildLifecycleDiscovery.GetExceptionName(exception)
                    + ").");
            }
        }
    }
}
