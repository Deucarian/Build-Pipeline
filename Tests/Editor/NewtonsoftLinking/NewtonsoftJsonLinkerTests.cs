using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;

namespace Deucarian.BuildPipeline.Tests
{
    public sealed class NewtonsoftJsonLinkerTests
    {
        [Test]
        public void DiscoveryFindsMarkedContractsAndAttributeReferencedTypes()
        {
            using (CecilAssemblyFixture fixture =
                   new CecilAssemblyFixture("Synthetic.Contracts"))
            {
                TypeDefinition contract = fixture.AddType(
                    "Examples",
                    "Contract`1");
                contract.GenericParameters.Add(new GenericParameter("T", contract));

                PropertyDefinition property = new PropertyDefinition(
                    "Value",
                    PropertyAttributes.None,
                    fixture.Module.TypeSystem.String);
                fixture.AddJsonAttribute(property, "JsonPropertyAttribute");
                contract.Properties.Add(property);

                MethodDefinition constructor = fixture.AddConstructor(contract);
                fixture.AddJsonAttribute(constructor, "JsonConstructorAttribute");

                TypeDefinition converter = fixture.AddType(
                    "Examples",
                    "ReferencedConverter");
                fixture.AddJsonTypeAttribute(
                    contract,
                    "JsonConverterAttribute",
                    converter);

                TypeDefinition outer = fixture.AddType("Examples", "Outer");
                TypeDefinition nested = fixture.AddNestedType(outer, "NestedContract");
                fixture.AddDataContractAttribute(nested);

                fixture.AddType("Examples", "Unrelated");
                fixture.Write();

                NewtonsoftJsonContractCatalog catalog =
                    NewtonsoftJsonContractDiscovery.Discover(fixture.DirectoryPath);

                Assert.That(catalog.Count, Is.EqualTo(3));
                Assert.That(
                    catalog.Contains("Synthetic.Contracts", "Examples.Contract`1"),
                    Is.True);
                Assert.That(
                    catalog.Contains("Synthetic.Contracts", "Examples.ReferencedConverter"),
                    Is.True);
                Assert.That(
                    catalog.Contains(
                        "Synthetic.Contracts",
                        "Examples.Outer/NestedContract"),
                    Is.True);
                Assert.That(
                    catalog.Contains("Synthetic.Contracts", "Examples.Unrelated"),
                    Is.False);
            }
        }

        [Test]
        public void DiscoveryDoesNotRootAnUnreferencedConverterSubclass()
        {
            using (CecilAssemblyFixture fixture =
                   new CecilAssemblyFixture("Synthetic.Converters"))
            {
                fixture.AddType(
                    "Examples",
                    "UnusedConverter",
                    fixture.NewtonsoftType("JsonConverter"));
                fixture.Write();

                NewtonsoftJsonContractCatalog catalog =
                    NewtonsoftJsonContractDiscovery.Discover(fixture.DirectoryPath);

                Assert.That(catalog.Count, Is.Zero);
            }
        }

        [Test]
        public void DiscoveryIgnoresDataContractsWhenPlayerCodeDoesNotUseNewtonsoft()
        {
            using (CecilAssemblyFixture fixture = new CecilAssemblyFixture(
                       "Synthetic.DataContracts",
                       referencesNewtonsoft: false))
            {
                TypeDefinition contract = fixture.AddType(
                    "Examples",
                    "DataOnlyContract");
                fixture.AddDataContractAttribute(contract);
                fixture.Write();

                NewtonsoftJsonContractCatalog catalog =
                    NewtonsoftJsonContractDiscovery.Discover(fixture.DirectoryPath);

                Assert.That(catalog.Count, Is.Zero);
            }
        }

        [Test]
        public void DiscoveryIncludesDataContractsWhenPlayerCodeUsesNewtonsoft()
        {
            using (CecilAssemblyFixture dataContracts = new CecilAssemblyFixture(
                       "Synthetic.DataContracts",
                       referencesNewtonsoft: false))
            using (CecilAssemblyFixture jsonConsumer = new CecilAssemblyFixture(
                       "Synthetic.JsonConsumer",
                       dataContracts.DirectoryPath))
            {
                TypeDefinition contract = dataContracts.AddType(
                    "Examples",
                    "DataOnlyContract");
                dataContracts.AddDataContractAttribute(contract);
                dataContracts.Write();
                jsonConsumer.AddType("Examples", "JsonConsumer");
                jsonConsumer.Write();

                NewtonsoftJsonContractCatalog catalog =
                    NewtonsoftJsonContractDiscovery.Discover(
                        dataContracts.DirectoryPath);

                Assert.That(
                    catalog.Contains(
                        "Synthetic.DataContracts",
                        "Examples.DataOnlyContract"),
                    Is.True);
            }
        }

        [Test]
        public void DiscoveryIncludesTypesWithInheritedContractMetadata()
        {
            using (CecilAssemblyFixture fixture =
                   new CecilAssemblyFixture("Synthetic.Inheritance"))
            {
                TypeDefinition baseContract = fixture.AddType(
                    "Examples",
                    "BaseContract");
                PropertyDefinition baseProperty = new PropertyDefinition(
                    "Value",
                    PropertyAttributes.None,
                    fixture.Module.TypeSystem.String);
                fixture.AddJsonAttribute(baseProperty, "JsonPropertyAttribute");
                baseContract.Properties.Add(baseProperty);
                TypeDefinition derivedContract = fixture.AddType(
                    "Examples",
                    "DerivedContract",
                    baseContract);
                fixture.Write();

                NewtonsoftJsonContractCatalog catalog =
                    NewtonsoftJsonContractDiscovery.Discover(fixture.DirectoryPath);

                Assert.That(
                    catalog.Contains(
                        "Synthetic.Inheritance",
                        baseContract.FullName),
                    Is.True);
                Assert.That(
                    catalog.Contains(
                        "Synthetic.Inheritance",
                        derivedContract.FullName),
                    Is.True);
            }
        }

        [Test]
        public void DiscoveryFindsTypeReferencesInsideBoxedAttributeArguments()
        {
            using (CecilAssemblyFixture fixture =
                   new CecilAssemblyFixture("Synthetic.BoxedArguments"))
            {
                TypeDefinition contract = fixture.AddType("Examples", "Contract");
                TypeDefinition reflectedType = fixture.AddType(
                    "Examples",
                    "ReflectedHelper");
                fixture.AddBoxedJsonTypeAttribute(
                    contract,
                    "JsonConverterAttribute",
                    reflectedType);
                fixture.Write();

                NewtonsoftJsonContractCatalog catalog =
                    NewtonsoftJsonContractDiscovery.Discover(fixture.DirectoryPath);

                Assert.That(
                    catalog.Contains(
                        "Synthetic.BoxedArguments",
                        reflectedType.FullName),
                    Is.True);
            }
        }

        [Test]
        public void DiscoveryResolvesEnumValuedNewtonsoftAttributeMetadata()
        {
            string resolverDirectory = CreateTempDirectory();
            try
            {
                WriteNewtonsoftJsonStub(resolverDirectory);
                using (CecilAssemblyFixture fixture =
                       new CecilAssemblyFixture("Synthetic.EnumMetadata"))
                {
                    fixture.AddWriterResolverDirectory(resolverDirectory);
                    TypeDefinition contract = fixture.AddType(
                        "Examples",
                        "EnumAttributedContract");
                    fixture.AddJsonEnumPropertyAttribute(
                        contract,
                        "JsonPropertyAttribute",
                        "NullValueHandling",
                        "NullValueHandling",
                        1);
                    string assemblyPath = fixture.Write();

                    Assert.Throws<BuildFailedException>(() =>
                        NewtonsoftJsonContractDiscovery.Discover(
                            new[] { assemblyPath },
                            Array.Empty<string>()));

                    NewtonsoftJsonContractCatalog catalog =
                        NewtonsoftJsonContractDiscovery.Discover(
                            new[] { assemblyPath },
                            new[] { resolverDirectory });

                    Assert.That(
                        catalog.Contains(
                            fixture.AssemblyName,
                            contract.FullName),
                        Is.True);
                }
            }
            finally
            {
                Directory.Delete(resolverDirectory, true);
            }
        }

        [Test]
        public void DiscoveryDoesNotScanContractsInResolverOnlyAssemblies()
        {
            string resolverDirectory = CreateTempDirectory();
            try
            {
                using (CecilAssemblyFixture playerAssembly =
                       new CecilAssemblyFixture("Synthetic.Player"))
                using (CecilAssemblyFixture resolverOnlyAssembly =
                       new CecilAssemblyFixture(
                           "Synthetic.ResolverOnly",
                           resolverDirectory))
                {
                    playerAssembly.AddType("Examples", "PlayerType");
                    string playerAssemblyPath = playerAssembly.Write();
                    TypeDefinition dependencyContract = resolverOnlyAssembly.AddType(
                        "Examples",
                        "DependencyContract");
                    resolverOnlyAssembly.AddJsonAttribute(
                        dependencyContract,
                        "JsonObjectAttribute");
                    resolverOnlyAssembly.Write();

                    NewtonsoftJsonContractCatalog catalog =
                        NewtonsoftJsonContractDiscovery.Discover(
                            new[] { playerAssemblyPath },
                            new[] { resolverDirectory });

                    Assert.That(catalog.Count, Is.Zero);
                    Assert.That(
                        catalog.Contains(
                            resolverOnlyAssembly.AssemblyName,
                            dependencyContract.FullName),
                        Is.False);
                }
            }
            finally
            {
                Directory.Delete(resolverDirectory, true);
            }
        }

        [Test]
        public void DiscoveryFailsClosedForMissingEmptyOrMalformedInput()
        {
            string emptyDirectory = CreateTempDirectory();
            string malformedDirectory = CreateTempDirectory();
            File.WriteAllText(Path.Combine(malformedDirectory, "Broken.dll"), "not managed code");
            try
            {
                Assert.Throws<BuildFailedException>(() =>
                    NewtonsoftJsonContractDiscovery.Discover(
                        Path.Combine(emptyDirectory, "missing")));
                Assert.Throws<BuildFailedException>(() =>
                    NewtonsoftJsonContractDiscovery.Discover(emptyDirectory));
                Assert.Throws<BuildFailedException>(() =>
                    NewtonsoftJsonContractDiscovery.Discover(malformedDirectory));
            }
            finally
            {
                Directory.Delete(emptyDirectory, true);
                Directory.Delete(malformedDirectory, true);
            }
        }

        [Test]
        public void DiscoveryRejectsDuplicateAssemblyNames()
        {
            string directory = CreateTempDirectory();
            try
            {
                WriteEmptyAssembly(directory, "First.dll", "Duplicate.Name");
                WriteEmptyAssembly(directory, "Second.dll", "Duplicate.Name");

                Assert.Throws<BuildFailedException>(() =>
                    NewtonsoftJsonContractDiscovery.Discover(directory));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void WriterProducesDeterministicSortedEscapedXml()
        {
            NewtonsoftJsonContractCatalog catalog = new NewtonsoftJsonContractCatalog();
            catalog.Add("Z&Assembly", "Examples.Zed");
            catalog.Add("A.Assembly", "Examples.Type<Generic>");
            catalog.Add("A.Assembly", "Examples.Alpha");
            catalog.Add("A.Assembly", "Examples.Alpha");

            string first = NewtonsoftJsonLinkXmlWriter.Serialize(catalog);
            string second = NewtonsoftJsonLinkXmlWriter.Serialize(catalog);
            XDocument document = XDocument.Parse(first);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(catalog.Count, Is.EqualTo(3));
            Assert.That(first, Does.Contain("Z&amp;Assembly"));
            Assert.That(first, Does.Contain("Examples.Type&lt;Generic&gt;"));
            Assert.That(
                document.Root.Elements("assembly")
                    .Select(element => (string)element.Attribute("fullname")),
                Is.EqualTo(new[] { "A.Assembly", "Z&Assembly" }));
            Assert.That(
                document.Root.Elements("assembly").First()
                    .Elements("type")
                    .Select(element => (string)element.Attribute("fullname")),
                Is.EqualTo(new[] { "Examples.Alpha", "Examples.Type<Generic>" }));
            Assert.That(
                document.Descendants("type")
                    .All(element => (string)element.Attribute("preserve") == "all"),
                Is.True);
        }

        [Test]
        public void WriterProducesAValidEmptyLinkerDocument()
        {
            string xml = NewtonsoftJsonLinkXmlWriter.Serialize(
                new NewtonsoftJsonContractCatalog());

            XDocument document = XDocument.Parse(xml);

            Assert.That(document.Root, Is.Not.Null);
            Assert.That(document.Root.Name.LocalName, Is.EqualTo("linker"));
            Assert.That(document.Root.HasElements, Is.False);
        }

        [Test]
        public void ProcessorGenerationWritesTheDiscoveredCatalog()
        {
            using (CecilAssemblyFixture fixture =
                   new CecilAssemblyFixture("Synthetic.Generation"))
            {
                TypeDefinition contract = fixture.AddType("Examples", "Payload");
                fixture.AddJsonAttribute(contract, "JsonObjectAttribute");
                string assemblyPath = fixture.Write();
                string outputPath = Path.Combine(
                    fixture.DirectoryPath,
                    "Generated",
                    "WebGL.link.xml");

                string generatedPath = DeucarianNewtonsoftLinkerProcessor.Generate(
                    new[] { assemblyPath },
                    new[] { fixture.DirectoryPath },
                    outputPath);

                Assert.That(generatedPath, Is.EqualTo(Path.GetFullPath(outputPath)));
                Assert.That(File.Exists(generatedPath), Is.True);
                XDocument document = XDocument.Load(generatedPath);
                Assert.That(
                    document.Descendants("type")
                        .Single()
                        .Attribute("fullname")?.Value,
                    Is.EqualTo("Examples.Payload"));
            }
        }

        [Test]
        public void ProcessorGenerationWritesAnEmptyDescriptorWithoutPlayerAssemblies()
        {
            string directory = CreateTempDirectory();
            try
            {
                string outputPath = Path.Combine(directory, "Empty.link.xml");

                string generatedPath = DeucarianNewtonsoftLinkerProcessor.Generate(
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    outputPath);

                XDocument document = XDocument.Load(generatedPath);
                Assert.That(document.Root, Is.Not.Null);
                Assert.That(document.Root.Name.LocalName, Is.EqualTo("linker"));
                Assert.That(document.Root.HasElements, Is.False);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void UnityDiscoversTheLinkerProcessor()
        {
            Assert.That(
                TypeCache.GetTypesDerivedFrom<IUnityLinkerProcessor>(),
                Has.Member(typeof(DeucarianNewtonsoftLinkerProcessor)));
        }

        private static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void WriteEmptyAssembly(
            string directory,
            string fileName,
            string assemblyName)
        {
            using (AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
                       new AssemblyNameDefinition(
                           assemblyName,
                           new Version(1, 0, 0, 0)),
                       assemblyName,
                       ModuleKind.Dll))
            {
                assembly.Write(Path.Combine(directory, fileName));
            }
        }

        private static void WriteNewtonsoftJsonStub(string directory)
        {
            using (AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
                       new AssemblyNameDefinition(
                           "Newtonsoft.Json",
                           new Version(13, 0, 0, 0)),
                       "Newtonsoft.Json",
                       ModuleKind.Dll))
            {
                ModuleDefinition module = assembly.MainModule;
                TypeDefinition enumType = new TypeDefinition(
                    "Newtonsoft.Json",
                    "NullValueHandling",
                    TypeAttributes.Public
                    | TypeAttributes.Sealed
                    | TypeAttributes.AnsiClass,
                    module.ImportReference(typeof(Enum)));
                enumType.Fields.Add(
                    new FieldDefinition(
                        "value__",
                        FieldAttributes.Public
                        | FieldAttributes.SpecialName
                        | FieldAttributes.RTSpecialName,
                        module.TypeSystem.Int32));
                enumType.Fields.Add(
                    new FieldDefinition(
                        "Include",
                        FieldAttributes.Public
                        | FieldAttributes.Static
                        | FieldAttributes.Literal,
                        enumType)
                    {
                        Constant = 1
                    });
                module.Types.Add(enumType);

                TypeDefinition attributeType = new TypeDefinition(
                    "Newtonsoft.Json",
                    "JsonPropertyAttribute",
                    TypeAttributes.Public | TypeAttributes.Class,
                    module.ImportReference(typeof(Attribute)));
                MethodDefinition constructor = new MethodDefinition(
                    ".ctor",
                    MethodAttributes.Public
                    | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName
                    | MethodAttributes.RTSpecialName,
                    module.TypeSystem.Void);
                constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                attributeType.Methods.Add(constructor);

                MethodDefinition setter = new MethodDefinition(
                    "set_NullValueHandling",
                    MethodAttributes.Public
                    | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName,
                    module.TypeSystem.Void);
                setter.Parameters.Add(
                    new ParameterDefinition("value", ParameterAttributes.None, enumType));
                setter.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                attributeType.Methods.Add(setter);
                attributeType.Properties.Add(
                    new PropertyDefinition(
                        "NullValueHandling",
                        PropertyAttributes.None,
                        enumType)
                    {
                        SetMethod = setter
                    });
                module.Types.Add(attributeType);

                assembly.Write(Path.Combine(directory, "Newtonsoft.Json.dll"));
            }
        }
    }
}
