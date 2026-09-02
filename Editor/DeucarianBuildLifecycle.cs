using System;
using System.Collections.Generic;
using UnityEditor.Build;

namespace Deucarian.BuildPipeline
{
    /// <summary>
    /// Adds product-neutral preparation and artifact checks to builds selected
    /// by <see cref="AppliesTo"/>. Implementations must have a public,
    /// parameterless constructor and keep construction, applicability, and
    /// prebuild validation free of side effects.
    /// </summary>
    public interface IDeucarianBuildLifecycleContributor
    {
        string Id { get; }
        int Order { get; }

        bool AppliesTo(DeucarianBuildRequest request);

        DeucarianBuildValidationResult ValidateBeforeBuild(
            DeucarianBuildRequest request);

        /// <summary>
        /// Applies temporary build state and returns the scope that restores it.
        /// An implementation that fails before returning must restore its own
        /// partial changes before throwing.
        /// </summary>
        IDisposable Prepare(DeucarianBuildRequest request);

        DeucarianBuildValidationResult ValidateGeneratedArtifacts(
            DeucarianBuildRequest request,
            DeucarianBuildArtifactManifest manifest);
    }

    internal sealed class DeucarianBuildLifecycleEntry
    {
        internal string Id { get; set; }
        internal int Order { get; set; }
        internal Type Type { get; set; }
        internal IDeucarianBuildLifecycleContributor Contributor { get; set; }
    }

    internal sealed class DeucarianBuildLifecycleDiscoveryResult
    {
        internal List<DeucarianBuildLifecycleEntry> Entries { get; } =
            new List<DeucarianBuildLifecycleEntry>();

        internal List<string> Issues { get; } = new List<string>();
    }

    internal sealed class DeucarianBuildLifecycleSelection
    {
        internal List<DeucarianBuildLifecycleEntry> Entries { get; } =
            new List<DeucarianBuildLifecycleEntry>();

        internal DeucarianBuildValidationResult Validation { get; } =
            new DeucarianBuildValidationResult();
    }

    internal sealed class DeucarianBuildLifecycleRestorationResult
    {
        internal DeucarianBuildValidationResult Validation { get; } =
            new DeucarianBuildValidationResult();

        internal List<Exception> Failures { get; } = new List<Exception>();

        internal Exception ToException(string heading)
        {
            if (Failures.Count == 0)
            {
                return new InvalidOperationException(
                    Validation.Format(heading));
            }

            return new AggregateException(heading, Failures);
        }
    }

    /// <summary>
    /// Creates the exact Unity exception type used by the existing public
    /// contract while retaining the causal graph outside the rendered
    /// exception chain. This keeps ordinary messages, logs, and
    /// <see cref="Exception.ToString"/> output sanitized.
    /// </summary>
    internal static class DeucarianSanitizedBuildFailure
    {
        private const string CauseDataKey =
            "Deucarian.BuildPipeline.SanitizedLifecycleCause";

        internal static BuildFailedException From(
            string sanitizedMessage,
            Exception cause)
        {
            if (cause == null)
            {
                throw new ArgumentNullException(nameof(cause));
            }

            string message = string.IsNullOrWhiteSpace(sanitizedMessage)
                ? "The build lifecycle failed."
                : sanitizedMessage;
            BuildFailedException failure = new BuildFailedException(message);
            failure.Data[CauseDataKey] = cause;
            return failure;
        }

        internal static Exception GetCause(BuildFailedException failure)
        {
            return failure?.Data[CauseDataKey] as Exception;
        }
    }

    internal sealed class DeucarianBuildLifecycleScopeSet : IDisposable
    {
        private readonly List<PreparedScope> preparedScopes;
        private bool disposed;

        internal DeucarianBuildLifecycleScopeSet(List<PreparedScope> preparedScopes)
        {
            this.preparedScopes = preparedScopes
                                  ?? new List<PreparedScope>();
            DeucarianBuildLifecycleScopeRegistry.Register(this);
        }

        public void Dispose()
        {
            DeucarianBuildLifecycleRestorationResult result =
                RestoreDetailed();
            if (!result.Validation.IsValid)
            {
                throw DeucarianSanitizedBuildFailure.From(
                    result.Validation.Format(
                        "Build lifecycle restoration failed"),
                    result.ToException(
                        "Build lifecycle restoration failed"));
            }
        }

        internal DeucarianBuildValidationResult Restore()
        {
            return RestoreDetailed().Validation;
        }

        internal DeucarianBuildLifecycleRestorationResult RestoreDetailed()
        {
            if (disposed)
            {
                return new DeucarianBuildLifecycleRestorationResult();
            }

            disposed = true;
            DeucarianBuildLifecycleScopeRegistry.Unregister(this);
            return DeucarianBuildLifecyclePipeline.DisposeReverseDetailed(
                preparedScopes);
        }

        internal sealed class PreparedScope
        {
            internal string Id { get; set; }
            internal IDisposable Scope { get; set; }
        }
    }
}
