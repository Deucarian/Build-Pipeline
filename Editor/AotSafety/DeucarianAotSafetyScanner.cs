using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Deucarian.BuildPipeline
{
    internal static class DeucarianAotSafetyScanner
    {
        private const string AssemblyMetadataAttributeName =
            "System.Reflection.AssemblyMetadataAttribute";
        private const string AotFeatureMetadataKey =
            "Deucarian.AOT.Feature";

        private static readonly StringComparer PathComparer =
            Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        private static readonly HashSet<string> TypeReflectionMethods =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "GetType",
                "GetMethod",
                "GetMethods",
                "GetMember",
                "GetMembers",
                "GetProperty",
                "GetProperties",
                "GetField",
                "GetFields",
                "GetConstructor",
                "GetConstructors",
                "GetEvent",
                "GetEvents",
                "GetNestedType",
                "GetNestedTypes",
                "GetInterface",
                "GetInterfaces",
                "GetCustomAttributes",
                "GetCustomAttribute",
                "IsDefined",
                "InvokeMember",
                "MakeGenericType"
            };

        private static readonly HashSet<string> ReflectionMemberMethods =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Invoke",
                "GetValue",
                "SetValue",
                "AddEventHandler",
                "RemoveEventHandler",
                "CreateDelegate",
                "MakeGenericMethod",
                "GetCustomAttributes",
                "GetCustomAttribute",
                "IsDefined"
            };

        internal static DeucarianAotSafetyReport Scan(
            IEnumerable<string> assemblyPaths,
            IEnumerable<string> resolverDirectories,
            DeucarianAotSafetySettings settings,
            DeucarianAotSafetyMode mode)
        {
            DeucarianAotSafetyReport report = new DeucarianAotSafetyReport
            {
                mode = mode.ToString(),
                linkerInspectionCompleted = true
            };

            if (assemblyPaths == null)
            {
                report.AddFinding(CreateInspectionFailure(
                    "AOT safety received no player assembly collection."));
                return report;
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
                report.AddFinding(CreateInspectionFailure(
                    "AOT safety received an invalid player assembly path: "
                    + exception.GetBaseException().Message));
                return report;
            }

            if (normalizedAssemblyPaths.Length == 0)
            {
                report.AddFinding(CreateInspectionFailure(
                    "AOT safety found no managed player assemblies to inspect."));
                return report;
            }

            string[] normalizedResolverDirectories = GetResolverDirectories(
                normalizedAssemblyPaths,
                resolverDirectories);
            using (DefaultAssemblyResolver resolver = new DefaultAssemblyResolver())
            {
                resolver.RemoveSearchDirectory(".");
                resolver.RemoveSearchDirectory("bin");
                for (int i = 0; i < normalizedResolverDirectories.Length; i++)
                {
                    resolver.AddSearchDirectory(normalizedResolverDirectories[i]);
                }

                for (int i = 0; i < normalizedAssemblyPaths.Length; i++)
                {
                    string path = normalizedAssemblyPaths[i];
                    if (!File.Exists(path))
                    {
                        report.AddFinding(CreateInspectionFailure(
                            "AOT safety could not find player assembly '" + path + "'."));
                        continue;
                    }

                    AssemblyDefinition assembly = null;
                    try
                    {
                        assembly = AssemblyDefinition.ReadAssembly(
                            path,
                            new ReaderParameters
                            {
                                AssemblyResolver = resolver,
                                InMemory = true,
                                ReadingMode = ReadingMode.Immediate,
                                ReadSymbols = false
                            });
                        ReadGeneratedEvidence(assembly, report);
                        if (ShouldInspectAssembly(assembly.Name.Name))
                        {
                            report.scannedAssemblyCount++;
                            InspectAssembly(assembly, settings, report);
                        }
                    }
                    catch (Exception exception)
                    {
                        report.AddFinding(CreateInspectionFailure(
                            "AOT safety could not inspect player assembly '"
                            + path + "': "
                            + exception.GetBaseException().Message));
                    }
                    finally
                    {
                        assembly?.Dispose();
                    }
                }
            }

            return report;
        }

        private static void InspectAssembly(
            AssemblyDefinition assembly,
            DeucarianAotSafetySettings settings,
            DeucarianAotSafetyReport report)
        {
            foreach (TypeDefinition type in EnumerateTypes(
                         assembly.MainModule.Types))
            {
                for (int methodIndex = 0;
                     methodIndex < type.Methods.Count;
                     methodIndex++)
                {
                    MethodDefinition method = type.Methods[methodIndex];
                    if (!method.HasBody)
                    {
                        continue;
                    }

                    for (int instructionIndex = 0;
                         instructionIndex < method.Body.Instructions.Count;
                         instructionIndex++)
                    {
                        Instruction instruction =
                            method.Body.Instructions[instructionIndex];
                        MethodReference calledMethod =
                            instruction.Operand as MethodReference;
                        if (calledMethod == null
                            || !TryClassify(calledMethod, out string category))
                        {
                            continue;
                        }

                        string calledApi = GetCalledApi(calledMethod);
                        if (settings != null
                            && settings.IsDeclaredException(
                                assembly.Name.Name,
                                type.FullName,
                                method.Name,
                                calledApi))
                        {
                            report.declaredExceptionCount++;
                            continue;
                        }

                        report.AddFinding(new DeucarianAotSafetyFinding
                        {
                            category = category,
                            assemblyName = assembly.Name.Name,
                            declaringType = type.FullName,
                            method = method.Name,
                            calledApi = calledApi,
                            message = "Unbounded runtime dynamic access calls '"
                                      + calledApi
                                      + "'. Generate or explicitly compose the target path instead."
                        });
                    }
                }
            }
        }

        private static bool TryClassify(
            MethodReference method,
            out string category)
        {
            string typeName = method.DeclaringType.FullName;
            string methodName = method.Name;

            if (typeName == "System.Activator"
                && methodName == "CreateInstance")
            {
                category = "DynamicConstruction";
                return true;
            }

            if (typeName == "System.Type"
                && TypeReflectionMethods.Contains(methodName))
            {
                category = "RuntimeTypeDiscovery";
                return true;
            }

            if (typeName == "System.Object" && methodName == "GetType")
            {
                category = "RuntimeTypeDiscovery";
                return true;
            }

            if ((typeName == "System.AppDomain"
                 && methodName == "GetAssemblies")
                || (typeName == "System.Reflection.Assembly"
                    && (methodName == "GetTypes"
                        || methodName == "GetType"
                        || methodName == "Load"
                        || methodName == "LoadFrom"
                        || methodName == "LoadFile")))
            {
                category = "AssemblyDiscovery";
                return true;
            }

            if (typeName.StartsWith(
                    "System.Reflection.",
                    StringComparison.Ordinal)
                && ReflectionMemberMethods.Contains(methodName))
            {
                category = "ReflectiveInvocation";
                return true;
            }

            if ((typeName == "System.Delegate"
                 && methodName == "CreateDelegate")
                || (typeName == "System.Runtime.Serialization.FormatterServices"
                    && methodName == "GetUninitializedObject"))
            {
                category = "ReflectiveInvocation";
                return true;
            }

            if (typeName == "System.Linq.Expressions.LambdaExpression"
                && methodName == "Compile")
            {
                category = "RuntimeCodeGeneration";
                return true;
            }

            if (typeName == "Newtonsoft.Json.JsonConvert"
                && (methodName == "SerializeObject"
                    || methodName == "DeserializeObject"
                    || methodName == "PopulateObject"))
            {
                category = "ReflectionBasedSerialization";
                return true;
            }

            if (typeName == "Newtonsoft.Json.JsonSerializer"
                && (methodName == "Serialize"
                    || methodName == "Deserialize"
                    || methodName == "Populate"))
            {
                category = "ReflectionBasedSerialization";
                return true;
            }

            if (typeName == "Newtonsoft.Json.Linq.JToken"
                && (methodName == "ToObject"
                    || methodName == "FromObject"))
            {
                category = "ReflectionBasedSerialization";
                return true;
            }

            if (typeName == "System.Text.Json.JsonSerializer"
                && (methodName.StartsWith("Serialize", StringComparison.Ordinal)
                    || methodName.StartsWith("Deserialize", StringComparison.Ordinal))
                && !UsesGeneratedSystemTextJsonMetadata(method))
            {
                category = "ReflectionBasedSerialization";
                return true;
            }

            if ((typeName == "System.Xml.Serialization.XmlSerializer"
                 && (methodName == ".ctor"
                     || methodName == "Serialize"
                     || methodName == "Deserialize"))
                || (typeName == "System.Runtime.Serialization.DataContractSerializer"
                    && (methodName == ".ctor"
                        || methodName == "ReadObject"
                        || methodName == "WriteObject")))
            {
                category = "ReflectionBasedSerialization";
                return true;
            }

            if (IsUnityStringDispatch(typeName, method))
            {
                category = "StringBasedUnityDispatch";
                return true;
            }

            category = null;
            return false;
        }

        private static bool IsUnityStringDispatch(
            string typeName,
            MethodReference method)
        {
            if (typeName != "UnityEngine.Component"
                && typeName != "UnityEngine.GameObject"
                && typeName != "UnityEngine.MonoBehaviour"
                && typeName != "UnityEngine.ScriptableObject")
            {
                return false;
            }

            string methodName = method.Name;
            bool dynamicMethod = methodName == "SendMessage"
                                 || methodName == "BroadcastMessage"
                                 || methodName == "SendMessageUpwards"
                                 || methodName == "Invoke"
                                 || methodName == "InvokeRepeating"
                                 || methodName == "StartCoroutine"
                                 || methodName == "StopCoroutine"
                                 || methodName == "GetComponent"
                                 || methodName == "AddComponent"
                                 || methodName == "CreateInstance";
            if (!dynamicMethod)
            {
                return false;
            }

            return method.Parameters.Count > 0
                   && method.Parameters[0].ParameterType.FullName
                   == "System.String";
        }

        private static bool UsesGeneratedSystemTextJsonMetadata(
            MethodReference method)
        {
            for (int i = 0; i < method.Parameters.Count; i++)
            {
                string parameterType = method.Parameters[i].ParameterType.FullName;
                if (parameterType.StartsWith(
                        "System.Text.Json.Serialization.Metadata.JsonTypeInfo",
                        StringComparison.Ordinal)
                    || parameterType ==
                    "System.Text.Json.Serialization.JsonSerializerContext")
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetCalledApi(MethodReference method)
        {
            return method.DeclaringType.FullName + "::" + method.Name;
        }

        private static void ReadGeneratedEvidence(
            AssemblyDefinition assembly,
            DeucarianAotSafetyReport report)
        {
            for (int i = 0; i < assembly.CustomAttributes.Count; i++)
            {
                CustomAttribute attribute = assembly.CustomAttributes[i];
                if (attribute.AttributeType.FullName !=
                    AssemblyMetadataAttributeName
                    || attribute.ConstructorArguments.Count != 2)
                {
                    continue;
                }

                string key = attribute.ConstructorArguments[0].Value as string;
                string value = attribute.ConstructorArguments[1].Value as string;
                if (string.Equals(
                        key,
                        AotFeatureMetadataKey,
                        StringComparison.Ordinal))
                {
                    report.AddFeature(value);
                }
            }
        }

        private static bool ShouldInspectAssembly(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                return false;
            }

            return assemblyName != "mscorlib"
                   && assemblyName != "netstandard"
                   && assemblyName != "Newtonsoft.Json"
                   && !assemblyName.StartsWith("System", StringComparison.Ordinal)
                   && !assemblyName.StartsWith("Microsoft", StringComparison.Ordinal)
                   && !assemblyName.StartsWith("Mono.", StringComparison.Ordinal)
                   && !assemblyName.StartsWith("Unity", StringComparison.Ordinal)
                   && !assemblyName.StartsWith("nunit", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<TypeDefinition> EnumerateTypes(
            IEnumerable<TypeDefinition> types)
        {
            foreach (TypeDefinition type in types)
            {
                yield return type;
                foreach (TypeDefinition nested in EnumerateTypes(type.NestedTypes))
                {
                    yield return nested;
                }
            }
        }

        private static string[] GetResolverDirectories(
            IEnumerable<string> assemblyPaths,
            IEnumerable<string> resolverDirectories)
        {
            IEnumerable<string> requested = resolverDirectories
                ?? Enumerable.Empty<string>();
            return requested
                .Concat(assemblyPaths
                    .Select(Path.GetDirectoryName))
                .Where(directory => !string.IsNullOrWhiteSpace(directory))
                .Select(Path.GetFullPath)
                .Where(Directory.Exists)
                .Distinct(PathComparer)
                .OrderBy(directory => directory, StringComparer.Ordinal)
                .ToArray();
        }

        private static DeucarianAotSafetyFinding CreateInspectionFailure(
            string message)
        {
            return new DeucarianAotSafetyFinding
            {
                category = "InspectionFailure",
                message = message
            };
        }
    }
}
