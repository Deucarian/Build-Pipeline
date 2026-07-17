using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.Build.Profile;

namespace Deucarian.BuildPipeline
{
    /// <summary>
    /// Reads a Build Profile's serialized Player Settings override without making
    /// that profile active. Passive validation must never call
    /// BuildProfile.SetActiveBuildProfile because Unity can reimport assets and
    /// schedule script compilation when the active profile changes.
    /// </summary>
    internal sealed class DeucarianBuildProfileSettingsSnapshot
    {
        private const string SerializedSettingsPath =
            "m_PlayerSettingsYaml.m_Settings";

        private readonly Dictionary<string, string> scalarValues =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, string>> sectionValues =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        private DeucarianBuildProfileSettingsSnapshot()
        {
        }

        internal static bool TryCreate(
            BuildProfile profile,
            out DeucarianBuildProfileSettingsSnapshot snapshot,
            out string issue)
        {
            snapshot = null;
            issue = string.Empty;
            if (profile == null)
            {
                issue = "A Build Profile is required.";
                return false;
            }

            SerializedObject serializedProfile = new SerializedObject(profile);
            serializedProfile.UpdateIfRequiredOrScript();
            SerializedProperty settings = serializedProfile.FindProperty(
                SerializedSettingsPath);
            if (settings == null || !settings.isArray || settings.arraySize == 0)
            {
                issue = "Build Profile '" + AssetDatabase.GetAssetPath(profile)
                        + "' has no serialized Player Settings override. Apply the policy explicitly.";
                return false;
            }

            DeucarianBuildProfileSettingsSnapshot candidate =
                new DeucarianBuildProfileSettingsSnapshot();
            string currentSection = null;
            int currentSectionIndent = -1;
            for (int i = 0; i < settings.arraySize; i++)
            {
                SerializedProperty item = settings.GetArrayElementAtIndex(i);
                SerializedProperty lineProperty = item.FindPropertyRelative("line");
                if (lineProperty == null)
                {
                    continue;
                }

                string line = NormalizeLine(lineProperty.stringValue);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                int indent = CountLeadingSpaces(line);
                string content = line.Trim();
                int separator = content.IndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }

                string key = content.Substring(0, separator).Trim();
                string value = content.Substring(separator + 1).Trim();
                if (indent <= 2)
                {
                    if (value.Length == 0)
                    {
                        currentSection = key;
                        currentSectionIndent = indent;
                    }
                    else
                    {
                        candidate.scalarValues[key] = value;
                        currentSection = null;
                        currentSectionIndent = -1;
                    }

                    continue;
                }

                if (currentSection == null || indent <= currentSectionIndent)
                {
                    continue;
                }

                if (!candidate.sectionValues.TryGetValue(
                        currentSection,
                        out Dictionary<string, string> values))
                {
                    values = new Dictionary<string, string>(StringComparer.Ordinal);
                    candidate.sectionValues[currentSection] = values;
                }

                values[key] = value;
            }

            snapshot = candidate;
            return true;
        }

        internal bool TryGetInt(string key, out int value)
        {
            value = default;
            return scalarValues.TryGetValue(key, out string serialized)
                   && int.TryParse(
                       serialized,
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out value);
        }

        internal bool TryGetBool(string key, out bool value)
        {
            value = default;
            if (!scalarValues.TryGetValue(key, out string serialized))
            {
                return false;
            }

            if (int.TryParse(
                    serialized,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int numeric))
            {
                value = numeric != 0;
                return true;
            }

            return bool.TryParse(serialized, out value);
        }

        internal bool TryGetSectionInt(string section, string key, out int value)
        {
            value = default;
            return sectionValues.TryGetValue(
                       section,
                       out Dictionary<string, string> values)
                   && values.TryGetValue(key, out string serialized)
                   && int.TryParse(
                       serialized,
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out value);
        }

        private static string NormalizeLine(string serializedLine)
        {
            if (string.IsNullOrEmpty(serializedLine))
            {
                return string.Empty;
            }

            string line = serializedLine;
            if (line[0] == '|')
            {
                line = line.Substring(1);
                if (line.Length > 0 && line[0] == ' ')
                {
                    line = line.Substring(1);
                }
            }

            return line;
        }

        private static int CountLeadingSpaces(string value)
        {
            int count = 0;
            while (count < value.Length && value[count] == ' ')
            {
                count++;
            }

            return count;
        }
    }
}
