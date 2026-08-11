using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Mono.Cecil;

namespace Deucarian.BuildPipeline
{
    internal static class DeucarianAotLinkXmlWriter
    {
        internal const string PreserveTypeMetadataKey =
            "Deucarian.AOT.PreserveType";

        private const string AssemblyMetadataAttributeName =
            "System.Reflection.AssemblyMetadataAttribute";

        private static readonly StringComparer PathComparer =
            Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        internal static string Generate(
            IEnumerable<string> assemblyPaths,
            IEnumerable<string> resolverDirectories,
            DeucarianAotSafetySettings settings,
            string outputPath,
            DeucarianAotSafetyReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                report.AddFinding(CreateFinding(
                    "AOT linker generation received no output path."));
                return null;
            }

            string[] normalizedPaths = NormalizeAssemblyPaths(
                assemblyPaths,
                report);
            string[] normalizedResolverDirectories =
                NormalizeResolverDirectories(
                    normalizedPaths,
                    resolverDirectories);
            List<AssemblyDefinition> assemblies =
                new List<AssemblyDefinition>();
            Dictionary<string, AssemblyDefinition> byName =
                new Dictionary<string, AssemblyDefinition>(
                    StringComparer.Ordinal);

            using (DefaultAssemblyResolver resolver =
                   new DefaultAssemblyResolver())
            {
                resolver.RemoveSearchDirectory(".");
                resolver.RemoveSearchDirectory("bin");
                for (int i = 0;
                     i < normalizedResolverDirectories.Length;
                     i++)
                {
                    resolver.AddSearchDirectory(
                        normalizedResolverDirectories[i]);
                }

                try
                {
                    ReadAssemblies(
                        normalizedPaths,
                        resolver,
                        assemblies,
                        byName,
                        report);
                    List<PreserveDeclaration> declarations =
                        CollectDeclarations(
                            settings,
                            assemblies,
                            report);
                    List<PreserveDeclaration> validDeclarations =
                        ValidateDeclarations(
                            declarations,
                            byName,
                            report);
                    WriteDescriptor(validDeclarations, outputPath);
                    report.generatedLinkerDescriptor =
                        Path.GetFullPath(outputPath).Replace('\\', '/');
                    return outputPath;
                }
                catch (Exception exception)
                {
                    report.AddFinding(CreateFinding(
                        "AOT linker generation failed: "
                        + exception.GetBaseException().Message));
                    return null;
                }
                finally
                {
                    for (int i = 0; i < assemblies.Count; i++)
                    {
                        assemblies[i].Dispose();
                    }
                }
            }
        }

        private static List<PreserveDeclaration> CollectDeclarations(
            DeucarianAotSafetySettings settings,
            IEnumerable<AssemblyDefinition> assemblies,
            DeucarianAotSafetyReport report)
        {
            List<PreserveDeclaration> declarations =
                new List<PreserveDeclaration>();
            if (settings != null)
            {
                foreach (DeucarianAotPreserveType preserveType
                         in settings.EnumeratePreserveTypes())
                {
                    declarations.Add(new PreserveDeclaration
                    {
                        AssemblyName = preserveType.assemblyName,
                        TypeName = preserveType.typeName,
                        Reason = preserveType.reason,
                        Source = DeucarianAotSafetySettings.ProjectSettingsPath
                    });
                }
            }

            foreach (AssemblyDefinition assembly in assemblies)
            {
                for (int attributeIndex = 0;
                     attributeIndex < assembly.CustomAttributes.Count;
                     attributeIndex++)
                {
                    CustomAttribute attribute =
                        assembly.CustomAttributes[attributeIndex];
                    if (attribute.AttributeType.FullName !=
                        AssemblyMetadataAttributeName
                        || attribute.ConstructorArguments.Count != 2)
                    {
                        continue;
                    }

                    string key =
                        attribute.ConstructorArguments[0].Value as string;
                    if (!string.Equals(
                            key,
                            PreserveTypeMetadataKey,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string value =
                        attribute.ConstructorArguments[1].Value as string;
                    PreserveDeclaration declaration;
                    if (!TryParseMetadata(
                            value,
                            assembly.Name.Name,
                            out declaration))
                    {
                        report.AddFinding(CreateFinding(
                            "Assembly '" + assembly.Name.Name
                            + "' contains invalid "
                            + PreserveTypeMetadataKey
                            + " metadata. Expected assembly|type|reason."));
                        continue;
                    }

                    declarations.Add(declaration);
                }
            }

            return declarations;
        }

        private static List<PreserveDeclaration> ValidateDeclarations(
            IEnumerable<PreserveDeclaration> declarations,
            IDictionary<string, AssemblyDefinition> assemblies,
            DeucarianAotSafetyReport report)
        {
            Dictionary<string, PreserveDeclaration> valid =
                new Dictionary<string, PreserveDeclaration>(
                    StringComparer.Ordinal);
            foreach (PreserveDeclaration declaration in declarations)
            {
                if (declaration == null
                    || string.IsNullOrWhiteSpace(declaration.AssemblyName)
                    || string.IsNullOrWhiteSpace(declaration.TypeName)
                    || string.IsNullOrWhiteSpace(declaration.Reason))
                {
                    report.AddFinding(CreateFinding(
                        "Every AOT preserve declaration requires an exact assembly, type, and reason."));
                    continue;
                }

                string assemblyName = declaration.AssemblyName.Trim();
                string typeName = declaration.TypeName.Trim();
                AssemblyDefinition assembly;
                if (!assemblies.TryGetValue(
                        assemblyName,
                        out assembly))
                {
                    report.AddFinding(CreateFinding(
                        "AOT preserve declaration from '"
                        + declaration.Source
                        + "' references missing player assembly '"
                        + assemblyName + "'."));
                    continue;
                }

                if (!ContainsType(assembly, typeName))
                {
                    report.AddFinding(CreateFinding(
                        "AOT preserve declaration from '"
                        + declaration.Source
                        + "' references missing type '"
                        + assemblyName + "::" + typeName + "'."));
                    continue;
                }

                declaration.AssemblyName = assemblyName;
                declaration.TypeName = typeName;
                string key = assemblyName + "\n" + typeName;
                if (!valid.ContainsKey(key))
                {
                    valid.Add(key, declaration);
                    report.AddPreservedType(assemblyName, typeName);
                }
            }

            return valid.Values
                .OrderBy(item => item.AssemblyName, StringComparer.Ordinal)
                .ThenBy(item => item.TypeName, StringComparer.Ordinal)
                .ToList();
        }

        private static void WriteDescriptor(
            IEnumerable<PreserveDeclaration> declarations,
            string outputPath)
        {
            string fullOutputPath = Path.GetFullPath(outputPath);
            string directory = Path.GetDirectoryName(fullOutputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            XmlWriterSettings writerSettings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace
            };
            using (XmlWriter writer = XmlWriter.Create(
                       fullOutputPath,
                       writerSettings))
            {
                writer.WriteStartElement("linker");
                foreach (IGrouping<string, PreserveDeclaration> assembly
                         in declarations.GroupBy(
                             item => item.AssemblyName,
                             StringComparer.Ordinal))
                {
                    writer.WriteStartElement("assembly");
                    writer.WriteAttributeString("fullname", assembly.Key);
                    foreach (PreserveDeclaration declaration in assembly)
                    {
                        writer.WriteStartElement("type");
                        writer.WriteAttributeString(
                            "fullname",
                            declaration.TypeName);
                        writer.WriteAttributeString("preserve", "all");
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }
        }

        private static void ReadAssemblies(
            IEnumerable<string> paths,
            IAssemblyResolver resolver,
            ICollection<AssemblyDefinition> assemblies,
            IDictionary<string, AssemblyDefinition> byName,
            DeucarianAotSafetyReport report)
        {
            foreach (string path in paths)
            {
                if (!File.Exists(path))
                {
                    report.AddFinding(CreateFinding(
                        "AOT linker generation could not find player assembly '"
                        + path + "'."));
                    continue;
                }

                AssemblyDefinition assembly =
                    AssemblyDefinition.ReadAssembly(
                        path,
                        new ReaderParameters
                        {
                            AssemblyResolver = resolver,
                            InMemory = true,
                            ReadingMode = ReadingMode.Immediate,
                            ReadSymbols = false
                        });
                assemblies.Add(assembly);
                if (!byName.ContainsKey(assembly.Name.Name))
                {
                    byName.Add(assembly.Name.Name, assembly);
                }
            }
        }

        private static bool ContainsType(
            AssemblyDefinition assembly,
            string typeName)
        {
            return EnumerateTypes(assembly.MainModule.Types)
                .Any(type => string.Equals(
                    type.FullName,
                    typeName,
                    StringComparison.Ordinal));
        }

        private static IEnumerable<TypeDefinition> EnumerateTypes(
            IEnumerable<TypeDefinition> types)
        {
            foreach (TypeDefinition type in types)
            {
                yield return type;
                foreach (TypeDefinition nested in EnumerateTypes(
                             type.NestedTypes))
                {
                    yield return nested;
                }
            }
        }

        private static bool TryParseMetadata(
            string value,
            string sourceAssembly,
            out PreserveDeclaration declaration)
        {
            declaration = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string[] parts = value.Split(new[] { '|' }, 3);
            if (parts.Length != 3)
            {
                return false;
            }

            declaration = new PreserveDeclaration
            {
                AssemblyName = parts[0],
                TypeName = parts[1],
                Reason = parts[2],
                Source = sourceAssembly + " assembly metadata"
            };
            return true;
        }

        private static string[] NormalizeAssemblyPaths(
            IEnumerable<string> paths,
            DeucarianAotSafetyReport report)
        {
            if (paths == null)
            {
                report.AddFinding(CreateFinding(
                    "AOT linker generation received no player assemblies."));
                return Array.Empty<string>();
            }

            try
            {
                return paths
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(Path.GetFullPath)
                    .Distinct(PathComparer)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception exception)
            {
                report.AddFinding(CreateFinding(
                    "AOT linker generation received an invalid assembly path: "
                    + exception.GetBaseException().Message));
                return Array.Empty<string>();
            }
        }

        private static string[] NormalizeResolverDirectories(
            IEnumerable<string> assemblyPaths,
            IEnumerable<string> requestedDirectories)
        {
            IEnumerable<string> requested = requestedDirectories
                ?? Enumerable.Empty<string>();
            return requested
                .Concat(assemblyPaths.Select(Path.GetDirectoryName))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Where(Directory.Exists)
                .Distinct(PathComparer)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        private static DeucarianAotSafetyFinding CreateFinding(
            string message)
        {
            return new DeucarianAotSafetyFinding
            {
                category = "InvalidPreserveDeclaration",
                message = message
            };
        }

        private sealed class PreserveDeclaration
        {
            public string AssemblyName;
            public string TypeName;
            public string Reason;
            public string Source;
        }
    }
}
