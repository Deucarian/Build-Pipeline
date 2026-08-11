using System;
using System.IO;
using System.Reflection;
using Mono.Cecil;
using NUnit.Framework;

namespace Deucarian.BuildPipeline.Tests
{
    public sealed class DeucarianAotLinkXmlWriterTests
    {
        [Test]
        public void Generate_WritesExactValidatedProjectDeclaration()
        {
            using (CecilAssemblyFixture fixture =
                   new CecilAssemblyFixture(
                       "Game.Runtime",
                       referencesNewtonsoft: false))
            {
                fixture.AddType("Game", "DynamicFactory");
                string assemblyPath = fixture.Write();
                string outputPath = Path.Combine(
                    fixture.DirectoryPath,
                    "aot.link.xml");
                DeucarianAotSafetySettings settings =
                    new DeucarianAotSafetySettings();
                settings.preserveTypes.Add(new DeucarianAotPreserveType
                {
                    assemblyName = "Game.Runtime",
                    typeName = "Game.DynamicFactory",
                    reason = "Created by an approved compatibility boundary."
                });
                DeucarianAotSafetyReport report =
                    new DeucarianAotSafetyReport();

                string result = DeucarianAotLinkXmlWriter.Generate(
                    new[] { assemblyPath },
                    new[] { fixture.DirectoryPath },
                    settings,
                    outputPath,
                    report);

                Assert.That(result, Is.EqualTo(outputPath));
                Assert.That(report.passed, Is.True);
                Assert.That(report.preservedTypeCount, Is.EqualTo(1));
                Assert.That(
                    report.preservedTypes,
                    Is.EquivalentTo(new[]
                    {
                        "Game.Runtime::Game.DynamicFactory"
                    }));
                string xml = File.ReadAllText(outputPath);
                Assert.That(xml, Does.Contain("fullname=\"Game.Runtime\""));
                Assert.That(xml, Does.Contain("fullname=\"Game.DynamicFactory\""));
                Assert.That(xml, Does.Contain("preserve=\"all\""));
            }
        }

        [Test]
        public void Generate_ReadsPackageOwnedAssemblyMetadataDeclaration()
        {
            using (CecilAssemblyFixture fixture =
                   new CecilAssemblyFixture(
                       "Game.Runtime",
                       referencesNewtonsoft: false))
            {
                fixture.AddType("Game", "GeneratedFactory");
                AddAssemblyMetadata(
                    fixture.Module,
                    DeucarianAotLinkXmlWriter.PreserveTypeMetadataKey,
                    "Game.Runtime|Game.GeneratedFactory|Generated plugin registry boundary.");
                string assemblyPath = fixture.Write();
                string outputPath = Path.Combine(
                    fixture.DirectoryPath,
                    "metadata.link.xml");
                DeucarianAotSafetyReport report =
                    new DeucarianAotSafetyReport();

                DeucarianAotLinkXmlWriter.Generate(
                    new[] { assemblyPath },
                    new[] { fixture.DirectoryPath },
                    new DeucarianAotSafetySettings(),
                    outputPath,
                    report);

                Assert.That(report.passed, Is.True);
                Assert.That(
                    report.preservedTypes,
                    Is.EquivalentTo(new[]
                    {
                        "Game.Runtime::Game.GeneratedFactory"
                    }));
                Assert.That(
                    File.ReadAllText(outputPath),
                    Does.Contain("Game.GeneratedFactory"));
            }
        }

        [Test]
        public void Generate_FailsClosedForStaleTypeDeclaration()
        {
            using (CecilAssemblyFixture fixture =
                   new CecilAssemblyFixture(
                       "Game.Runtime",
                       referencesNewtonsoft: false))
            {
                string assemblyPath = fixture.Write();
                string outputPath = Path.Combine(
                    fixture.DirectoryPath,
                    "invalid.link.xml");
                DeucarianAotSafetySettings settings =
                    new DeucarianAotSafetySettings();
                settings.preserveTypes.Add(new DeucarianAotPreserveType
                {
                    assemblyName = "Game.Runtime",
                    typeName = "Game.RemovedType",
                    reason = "Stale declaration under test."
                });
                DeucarianAotSafetyReport report =
                    new DeucarianAotSafetyReport();

                DeucarianAotLinkXmlWriter.Generate(
                    new[] { assemblyPath },
                    new[] { fixture.DirectoryPath },
                    settings,
                    outputPath,
                    report);

                Assert.That(report.passed, Is.False);
                Assert.That(report.preservedTypeCount, Is.Zero);
                Assert.That(
                    report.findings[0].message,
                    Does.Contain("Game.RemovedType"));
            }
        }

        private static void AddAssemblyMetadata(
            ModuleDefinition module,
            string key,
            string value)
        {
            ConstructorInfo constructor =
                typeof(AssemblyMetadataAttribute).GetConstructor(
                    new[] { typeof(string), typeof(string) });
            CustomAttribute attribute = new CustomAttribute(
                module.ImportReference(constructor));
            attribute.ConstructorArguments.Add(
                new CustomAttributeArgument(
                    module.TypeSystem.String,
                    key));
            attribute.ConstructorArguments.Add(
                new CustomAttributeArgument(
                    module.TypeSystem.String,
                    value));
            module.Assembly.CustomAttributes.Add(attribute);
        }
    }
}
