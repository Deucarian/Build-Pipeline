using System;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Deucarian.BuildPipeline.Tests
{
    internal sealed class CecilAssemblyFixture : IDisposable
    {
        private readonly AssemblyDefinition assembly;
        private readonly AssemblyNameReference newtonsoftReference;
        private readonly bool ownsDirectory;

        public CecilAssemblyFixture(
            string assemblyName,
            string directoryPath = null,
            bool referencesNewtonsoft = true)
        {
            ownsDirectory = directoryPath == null;
            DirectoryPath = directoryPath
                ?? Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);

            assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(assemblyName, new Version(1, 0, 0, 0)),
                assemblyName,
                ModuleKind.Dll);
            if (referencesNewtonsoft)
            {
                newtonsoftReference = new AssemblyNameReference(
                    "Newtonsoft.Json",
                    new Version(13, 0, 0, 0));
                assembly.MainModule.AssemblyReferences.Add(newtonsoftReference);
            }
        }

        public string DirectoryPath { get; }

        public string AssemblyName => assembly.Name.Name;

        public ModuleDefinition Module => assembly.MainModule;

        public TypeDefinition AddType(
            string @namespace,
            string name,
            TypeReference baseType = null)
        {
            TypeDefinition type = new TypeDefinition(
                @namespace,
                name,
                TypeAttributes.Public | TypeAttributes.Class,
                baseType ?? Module.TypeSystem.Object);
            Module.Types.Add(type);
            return type;
        }

        public TypeDefinition AddNestedType(TypeDefinition declaringType, string name)
        {
            TypeDefinition type = new TypeDefinition(
                string.Empty,
                name,
                TypeAttributes.NestedPublic | TypeAttributes.Class,
                Module.TypeSystem.Object);
            declaringType.NestedTypes.Add(type);
            return type;
        }

        public TypeReference NewtonsoftType(string name)
        {
            if (newtonsoftReference == null)
            {
                throw new InvalidOperationException(
                    "This synthetic assembly does not reference Newtonsoft.Json.");
            }

            return new TypeReference(
                "Newtonsoft.Json",
                name,
                Module,
                newtonsoftReference);
        }

        public void AddWriterResolverDirectory(string directoryPath)
        {
            if (!(Module.AssemblyResolver is BaseAssemblyResolver resolver))
            {
                throw new InvalidOperationException(
                    "The synthetic assembly does not expose a configurable assembly resolver.");
            }

            resolver.AddSearchDirectory(directoryPath);
        }

        public void AddJsonAttribute(ICustomAttributeProvider provider, string name)
        {
            provider.CustomAttributes.Add(CreateAttribute(NewtonsoftType(name)));
        }

        public void AddJsonEnumPropertyAttribute(
            ICustomAttributeProvider provider,
            string attributeName,
            string propertyName,
            string enumTypeName,
            int value)
        {
            CustomAttribute attribute = CreateAttribute(
                NewtonsoftType(attributeName));
            attribute.Properties.Add(
                new CustomAttributeNamedArgument(
                    propertyName,
                    new CustomAttributeArgument(
                        NewtonsoftType(enumTypeName),
                        value)));
            provider.CustomAttributes.Add(attribute);
        }

        public void AddJsonTypeAttribute(
            ICustomAttributeProvider provider,
            string name,
            TypeReference reflectedType)
        {
            TypeReference attributeType = NewtonsoftType(name);
            TypeReference systemType = Module.ImportReference(typeof(Type));
            MethodReference constructor = new MethodReference(
                ".ctor",
                Module.TypeSystem.Void,
                attributeType)
            {
                HasThis = true
            };
            constructor.Parameters.Add(new ParameterDefinition(systemType));
            CustomAttribute attribute = new CustomAttribute(constructor);
            attribute.ConstructorArguments.Add(
                new CustomAttributeArgument(systemType, reflectedType));
            provider.CustomAttributes.Add(attribute);
        }

        public void AddBoxedJsonTypeAttribute(
            ICustomAttributeProvider provider,
            string name,
            TypeReference reflectedType)
        {
            TypeReference attributeType = NewtonsoftType(name);
            TypeReference systemType = Module.ImportReference(typeof(Type));
            TypeReference objectArray = new ArrayType(Module.TypeSystem.Object);
            MethodReference constructor = new MethodReference(
                ".ctor",
                Module.TypeSystem.Void,
                attributeType)
            {
                HasThis = true
            };
            constructor.Parameters.Add(new ParameterDefinition(objectArray));
            CustomAttribute attribute = new CustomAttribute(constructor);
            CustomAttributeArgument boxedType = new CustomAttributeArgument(
                Module.TypeSystem.Object,
                new CustomAttributeArgument(systemType, reflectedType));
            attribute.ConstructorArguments.Add(
                new CustomAttributeArgument(
                    objectArray,
                    new[] { boxedType }));
            provider.CustomAttributes.Add(attribute);
        }

        public void AddDataContractAttribute(ICustomAttributeProvider provider)
        {
            AssemblyNameReference serializationReference = new AssemblyNameReference(
                "System.Runtime.Serialization.Primitives",
                new Version(4, 0, 0, 0));
            Module.AssemblyReferences.Add(serializationReference);
            TypeReference attributeType = new TypeReference(
                "System.Runtime.Serialization",
                "DataContractAttribute",
                Module,
                serializationReference);
            provider.CustomAttributes.Add(CreateAttribute(attributeType));
        }

        public MethodDefinition AddConstructor(TypeDefinition type)
        {
            MethodDefinition constructor = new MethodDefinition(
                ".ctor",
                MethodAttributes.Public
                | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName,
                Module.TypeSystem.Void);
            constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            type.Methods.Add(constructor);
            return constructor;
        }

        public string Write(string fileName = null)
        {
            string path = Path.Combine(
                DirectoryPath,
                fileName ?? AssemblyName + ".dll");
            assembly.Write(path);
            return path;
        }

        public void Dispose()
        {
            assembly.Dispose();
            if (!ownsDirectory || !Directory.Exists(DirectoryPath))
            {
                return;
            }

            try
            {
                Directory.Delete(DirectoryPath, true);
            }
            catch (IOException)
            {
                // NUnit's temp cleanup can finish a transient file handle on the next run.
            }
            catch (UnauthorizedAccessException)
            {
                // NUnit's temp cleanup can finish a transient file handle on the next run.
            }
        }

        private CustomAttribute CreateAttribute(TypeReference attributeType)
        {
            MethodReference constructor = new MethodReference(
                ".ctor",
                Module.TypeSystem.Void,
                attributeType)
            {
                HasThis = true
            };
            return new CustomAttribute(constructor);
        }
    }
}
