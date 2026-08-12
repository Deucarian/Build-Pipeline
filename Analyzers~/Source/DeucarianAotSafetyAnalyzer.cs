using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Deucarian.BuildPipeline.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DeucarianAotSafetyAnalyzer : DiagnosticAnalyzer
    {
        private const string AnalysisOption = "deucarian_aot_analysis";

        private static readonly HashSet<string> TypeDiscoveryMethods =
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

        private static readonly HashSet<string> ReflectionInvocationMethods =
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

        private static readonly HashSet<string> UnityStringMethods =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "SendMessage",
                "BroadcastMessage",
                "SendMessageUpwards",
                "Invoke",
                "InvokeRepeating",
                "StartCoroutine",
                "StopCoroutine",
                "GetComponent",
                "AddComponent",
                "CreateInstance"
            };

        public override ImmutableArray<DiagnosticDescriptor>
            SupportedDiagnostics => ImmutableArray.Create(
                DeucarianAotSafetyDiagnostics.RuntimeTypeDiscovery,
                DeucarianAotSafetyDiagnostics.ReflectiveInvocation,
                DeucarianAotSafetyDiagnostics.RuntimeCodeGeneration,
                DeucarianAotSafetyDiagnostics.UnityStringDispatch,
                DeucarianAotSafetyDiagnostics.RuntimeExpressionCompilation);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(
                GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(RegisterCompilation);
        }

        private static void RegisterCompilation(
            CompilationStartAnalysisContext context)
        {
            if (!ShouldAnalyze(context))
            {
                return;
            }

            context.RegisterOperationAction(
                AnalyzeInvocation,
                OperationKind.Invocation);
            context.RegisterOperationAction(
                AnalyzeObjectCreation,
                OperationKind.ObjectCreation);
        }

        private static bool ShouldAnalyze(
            CompilationStartAnalysisContext context)
        {
            string configuredValue;
            if (context.Options.AnalyzerConfigOptionsProvider.GlobalOptions
                .TryGetValue(AnalysisOption, out configuredValue))
            {
                if (string.Equals(
                        configuredValue,
                        "disabled",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        configuredValue,
                        "false",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (string.Equals(
                        configuredValue,
                        "enabled",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        configuredValue,
                        "true",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            string assemblyName = context.Compilation.AssemblyName
                                  ?? string.Empty;
            if (assemblyName.EndsWith(
                    ".Editor",
                    StringComparison.OrdinalIgnoreCase)
                || assemblyName.IndexOf(
                    ".Editor.",
                    StringComparison.OrdinalIgnoreCase) >= 0
                || assemblyName.EndsWith(
                    ".Tests",
                    StringComparison.OrdinalIgnoreCase)
                || assemblyName.IndexOf(
                    ".Tests.",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            foreach (AssemblyIdentity reference
                     in context.Compilation.ReferencedAssemblyNames)
            {
                if (string.Equals(
                        reference.Name,
                        "UnityEditor",
                        StringComparison.Ordinal)
                    || reference.Name.StartsWith(
                        "UnityEditor.",
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context)
        {
            IInvocationOperation invocation =
                (IInvocationOperation)context.Operation;
            IMethodSymbol method = invocation.TargetMethod;
            DiagnosticDescriptor descriptor = Classify(method);
            if (descriptor == null)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                descriptor,
                invocation.Syntax.GetLocation(),
                FormatApi(method)));
        }

        private static void AnalyzeObjectCreation(
            OperationAnalysisContext context)
        {
            IObjectCreationOperation creation =
                (IObjectCreationOperation)context.Operation;
            INamedTypeSymbol type = creation.Type as INamedTypeSymbol;
            if (type == null || !IsRuntimeGeneratedType(type))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DeucarianAotSafetyDiagnostics.RuntimeCodeGeneration,
                creation.Syntax.GetLocation(),
                type.ToDisplayString(
                    SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }

        private static DiagnosticDescriptor Classify(IMethodSymbol method)
        {
            INamedTypeSymbol type = method.ContainingType;
            string methodName = method.Name;

            if (IsRuntimeGeneratedType(type))
            {
                return DeucarianAotSafetyDiagnostics.RuntimeCodeGeneration;
            }

            if (IsNamespace(type, "System.Linq.Expressions")
                && methodName == "Compile")
            {
                return DeucarianAotSafetyDiagnostics
                    .RuntimeExpressionCompilation;
            }

            if (IsType(type, "System.Activator")
                && methodName == "CreateInstance")
            {
                return DeucarianAotSafetyDiagnostics.ReflectiveInvocation;
            }

            if ((IsType(type, "System.Type")
                 && TypeDiscoveryMethods.Contains(methodName))
                || (IsType(type, "System.Object")
                    && methodName == "GetType")
                || (IsType(type, "System.AppDomain")
                    && methodName == "GetAssemblies")
                || (IsType(type, "System.Reflection.Assembly")
                    && (methodName == "GetTypes"
                        || methodName == "GetExportedTypes"
                        || methodName == "GetType"
                        || methodName == "Load"
                        || methodName == "LoadFrom"
                        || methodName == "LoadFile")))
            {
                return DeucarianAotSafetyDiagnostics.RuntimeTypeDiscovery;
            }

            if ((IsNamespace(type, "System.Reflection")
                 && ReflectionInvocationMethods.Contains(methodName))
                || (IsType(type, "System.Delegate")
                    && (methodName == "CreateDelegate"
                        || methodName == "DynamicInvoke"))
                || (IsType(
                        type,
                        "System.Runtime.Serialization.FormatterServices")
                    && methodName == "GetUninitializedObject"))
            {
                return DeucarianAotSafetyDiagnostics.ReflectiveInvocation;
            }

            if (IsUnityStringDispatch(method))
            {
                return DeucarianAotSafetyDiagnostics.UnityStringDispatch;
            }

            return null;
        }

        private static bool IsRuntimeGeneratedType(INamedTypeSymbol type)
        {
            return IsNamespace(type, "System.Reflection.Emit")
                   || IsType(type, "System.Reflection.Emit.DynamicMethod");
        }

        private static bool IsUnityStringDispatch(IMethodSymbol method)
        {
            if (!UnityStringMethods.Contains(method.Name)
                || method.Parameters.Length == 0
                || method.Parameters[0].Type.SpecialType
                != SpecialType.System_String)
            {
                return false;
            }

            INamedTypeSymbol type = method.ContainingType;
            while (type != null)
            {
                string typeName = GetTypeName(type);
                if (typeName == "UnityEngine.Component"
                    || typeName == "UnityEngine.GameObject"
                    || typeName == "UnityEngine.MonoBehaviour"
                    || typeName == "UnityEngine.ScriptableObject")
                {
                    return true;
                }

                type = type.BaseType;
            }

            return false;
        }

        private static bool IsNamespace(
            INamedTypeSymbol type,
            string namespaceName)
        {
            string actual = type?.ContainingNamespace?.ToDisplayString()
                            ?? string.Empty;
            return actual == namespaceName
                   || actual.StartsWith(
                       namespaceName + ".",
                       StringComparison.Ordinal);
        }

        private static bool IsType(
            INamedTypeSymbol type,
            string fullName)
        {
            return string.Equals(
                GetTypeName(type),
                fullName,
                StringComparison.Ordinal);
        }

        private static string GetTypeName(INamedTypeSymbol type)
        {
            if (type == null)
            {
                return string.Empty;
            }

            return type.ToDisplayString(
                SymbolDisplayFormat.CSharpErrorMessageFormat);
        }

        private static string FormatApi(IMethodSymbol method)
        {
            return GetTypeName(method.ContainingType) + "." + method.Name;
        }
    }
}
