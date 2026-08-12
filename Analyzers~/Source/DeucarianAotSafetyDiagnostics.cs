using Microsoft.CodeAnalysis;

namespace Deucarian.BuildPipeline.Analyzers
{
    internal static class DeucarianAotSafetyDiagnostics
    {
        internal const string RuntimeTypeDiscoveryId = "DBP1001";
        internal const string ReflectiveInvocationId = "DBP1002";
        internal const string RuntimeCodeGenerationId = "DBP1003";
        internal const string UnityStringDispatchId = "DBP1004";
        internal const string RuntimeExpressionCompilationId = "DBP1005";

        private const string Category = "AOT Safety";
        private const string HelpBase =
            "https://github.com/Deucarian/Build-Pipeline#compiler-backed-aot-diagnostics";

        internal static readonly DiagnosticDescriptor RuntimeTypeDiscovery =
            new DiagnosticDescriptor(
                RuntimeTypeDiscoveryId,
                "Runtime type discovery is not AOT-safe",
                "'{0}' discovers types or members at runtime; use typeof(...), generated registration, or an explicit registry",
                Category,
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true,
                description:
                    "Runtime type and assembly discovery hides code reachability from Unity's managed linker and IL2CPP.",
                helpLinkUri: HelpBase);

        internal static readonly DiagnosticDescriptor ReflectiveInvocation =
            new DiagnosticDescriptor(
                ReflectiveInvocationId,
                "Reflective invocation or construction is not AOT-safe",
                "'{0}' invokes or constructs a runtime-selected target; use a direct call, constructor, factory, or explicit registry",
                Category,
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true,
                description:
                    "Reflection-based invocation and dynamic construction can target members that stripping removes and generic combinations IL2CPP did not compile.",
                helpLinkUri: HelpBase);

        internal static readonly DiagnosticDescriptor RuntimeCodeGeneration =
            new DiagnosticDescriptor(
                RuntimeCodeGenerationId,
                "Runtime code generation is unsupported in AOT players",
                "'{0}' generates or invokes code dynamically; replace it with generated or statically compiled code",
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description:
                    "Runtime code generation and dynamic invocation cannot be made reliable by linker preservation in IL2CPP AOT players.",
                helpLinkUri: HelpBase);

        internal static readonly DiagnosticDescriptor UnityStringDispatch =
            new DiagnosticDescriptor(
                UnityStringDispatchId,
                "String-based Unity dispatch is stripping-unsafe",
                "'{0}' selects Unity code by string; use a strongly typed component, direct call, or explicit delegate",
                Category,
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true,
                description:
                    "Unity string dispatch hides the target member or type from the compiler and managed linker.",
                helpLinkUri: HelpBase);

        internal static readonly DiagnosticDescriptor
            RuntimeExpressionCompilation = new DiagnosticDescriptor(
                RuntimeExpressionCompilationId,
                "Runtime expression compilation is not AOT-safe by default",
                "'{0}' compiles an expression at runtime; use generated or statically compiled code, or an explicitly audited interpreter boundary",
                Category,
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true,
                description:
                    "Runtime expression compilation can require dynamic code generation or reflection that is unavailable or stripping-unsafe in IL2CPP players.",
                helpLinkUri: HelpBase);
    }
}
