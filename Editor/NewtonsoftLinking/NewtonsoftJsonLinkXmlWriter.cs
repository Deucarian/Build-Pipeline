using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Deucarian.BuildPipeline
{
    internal static class NewtonsoftJsonLinkXmlWriter
    {
        public static string Write(
            NewtonsoftJsonContractCatalog catalog,
            string outputPath)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("An output path is required.", nameof(outputPath));
            }

            string fullOutputPath = Path.GetFullPath(outputPath);
            string directory = Path.GetDirectoryName(fullOutputPath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException(
                    "The Newtonsoft linker output path has no parent directory.");
            }

            string contents = Serialize(catalog);
            Directory.CreateDirectory(directory);
            if (!File.Exists(fullOutputPath)
                || !string.Equals(
                    File.ReadAllText(fullOutputPath),
                    contents,
                    StringComparison.Ordinal))
            {
                File.WriteAllText(
                    fullOutputPath,
                    contents,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            return fullOutputPath;
        }

        internal static string Serialize(NewtonsoftJsonContractCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            XElement linker = new XElement("linker");
            foreach (var assemblyEntry in catalog.Entries)
            {
                XElement assembly = new XElement(
                    "assembly",
                    new XAttribute("fullname", assemblyEntry.Key));
                foreach (string typeName in assemblyEntry.Value)
                {
                    assembly.Add(
                        new XElement(
                            "type",
                            new XAttribute("fullname", typeName),
                            new XAttribute("preserve", "all")));
                }

                linker.Add(assembly);
            }

            XDocument document = new XDocument(linker);
            StringBuilder builder = new StringBuilder();
            using (XmlWriter writer = XmlWriter.Create(
                       builder,
                       new XmlWriterSettings
                       {
                           Indent = true,
                           IndentChars = "  ",
                           NewLineChars = "\n",
                           NewLineHandling = NewLineHandling.Replace,
                           OmitXmlDeclaration = true
                       }))
            {
                document.Save(writer);
            }

            builder.Append('\n');
            return builder.ToString();
        }
    }
}
