using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;

namespace Deucarian.BuildPipeline.Analyzers
{
    [ExportCodeFixProvider(
        LanguageNames.CSharp,
        Name = nameof(DeucarianAotSafetyCodeFixProvider))]
    [Shared]
    public sealed class DeucarianAotSafetyCodeFixProvider : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create(
                DeucarianAotSafetyDiagnostics.RuntimeTypeDiscoveryId,
                DeucarianAotSafetyDiagnostics.ReflectiveInvocationId);

        public override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public override async Task RegisterCodeFixesAsync(
            CodeFixContext context)
        {
            SyntaxNode root = await context.Document
                .GetSyntaxRootAsync(context.CancellationToken)
                .ConfigureAwait(false);
            if (root == null)
            {
                return;
            }

            InvocationExpressionSyntax invocation = root
                .FindNode(context.Span, getInnermostNodeForTie: true)
                .FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocation == null)
            {
                return;
            }

            SemanticModel semanticModel = await context.Document
                .GetSemanticModelAsync(context.CancellationToken)
                .ConfigureAwait(false);
            IInvocationOperation operation = semanticModel?
                .GetOperation(invocation, context.CancellationToken)
                as IInvocationOperation;
            if (operation == null)
            {
                return;
            }

            ITypeSymbol constructedType = TryGetConstructedType(
                operation,
                semanticModel,
                context.CancellationToken);
            if (constructedType != null
                && CanConstruct(
                    constructedType,
                    semanticModel,
                    invocation.SpanStart))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Replace with direct construction",
                        cancellationToken => ReplaceWithConstructionAsync(
                            context.Document,
                            invocation,
                            constructedType,
                            cancellationToken),
                        equivalenceKey: "Deucarian.DirectConstruction"),
                    context.Diagnostics);
            }

            INamedTypeSymbol discoveredType = TryGetDiscoveredType(
                operation,
                semanticModel);
            if (discoveredType != null)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Replace string lookup with typeof",
                        cancellationToken => ReplaceWithTypeOfAsync(
                            context.Document,
                            invocation,
                            discoveredType,
                            cancellationToken),
                        equivalenceKey: "Deucarian.TypeOf"),
                    context.Diagnostics);
            }
        }

        private static ITypeSymbol TryGetConstructedType(
            IInvocationOperation invocation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            IMethodSymbol method = invocation.TargetMethod;
            if (!IsType(method.ContainingType, "System.Activator")
                || method.Name != "CreateInstance")
            {
                return null;
            }

            if (method.IsGenericMethod
                && method.TypeArguments.Length == 1
                && invocation.Arguments.Length == 0)
            {
                return method.TypeArguments[0];
            }

            if (invocation.Arguments.Length != 1)
            {
                return null;
            }

            TypeOfExpressionSyntax typeOf = invocation.Arguments[0]
                .Syntax
                .DescendantNodesAndSelf()
                .OfType<TypeOfExpressionSyntax>()
                .FirstOrDefault();
            return typeOf == null
                ? null
                : semanticModel.GetTypeInfo(
                    typeOf.Type,
                    cancellationToken).Type;
        }

        private static bool CanConstruct(
            ITypeSymbol type,
            SemanticModel semanticModel,
            int position)
        {
            ITypeParameterSymbol typeParameter = type as ITypeParameterSymbol;
            if (typeParameter != null)
            {
                return typeParameter.HasConstructorConstraint
                       || typeParameter.HasValueTypeConstraint;
            }

            INamedTypeSymbol namedType = type as INamedTypeSymbol;
            if (namedType == null
                || namedType.IsAbstract
                || (namedType.TypeKind != TypeKind.Class
                    && namedType.TypeKind != TypeKind.Struct))
            {
                return false;
            }

            if (namedType.TypeKind == TypeKind.Struct)
            {
                return true;
            }

            ISymbol enclosing = semanticModel.GetEnclosingSymbol(position);
            ISymbol within = enclosing?.ContainingType
                             ?? (ISymbol)semanticModel.Compilation.Assembly;
            foreach (IMethodSymbol constructor
                     in namedType.InstanceConstructors)
            {
                if (constructor.Parameters.Length == 0
                    && semanticModel.Compilation.IsSymbolAccessibleWithin(
                        constructor,
                        within))
                {
                    return true;
                }
            }

            return false;
        }

        private static INamedTypeSymbol TryGetDiscoveredType(
            IInvocationOperation invocation,
            SemanticModel semanticModel)
        {
            IMethodSymbol method = invocation.TargetMethod;
            if (method.Name != "GetType"
                || (!IsType(method.ContainingType, "System.Type")
                    && !IsType(
                        method.ContainingType,
                        "System.Reflection.Assembly"))
                || invocation.Arguments.Length == 0)
            {
                return null;
            }

            Optional<object> constant = invocation.Arguments[0]
                .Value.ConstantValue;
            string requestedName = constant.HasValue
                ? constant.Value as string
                : null;
            if (string.IsNullOrWhiteSpace(requestedName))
            {
                return null;
            }

            int assemblySeparator = requestedName.IndexOf(',');
            if (assemblySeparator >= 0)
            {
                requestedName = requestedName.Substring(
                    0,
                    assemblySeparator);
            }

            requestedName = requestedName.Trim();
            INamedTypeSymbol exact = semanticModel.Compilation
                .GetTypeByMetadataName(requestedName);
            if (exact != null)
            {
                return exact;
            }

            if (requestedName.IndexOf('.') >= 0
                || requestedName.IndexOf('+') >= 0)
            {
                return null;
            }

            IEnumerable<INamedTypeSymbol> candidates = semanticModel
                .LookupNamespacesAndTypes(
                    invocation.Syntax.SpanStart,
                    name: requestedName)
                .OfType<INamedTypeSymbol>();
            INamedTypeSymbol result = null;
            foreach (INamedTypeSymbol candidate in candidates)
            {
                if (result != null
                    && !SymbolEqualityComparer.Default.Equals(
                        result,
                        candidate))
                {
                    return null;
                }

                result = candidate;
            }

            return result;
        }

        private static async Task<Document>
            ReplaceWithConstructionAsync(
                Document document,
                InvocationExpressionSyntax invocation,
                ITypeSymbol type,
                CancellationToken cancellationToken)
        {
            SemanticModel semanticModel = await document
                .GetSemanticModelAsync(cancellationToken)
                .ConfigureAwait(false);
            string typeName = type.ToMinimalDisplayString(
                semanticModel,
                invocation.SpanStart);
            ObjectCreationExpressionSyntax replacement =
                SyntaxFactory.ObjectCreationExpression(
                        SyntaxFactory.ParseTypeName(typeName))
                    .WithArgumentList(SyntaxFactory.ArgumentList())
                    .WithTriviaFrom(invocation)
                    .WithAdditionalAnnotations(Formatter.Annotation);
            return await ReplaceAsync(
                    document,
                    invocation,
                    replacement,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private static async Task<Document> ReplaceWithTypeOfAsync(
            Document document,
            InvocationExpressionSyntax invocation,
            INamedTypeSymbol type,
            CancellationToken cancellationToken)
        {
            SemanticModel semanticModel = await document
                .GetSemanticModelAsync(cancellationToken)
                .ConfigureAwait(false);
            string typeName = type.ToMinimalDisplayString(
                semanticModel,
                invocation.SpanStart);
            TypeOfExpressionSyntax replacement =
                SyntaxFactory.TypeOfExpression(
                        SyntaxFactory.ParseTypeName(typeName))
                    .WithTriviaFrom(invocation)
                    .WithAdditionalAnnotations(Formatter.Annotation);
            return await ReplaceAsync(
                    document,
                    invocation,
                    replacement,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private static async Task<Document> ReplaceAsync(
            Document document,
            SyntaxNode oldNode,
            SyntaxNode newNode,
            CancellationToken cancellationToken)
        {
            SyntaxNode root = await document
                .GetSyntaxRootAsync(cancellationToken)
                .ConfigureAwait(false);
            return document.WithSyntaxRoot(root.ReplaceNode(oldNode, newNode));
        }

        private static bool IsType(
            INamedTypeSymbol type,
            string fullName)
        {
            return type != null
                   && string.Equals(
                       type.ToDisplayString(
                           SymbolDisplayFormat.CSharpErrorMessageFormat),
                       fullName,
                       StringComparison.Ordinal);
        }
    }
}
