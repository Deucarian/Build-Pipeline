using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using UnityEditor.Build;

namespace Deucarian.BuildPipeline
{
    internal static class NewtonsoftJsonContractDiscovery
    {
        private const string NewtonsoftAssemblyName = "Newtonsoft.Json";
        private const string NewtonsoftNamespace = "Newtonsoft.Json";

        private static readonly StringComparer PathComparer =
            Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        private static readonly HashSet<string> DataContractAttributeNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "System.Runtime.Serialization.DataContractAttribute",
                "System.Runtime.Serialization.DataMemberAttribute",
                "System.Runtime.Serialization.EnumMemberAttribute",
                "System.Runtime.Serialization.IgnoreDataMemberAttribute",
                "System.Runtime.Serialization.OnDeserializedAttribute",
                "System.Runtime.Serialization.OnDeserializingAttribute",
                "System.Runtime.Serialization.OnSerializedAttribute",
                "System.Runtime.Serialization.OnSerializingAttribute"
            };

        public static NewtonsoftJsonContractCatalog Discover(string inputDirectory)
        {
            if (string.IsNullOrWhiteSpace(inputDirectory))
            {
                throw new BuildFailedException(
                    "Deucarian Newtonsoft linker received no managed assembly directory. "
                    + "Automatic contract preservation cannot be completed, so the build was stopped.");
            }

            string fullInputDirectory;
            try
            {
                fullInputDirectory = Path.GetFullPath(inputDirectory);
            }
            catch (Exception exception)
            {
                throw CreateFailure(
                    "Deucarian Newtonsoft linker received an invalid managed assembly directory.",
                    exception);
            }

            if (!Directory.Exists(fullInputDirectory))
            {
                throw new BuildFailedException(
                    "Deucarian Newtonsoft linker could not find managed assembly directory '"
                    + fullInputDirectory
                    + "'. Automatic contract preservation cannot be completed, so the build was stopped.");
            }

            string[] assemblyPaths;
            try
            {
                assemblyPaths = Directory.GetFiles(
                    fullInputDirectory,
                    "*.dll",
                    SearchOption.TopDirectoryOnly);
                Array.Sort(assemblyPaths, StringComparer.Ordinal);
            }
            catch (Exception exception)
            {
                throw CreateFailure(
                    "Deucarian Newtonsoft linker could not enumerate managed assemblies in '"
                    + fullInputDirectory
                    + "'.",
                    exception);
            }

            if (assemblyPaths.Length == 0)
            {
                throw new BuildFailedException(
                    "Deucarian Newtonsoft linker found no managed assemblies in '"
                    + fullInputDirectory
                    + "'. Automatic contract preservation cannot be completed, so the build was stopped.");
            }

            return Discover(assemblyPaths, new[] { fullInputDirectory });
        }

        public static NewtonsoftJsonContractCatalog Discover(
            IEnumerable<string> assemblyPaths,
            IEnumerable<string> resolverDirectories)
        {
            if (assemblyPaths == null)
            {
                throw new BuildFailedException(
                    "Deucarian Newtonsoft linker received no player assemblies. "
                    + "Automatic contract preservation cannot be completed, so the build was stopped.");
            }

            string[] normalizedAssemblyPaths;
            try
            {
                normalizedAssemblyPaths = assemblyPaths
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(Path.GetFullPath)
                    .Distinct(PathComparer)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception exception)
            {
                throw CreateFailure(
                    "Deucarian Newtonsoft linker received an invalid player assembly path.",
                    exception);
            }

            if (normalizedAssemblyPaths.Length == 0)
            {
                return new NewtonsoftJsonContractCatalog();
            }

            foreach (string assemblyPath in normalizedAssemblyPaths)
            {
                if (!File.Exists(assemblyPath))
                {
                    throw new BuildFailedException(
                        "Deucarian Newtonsoft linker could not find target player assembly '"
                        + assemblyPath
                        + "'. Automatic contract preservation cannot be completed, so the build was stopped.");
                }
            }

            string[] normalizedResolverDirectories = GetResolverDirectories(
                normalizedAssemblyPaths,
                resolverDirectories);
            List<AssemblyDefinition> assemblies = new List<AssemblyDefinition>();
            using (DefaultAssemblyResolver resolver = new DefaultAssemblyResolver())
            {
                resolver.RemoveSearchDirectory(".");
                resolver.RemoveSearchDirectory("bin");
                foreach (string directory in normalizedResolverDirectories)
                {
                    resolver.AddSearchDirectory(directory);
                }

                try
                {
                    ReadAssemblies(normalizedAssemblyPaths, resolver, assemblies);
                    return Discover(assemblies);
                }
                catch (BuildFailedException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw CreateFailure(
                        "Deucarian Newtonsoft linker could not inspect target player assemblies. "
                        + "Automatic contract preservation cannot be completed, so the build was stopped.",
                        exception);
                }
                finally
                {
                    foreach (AssemblyDefinition assembly in assemblies)
                    {
                        assembly.Dispose();
                    }
                }
            }
        }

        internal static NewtonsoftJsonContractCatalog Discover(
            IEnumerable<AssemblyDefinition> assemblies)
        {
            if (assemblies == null)
            {
                throw new ArgumentNullException(nameof(assemblies));
            }

            List<AssemblyDefinition> assemblyList = assemblies
                .Where(assembly => assembly != null)
                .ToList();
            NewtonsoftJsonContractCatalog catalog = new NewtonsoftJsonContractCatalog();
            if (!UsesNewtonsoftJson(assemblyList))
            {
                return catalog;
            }

            Dictionary<string, TypeDefinition> typesByIdentity =
                CreateTypeIndex(assemblyList);

            foreach (AssemblyDefinition assembly in assemblyList)
            {
                if (string.Equals(
                        assembly.Name.Name,
                        NewtonsoftAssemblyName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                string assemblyName = assembly.Name.Name;
                foreach (TypeDefinition type in EnumerateTypes(assembly.MainModule.Types))
                {
                    List<TypeReference> referencedTypes = new List<TypeReference>();
                    if (HasSerializationMetadata(
                            type,
                            typesByIdentity,
                            referencedTypes))
                    {
                        catalog.Add(assemblyName, type.FullName);
                    }

                    foreach (TypeReference referencedType in referencedTypes)
                    {
                        AddReferencedType(catalog, referencedType);
                    }
                }
            }

            return catalog;
        }

        private static bool UsesNewtonsoftJson(
            IEnumerable<AssemblyDefinition> assemblies)
        {
            return assemblies.Any(assembly =>
                string.Equals(
                    assembly.Name.Name,
                    NewtonsoftAssemblyName,
                    StringComparison.Ordinal)
                || assembly.MainModule.AssemblyReferences.Any(reference =>
                    string.Equals(
                        reference.Name,
                        NewtonsoftAssemblyName,
                        StringComparison.Ordinal)));
        }

        private static string[] GetResolverDirectories(
            IEnumerable<string> assemblyPaths,
            IEnumerable<string> resolverDirectories)
        {
            List<string> directories = assemblyPaths
                .Select(Path.GetDirectoryName)
                .Where(directory => !string.IsNullOrWhiteSpace(directory))
                .ToList();
            if (resolverDirectories != null)
            {
                try
                {
                    directories.AddRange(
                        resolverDirectories
                            .Where(directory => !string.IsNullOrWhiteSpace(directory))
                            .Select(Path.GetFullPath));
                }
                catch (Exception exception)
                {
                    throw CreateFailure(
                        "Deucarian Newtonsoft linker received an invalid assembly resolver directory.",
                        exception);
                }
            }

            string[] normalizedDirectories = directories
                .Distinct(PathComparer)
                .OrderBy(directory => directory, StringComparer.Ordinal)
                .ToArray();
            foreach (string directory in normalizedDirectories)
            {
                if (!Directory.Exists(directory))
                {
                    throw new BuildFailedException(
                        "Deucarian Newtonsoft linker could not find assembly resolver directory '"
                        + directory
                        + "'. Automatic contract preservation cannot be completed, so the build was stopped.");
                }
            }

            return normalizedDirectories;
        }

        private static Dictionary<string, TypeDefinition> CreateTypeIndex(
            IEnumerable<AssemblyDefinition> assemblies)
        {
            Dictionary<string, TypeDefinition> types =
                new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
            foreach (AssemblyDefinition assembly in assemblies)
            {
                foreach (TypeDefinition type in EnumerateTypes(assembly.MainModule.Types))
                {
                    types[CreateTypeIdentity(assembly.Name.Name, type.FullName)] = type;
                }
            }

            return types;
        }

        private static void ReadAssemblies(
            IEnumerable<string> assemblyPaths,
            IAssemblyResolver resolver,
            ICollection<AssemblyDefinition> assemblies)
        {
            HashSet<string> assemblyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (string assemblyPath in assemblyPaths)
            {
                AssemblyDefinition assembly;
                try
                {
                    assembly = AssemblyDefinition.ReadAssembly(
                        assemblyPath,
                        new ReaderParameters
                        {
                            AssemblyResolver = resolver,
                            InMemory = true,
                            ReadSymbols = false,
                            ReadingMode = ReadingMode.Deferred
                        });
                }
                catch (Exception exception)
                {
                    throw CreateFailure(
                        "Deucarian Newtonsoft linker could not inspect '"
                        + assemblyPath
                        + "'. Automatic contract preservation cannot be completed, so the build was stopped.",
                        exception);
                }

                string assemblyName = assembly.Name.Name;
                if (!assemblyNames.Add(assemblyName))
                {
                    assembly.Dispose();
                    throw new BuildFailedException(
                        "Deucarian Newtonsoft linker found duplicate player assembly name '"
                        + assemblyName
                        + "'. Automatic contract preservation cannot be completed, so the build was stopped.");
                }

                assemblies.Add(assembly);
            }
        }

        private static IEnumerable<TypeDefinition> EnumerateTypes(
            IEnumerable<TypeDefinition> rootTypes)
        {
            foreach (TypeDefinition type in rootTypes)
            {
                yield return type;
                foreach (TypeDefinition nestedType in EnumerateTypes(type.NestedTypes))
                {
                    yield return nestedType;
                }
            }
        }

        private static bool HasSerializationMetadata(
            TypeDefinition type,
            IReadOnlyDictionary<string, TypeDefinition> typesByIdentity,
            ICollection<TypeReference> referencedTypes)
        {
            bool found = InspectDeclaredSerializationMetadata(
                type,
                referencedTypes);
            found |= HasInheritedSerializationMetadata(
                type,
                typesByIdentity,
                referencedTypes);

            return found;
        }

        private static bool InspectDeclaredSerializationMetadata(
            TypeDefinition type,
            ICollection<TypeReference> referencedTypes)
        {
            bool found = Inspect(type, referencedTypes);

            foreach (GenericParameter parameter in type.GenericParameters)
            {
                found |= Inspect(parameter, referencedTypes);
            }

            foreach (FieldDefinition field in type.Fields)
            {
                found |= Inspect(field, referencedTypes);
            }

            foreach (PropertyDefinition property in type.Properties)
            {
                found |= Inspect(property, referencedTypes);
            }

            foreach (EventDefinition eventDefinition in type.Events)
            {
                found |= Inspect(eventDefinition, referencedTypes);
            }

            foreach (MethodDefinition method in type.Methods)
            {
                found |= Inspect(method, referencedTypes);
                found |= Inspect(method.MethodReturnType, referencedTypes);
                foreach (ParameterDefinition parameter in method.Parameters)
                {
                    found |= Inspect(parameter, referencedTypes);
                }

                foreach (GenericParameter parameter in method.GenericParameters)
                {
                    found |= Inspect(parameter, referencedTypes);
                }
            }

            return found;
        }

        private static bool HasInheritedSerializationMetadata(
            TypeDefinition type,
            IReadOnlyDictionary<string, TypeDefinition> typesByIdentity,
            ICollection<TypeReference> referencedTypes)
        {
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
            TypeReference baseType = type.BaseType;
            bool found = false;
            while (baseType != null)
            {
                TypeReference elementType = baseType.GetElementType();
                string assemblyName = GetScopeAssemblyName(elementType);
                string identity = CreateTypeIdentity(assemblyName, elementType.FullName);
                if (!visited.Add(identity)
                    || !typesByIdentity.TryGetValue(
                        identity,
                        out TypeDefinition baseDefinition))
                {
                    break;
                }

                found |= InspectDeclaredSerializationMetadata(
                    baseDefinition,
                    referencedTypes);
                baseType = baseDefinition.BaseType;
            }

            return found;
        }

        private static bool Inspect(
            ICustomAttributeProvider provider,
            ICollection<TypeReference> referencedTypes)
        {
            if (provider == null || !provider.HasCustomAttributes)
            {
                return false;
            }

            bool found = false;
            foreach (CustomAttribute attribute in provider.CustomAttributes)
            {
                if (!IsSerializationAttribute(attribute.AttributeType))
                {
                    continue;
                }

                found = true;
                CollectReferencedTypes(attribute, referencedTypes);
            }

            return found;
        }

        private static bool IsSerializationAttribute(TypeReference attributeType)
        {
            if (attributeType == null)
            {
                return false;
            }

            if (DataContractAttributeNames.Contains(attributeType.FullName))
            {
                return true;
            }

            string scopeAssemblyName = GetScopeAssemblyName(attributeType);
            string attributeNamespace = attributeType.Namespace ?? string.Empty;
            return string.Equals(
                       scopeAssemblyName,
                       NewtonsoftAssemblyName,
                       StringComparison.Ordinal)
                   && (string.Equals(
                           attributeNamespace,
                           NewtonsoftNamespace,
                           StringComparison.Ordinal)
                       || attributeNamespace.StartsWith(
                           NewtonsoftNamespace + ".",
                           StringComparison.Ordinal));
        }

        private static void CollectReferencedTypes(
            CustomAttribute attribute,
            ICollection<TypeReference> referencedTypes)
        {
            foreach (CustomAttributeArgument argument in attribute.ConstructorArguments)
            {
                CollectReferencedTypes(argument, referencedTypes);
            }

            foreach (CustomAttributeNamedArgument argument in attribute.Fields)
            {
                CollectReferencedTypes(argument.Argument, referencedTypes);
            }

            foreach (CustomAttributeNamedArgument argument in attribute.Properties)
            {
                CollectReferencedTypes(argument.Argument, referencedTypes);
            }
        }

        private static void CollectReferencedTypes(
            CustomAttributeArgument argument,
            ICollection<TypeReference> referencedTypes)
        {
            if (argument.Value is TypeReference typeReference)
            {
                referencedTypes.Add(typeReference);
                return;
            }

            if (argument.Value is CustomAttributeArgument[] arguments)
            {
                foreach (CustomAttributeArgument nestedArgument in arguments)
                {
                    CollectReferencedTypes(nestedArgument, referencedTypes);
                }

                return;
            }

            if (argument.Value is CustomAttributeArgument boxedArgument)
            {
                CollectReferencedTypes(boxedArgument, referencedTypes);
            }
        }

        private static void AddReferencedType(
            NewtonsoftJsonContractCatalog catalog,
            TypeReference typeReference)
        {
            TypeReference elementType = typeReference.GetElementType();
            string assemblyName = GetScopeAssemblyName(elementType);
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                throw new BuildFailedException(
                    "Deucarian Newtonsoft linker could not determine the assembly for reflected type '"
                    + elementType.FullName
                    + "'. Automatic contract preservation cannot be completed, so the build was stopped.");
            }

            catalog.Add(assemblyName, elementType.FullName);
        }

        private static string GetScopeAssemblyName(TypeReference typeReference)
        {
            IMetadataScope scope = typeReference.Scope;
            if (scope is AssemblyNameReference assemblyReference)
            {
                return assemblyReference.Name;
            }

            if (scope is ModuleDefinition module && module.Assembly != null)
            {
                return module.Assembly.Name.Name;
            }

            if (typeReference.Module?.Assembly != null)
            {
                return typeReference.Module.Assembly.Name.Name;
            }

            return string.Empty;
        }

        private static string CreateTypeIdentity(string assemblyName, string typeName)
        {
            return (assemblyName ?? string.Empty) + "\n" + typeName;
        }

        private static BuildFailedException CreateFailure(
            string message,
            Exception exception)
        {
            return new BuildFailedException(new InvalidOperationException(message, exception));
        }
    }
}
