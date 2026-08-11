using System;
using System.Collections.Generic;
using System.Text;

namespace Deucarian.BuildPipeline
{
    public enum DeucarianAotSafetyMode
    {
        Inherit,
        Audit,
        Enforce
    }

    [Serializable]
    public sealed class DeucarianAotSafetyFinding
    {
        public string category;
        public string assemblyName;
        public string declaringType;
        public string method;
        public string calledApi;
        public string message;
    }

    [Serializable]
    public sealed class DeucarianAotSafetyReport
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public string mode = DeucarianAotSafetyMode.Audit.ToString();
        public bool linkerInspectionCompleted;
        public bool passed = true;
        public int scannedAssemblyCount;
        public int declaredExceptionCount;
        public int preservedTypeCount;
        public string generatedLinkerDescriptor;
        public List<string> generatedFeatures = new List<string>();
        public List<string> preservedTypes = new List<string>();
        public List<string> manualLinkXmlFiles = new List<string>();
        public List<DeucarianAotSafetyFinding> findings =
            new List<DeucarianAotSafetyFinding>();

        internal void AddFinding(DeucarianAotSafetyFinding finding)
        {
            if (finding == null)
            {
                return;
            }

            findings.Add(finding);
            passed = false;
        }

        internal void AddFeature(string feature)
        {
            AddSortedUnique(generatedFeatures, feature);
        }

        internal void AddPreservedType(
            string assemblyName,
            string typeName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName)
                || string.IsNullOrWhiteSpace(typeName))
            {
                return;
            }

            AddSortedUnique(
                preservedTypes,
                assemblyName.Trim() + "::" + typeName.Trim());
            preservedTypeCount = preservedTypes.Count;
        }

        internal void Merge(DeucarianAotSafetyReport other)
        {
            if (other == null)
            {
                return;
            }

            linkerInspectionCompleted |= other.linkerInspectionCompleted;
            scannedAssemblyCount += other.scannedAssemblyCount;
            declaredExceptionCount += other.declaredExceptionCount;
            if (!string.IsNullOrWhiteSpace(other.generatedLinkerDescriptor))
            {
                generatedLinkerDescriptor =
                    other.generatedLinkerDescriptor;
            }

            if (other.generatedFeatures != null)
            {
                for (int i = 0; i < other.generatedFeatures.Count; i++)
                {
                    AddFeature(other.generatedFeatures[i]);
                }
            }

            if (other.preservedTypes != null)
            {
                for (int i = 0; i < other.preservedTypes.Count; i++)
                {
                    AddSortedUnique(
                        preservedTypes,
                        other.preservedTypes[i]);
                }
            }

            preservedTypeCount = preservedTypes.Count;
            if (other.manualLinkXmlFiles != null)
            {
                for (int i = 0; i < other.manualLinkXmlFiles.Count; i++)
                {
                    AddSortedUnique(
                        manualLinkXmlFiles,
                        other.manualLinkXmlFiles[i]);
                }
            }

            if (other.findings != null)
            {
                for (int i = 0; i < other.findings.Count; i++)
                {
                    AddFinding(other.findings[i]);
                }
            }

            passed &= other.passed;
        }

        public string FormatFailure(string heading)
        {
            if (passed)
            {
                return heading + ": passed.";
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(heading);
            builder.Append(':');
            for (int i = 0; i < findings.Count; i++)
            {
                DeucarianAotSafetyFinding finding = findings[i];
                builder.Append("\n- ");
                builder.Append(finding.message);
                if (!string.IsNullOrWhiteSpace(finding.assemblyName))
                {
                    builder.Append(" [");
                    builder.Append(finding.assemblyName);
                    if (!string.IsNullOrWhiteSpace(finding.declaringType))
                    {
                        builder.Append(" :: ");
                        builder.Append(finding.declaringType);
                    }

                    if (!string.IsNullOrWhiteSpace(finding.method))
                    {
                        builder.Append('.');
                        builder.Append(finding.method);
                    }

                    builder.Append(']');
                }
            }

            return builder.ToString();
        }

        private static void AddSortedUnique(
            List<string> values,
            string value)
        {
            if (values == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string normalized = value.Trim();
            if (!values.Contains(normalized))
            {
                values.Add(normalized);
                values.Sort(StringComparer.Ordinal);
            }
        }
    }
}
