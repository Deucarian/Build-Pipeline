using System;
using System.Collections.Generic;
using Mono.Cecil;

namespace Deucarian.BuildPipeline
{
    internal sealed class DeucarianAotAssemblyEvidence
    {
        internal const string FeatureMetadataKey =
            "Deucarian.AOT.Feature";
        internal const string ExceptionMetadataKey =
            "Deucarian.AOT.Exception";
        internal const string PreserveTypeMetadataKey =
            "Deucarian.AOT.PreserveType";

        private const string AssemblyMetadataAttributeName =
            "System.Reflection.AssemblyMetadataAttribute";

        private readonly List<DeucarianAotSafetyException> exceptions =
            new List<DeucarianAotSafetyException>();

        internal bool HasPreserveTypeDeclarations { get; private set; }

        internal static DeucarianAotAssemblyEvidence Read(
            AssemblyDefinition assembly,
            DeucarianAotSafetyReport report)
        {
            DeucarianAotAssemblyEvidence evidence =
                new DeucarianAotAssemblyEvidence();
            if (assembly == null)
            {
                return evidence;
            }

            for (int index = 0;
                 index < assembly.CustomAttributes.Count;
                 index++)
            {
                CustomAttribute attribute =
                    assembly.CustomAttributes[index];
                if (attribute.AttributeType.FullName !=
                    AssemblyMetadataAttributeName
                    || attribute.ConstructorArguments.Count != 2)
                {
                    continue;
                }

                string key =
                    attribute.ConstructorArguments[0].Value as string;
                string value =
                    attribute.ConstructorArguments[1].Value as string;
                if (string.Equals(
                        key,
                        FeatureMetadataKey,
                        StringComparison.Ordinal))
                {
                    report?.AddFeature(value);
                    continue;
                }

                if (string.Equals(
                        key,
                        PreserveTypeMetadataKey,
                        StringComparison.Ordinal))
                {
                    evidence.HasPreserveTypeDeclarations = true;
                    continue;
                }

                if (!string.Equals(
                        key,
                        ExceptionMetadataKey,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                DeucarianAotSafetyException exception;
                if (!TryParseException(
                        assembly.Name.Name,
                        value,
                        out exception))
                {
                    report?.AddFinding(new DeucarianAotSafetyFinding
                    {
                        category = "InvalidAotEvidence",
                        assemblyName = assembly.Name.Name,
                        message = "Assembly '" + assembly.Name.Name
                                  + "' contains invalid "
                                  + ExceptionMetadataKey
                                  + " metadata. Expected declaringType|method|calledApi|strategy|reason."
                    });
                    continue;
                }

                evidence.exceptions.Add(exception);
            }

            return evidence;
        }

        internal bool IsDeclaredException(
            string assemblyName,
            string declaringType,
            string method,
            string calledApi)
        {
            for (int i = 0; i < exceptions.Count; i++)
            {
                DeucarianAotSafetyException candidate = exceptions[i];
                if (!Matches(candidate.assemblyName, assemblyName)
                    || !Matches(candidate.declaringType, declaringType)
                    || !Matches(candidate.method, method)
                    || !Matches(candidate.calledApi, calledApi)
                    || string.IsNullOrWhiteSpace(candidate.reason)
                    || string.IsNullOrWhiteSpace(candidate.strategy))
                {
                    continue;
                }

                if (string.Equals(
                        candidate.strategy,
                        "Declared",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return HasPreserveTypeDeclarations;
                }

                return string.Equals(
                           candidate.strategy,
                           "Generated",
                           StringComparison.OrdinalIgnoreCase)
                       || string.Equals(
                           candidate.strategy,
                           "Framework",
                           StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static bool TryParseException(
            string assemblyName,
            string value,
            out DeucarianAotSafetyException exception)
        {
            exception = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string[] parts = value.Split(new[] { '|' }, 5);
            if (parts.Length != 5)
            {
                return false;
            }

            exception = new DeucarianAotSafetyException
            {
                assemblyName = assemblyName,
                declaringType = parts[0],
                method = parts[1],
                calledApi = parts[2],
                strategy = parts[3],
                reason = parts[4]
            };
            return true;
        }

        private static bool Matches(string expected, string actual)
        {
            return string.IsNullOrWhiteSpace(expected)
                   || string.Equals(
                       expected.Trim(),
                       actual ?? string.Empty,
                       StringComparison.Ordinal);
        }
    }
}
