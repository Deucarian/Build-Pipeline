using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.BuildPipeline.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Deucarian.BuildPipeline.Analyzers.Tests
{
    public sealed class DeucarianAotSafetyAnalyzerTests
    {
        [Fact]
        public async Task ActivatorCreateInstanceReportsWarningAndFixesDirectly()
        {
            const string source = @"
using System;

namespace Sample
{
    public sealed class Widget
    {
        public Widget() { }
    }

    public static class Factory
    {
        public static Widget Create()
        {
            return Activator.CreateInstance<Widget>();
        }
    }
}";

            Diagnostic diagnostic = await GetSingleDiagnosticAsync(source);

            Assert.Equal("DBP1002", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);

            string fixedSource = await ApplyFirstFixAsync(
                source,
                diagnostic);
            Assert.Contains("return new Widget();", fixedSource);
            Assert.DoesNotContain("Activator.CreateInstance", fixedSource);
        }

        [Fact]
        public async Task TypeGetTypeReportsWarningAndUsesTypeOfFix()
        {
            const string source = @"
using System;

namespace Sample
{
    public sealed class Widget { }

    public static class Lookup
    {
        public static Type Find()
        {
            return Type.GetType(""Sample.Widget"");
        }
    }
}";

            Diagnostic diagnostic = await GetSingleDiagnosticAsync(source);

            Assert.Equal("DBP1001", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);

            string fixedSource = await ApplyFirstFixAsync(
                source,
                diagnostic);
            Assert.Contains("return typeof(Widget);", fixedSource);
            Assert.DoesNotContain("Type.GetType", fixedSource);
        }

        [Fact]
        public async Task TypeOfActivatorCallUsesDirectConstructionFix()
        {
            const string source = @"
using System;

public sealed class Widget
{
    public Widget() { }
}

public static class Factory
{
    public static object Create()
    {
        return Activator.CreateInstance(typeof(Widget));
    }
}";

            Diagnostic diagnostic = await GetSingleDiagnosticAsync(source);
            string fixedSource = await ApplyFirstFixAsync(
                source,
                diagnostic);

            Assert.Equal("DBP1002", diagnostic.Id);
            Assert.Contains("return new Widget();", fixedSource);
        }

        [Fact]
        public async Task RuntimeSelectedActivatorCallHasNoSpeculativeFix()
        {
            const string source = @"
using System;

public static class Factory
{
    public static object Create(Type type)
    {
        return Activator.CreateInstance(type);
    }
}";

            Diagnostic diagnostic = await GetSingleDiagnosticAsync(source);
            int fixCount = await GetFixCountAsync(source, diagnostic);

            Assert.Equal("DBP1002", diagnostic.Id);
            Assert.Equal(0, fixCount);
        }

        [Fact]
        public async Task AssemblyScanningReportsTypeDiscoveryWarning()
        {
            const string source = @"
using System;
using System.Reflection;

public static class Lookup
{
    public static Type[] Find(Assembly assembly)
    {
        return assembly.GetTypes();
    }
}";

            Diagnostic diagnostic = await GetSingleDiagnosticAsync(source);

            Assert.Equal("DBP1001", diagnostic.Id);
            Assert.Contains(
                "System.Reflection.Assembly.GetTypes",
                diagnostic.GetMessage());
        }

        [Fact]
        public async Task ExpressionCompilationIsAMigrationWarning()
        {
            const string source = @"
using System;
using System.Linq.Expressions;

public static class RuntimeCompiler
{
    public static Func<int> Create()
    {
        return Expression.Lambda<Func<int>>(
            Expression.Constant(42)).Compile();
    }
}";

            Diagnostic diagnostic = await GetSingleDiagnosticAsync(source);

            Assert.Equal("DBP1005", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        }

        [Fact]
        public async Task ReflectionEmitIsACompilerError()
        {
            const string source = @"
using System;
using System.Reflection.Emit;

public static class RuntimeCompiler
{
    public static DynamicMethod Create()
    {
        return new DynamicMethod(
            ""Generated"",
            typeof(void),
            Type.EmptyTypes);
    }
}";

            Diagnostic diagnostic = await GetSingleDiagnosticAsync(source);

            Assert.Equal("DBP1003", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        }

        [Fact]
        public async Task UnityStringDispatchReportsWarning()
        {
            const string source = @"
namespace UnityEngine
{
    public class Component
    {
        public void SendMessage(string methodName) { }
    }

    public class MonoBehaviour : Component { }
}

public sealed class Controller : UnityEngine.MonoBehaviour
{
    public void Dispatch()
    {
        SendMessage(""Refresh"");
    }
}";

            Diagnostic diagnostic = await GetSingleDiagnosticAsync(source);

            Assert.Equal("DBP1004", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        }

        [Fact]
        public async Task JsonSpecificCallsRemainOwnedBySerialization()
        {
            const string source = @"
namespace Newtonsoft.Json
{
    public static class JsonConvert
    {
        public static T DeserializeObject<T>(string json) => default(T);
    }
}

public static class Reader
{
    public static object Read(string json)
    {
        return Newtonsoft.Json.JsonConvert.DeserializeObject<object>(json);
    }
}";

            ImmutableArray<Diagnostic> diagnostics =
                await GetDiagnosticsAsync(source);

            Assert.Empty(diagnostics);
        }

        [Fact]
        public async Task EditorAssembliesAreSkippedByDefault()
        {
            const string source = @"
using System;

public static class EditorFactory
{
    public static T Create<T>() where T : new()
    {
        return Activator.CreateInstance<T>();
    }
}";

            ImmutableArray<Diagnostic> diagnostics =
                await GetDiagnosticsAsync(
                    source,
                    "Sample.Editor");

            Assert.Empty(diagnostics);
        }

        private static async Task<Diagnostic> GetSingleDiagnosticAsync(
            string source)
        {
            ImmutableArray<Diagnostic> diagnostics =
                await GetDiagnosticsAsync(source);
            return Assert.Single(diagnostics);
        }

        private static async Task<ImmutableArray<Diagnostic>>
            GetDiagnosticsAsync(
                string source,
                string assemblyName = "Sample.Runtime")
        {
            using (AdhocWorkspace workspace = new AdhocWorkspace())
            {
                Document document = CreateDocument(
                    workspace,
                    source,
                    assemblyName);
                Compilation compilation = await document.Project
                    .GetCompilationAsync();
                CompilationWithAnalyzers analyzed =
                    compilation.WithAnalyzers(
                        ImmutableArray.Create<DiagnosticAnalyzer>(
                            new DeucarianAotSafetyAnalyzer()));
                ImmutableArray<Diagnostic> diagnostics =
                    await analyzed.GetAnalyzerDiagnosticsAsync();
                return diagnostics
                    .OrderBy(item => item.Location.SourceSpan.Start)
                    .ToImmutableArray();
            }
        }

        private static async Task<string> ApplyFirstFixAsync(
            string source,
            Diagnostic diagnostic)
        {
            using (AdhocWorkspace workspace = new AdhocWorkspace())
            {
                Document document = CreateDocument(
                    workspace,
                    source,
                    "Sample.Runtime");
                List<CodeAction> actions = new List<CodeAction>();
                CodeFixContext context = new CodeFixContext(
                    document,
                    diagnostic,
                    (action, _) => actions.Add(action),
                    CancellationToken.None);
                DeucarianAotSafetyCodeFixProvider provider =
                    new DeucarianAotSafetyCodeFixProvider();
                await provider.RegisterCodeFixesAsync(context);

                CodeAction action = Assert.Single(actions);
                ImmutableArray<CodeActionOperation> operations =
                    await action.GetOperationsAsync(CancellationToken.None);
                ApplyChangesOperation apply = Assert.Single(
                    operations.OfType<ApplyChangesOperation>());
                Document changed = apply.ChangedSolution
                    .GetDocument(document.Id);
                changed = await Formatter.FormatAsync(changed);
                SourceText text = await changed.GetTextAsync();
                return text.ToString();
            }
        }

        private static async Task<int> GetFixCountAsync(
            string source,
            Diagnostic diagnostic)
        {
            using (AdhocWorkspace workspace = new AdhocWorkspace())
            {
                Document document = CreateDocument(
                    workspace,
                    source,
                    "Sample.Runtime");
                List<CodeAction> actions = new List<CodeAction>();
                CodeFixContext context = new CodeFixContext(
                    document,
                    diagnostic,
                    (action, _) => actions.Add(action),
                    CancellationToken.None);
                await new DeucarianAotSafetyCodeFixProvider()
                    .RegisterCodeFixesAsync(context);
                return actions.Count;
            }
        }

        private static Document CreateDocument(
            AdhocWorkspace workspace,
            string source,
            string assemblyName)
        {
            ProjectId projectId = ProjectId.CreateNewId();
            DocumentId documentId = DocumentId.CreateNewId(projectId);
            Solution solution = workspace.CurrentSolution
                .AddProject(ProjectInfo.Create(
                    projectId,
                    VersionStamp.Create(),
                    assemblyName,
                    assemblyName,
                    LanguageNames.CSharp,
                    compilationOptions: new CSharpCompilationOptions(
                        OutputKind.DynamicallyLinkedLibrary),
                    parseOptions: new CSharpParseOptions(
                        LanguageVersion.CSharp10),
                    metadataReferences: GetPlatformReferences()))
                .AddDocument(
                    documentId,
                    "Test.cs",
                    SourceText.From(source));
            Assert.True(workspace.TryApplyChanges(solution));
            return workspace.CurrentSolution.GetDocument(documentId);
        }

        private static IEnumerable<MetadataReference>
            GetPlatformReferences()
        {
            string trustedAssemblies = AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES") as string;
            return trustedAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));
        }
    }
}
