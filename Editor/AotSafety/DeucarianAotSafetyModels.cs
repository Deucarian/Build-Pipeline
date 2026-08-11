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
        public List<string> generatedFeatures = new List<string>();
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
            if (string.IsNullOrWhiteSpace(feature)
                || generatedFeatures.Contains(feature))
            {
                return;
            }

            generatedFeatures.Add(feature);
            generatedFeatures.Sort(StringComparer.Ordinal);
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

            for (int i = 0; i < other.generatedFeatures.Count; i++)
            {
                AddFeature(other.generatedFeatures[i]);
            }

            for (int i = 0; i < other.manualLinkXmlFiles.Count; i++)
            {
                string path = other.manualLinkXmlFiles[i];
                if (!manualLinkXmlFiles.Contains(path))
                {
                    manualLinkXmlFiles.Add(path);
                }
            }

            manualLinkXmlFiles.Sort(StringComparer.Ordinal);
            for (int i = 0; i < other.findings.Count; i++)
            {
                AddFinding(other.findings[i]);
            }
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
    }
}
