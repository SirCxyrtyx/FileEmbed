using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FileEmbed;

[Generator(LanguageNames.CSharp)]
public class FileEmbedGenerator : IIncrementalGenerator
{
    private const string FILE_EMBED_ATTRIBUTE_FULL_NAME = $"{FileEmbedNamespaceName}.{FileEmbedAttributeName}";
    private const string READONLYSPAN_NAME = "System.ReadOnlySpan`1";
    internal const string FileEmbedAttributeName = nameof(FileEmbedAttribute);
    internal const string FileEmbedNamespaceName = $"{nameof(FileEmbed)}";
    internal const string MaxEmbedSizeBuildProperty = "FileEmbed_MaxEmbedSize";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<MethodDeclarationSyntax> syntaxProvider = context.SyntaxProvider
                .CreateSyntaxProvider(
                    (node, _) => IsSyntaxTargetForGeneration(node),
                    (syntaxContext, _) => GetSemanticTargetForGeneration(syntaxContext))
                .Where(m => m is not null)!;

        IncrementalValueProvider<ImmutableArray<object?>> provider = syntaxProvider
            .Combine(context.CompilationProvider)
            .WithComparer(new LambdaComparer<(MethodDeclarationSyntax, Compilation)>(
                static (left, right) => left.Item1.Equals(right.Item1),
                static o => o.Item1.GetHashCode()))
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(GetMethodInfoOrDiagnostic)
            .Collect();

        context.RegisterSourceOutput(provider, static (context, results) =>
        {
            var methodsToGenerate = new List<EmbedMethodGenerationData>();
            foreach (object? result in results)
            {
                switch (result)
                {
                    case Diagnostic d:
                        context.ReportDiagnostic(d);
                        break;
                    case EmbedMethodGenerationData embedMethod:
                        methodsToGenerate.Add(embedMethod);
                        break;
                }
            }
            if (methodsToGenerate.Count > 0)
            {
                CodeEmitter.EmitEmbedMethods(methodsToGenerate, context);
            }
        });

        context.RegisterPostInitializationOutput(CodeEmitter.EmitAttribute);
    }

    private static bool IsSyntaxTargetForGeneration(SyntaxNode node) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 };

    private static MethodDeclarationSyntax? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
        var methodDeclarationSyntax = (MethodDeclarationSyntax)context.Node;

        foreach (AttributeListSyntax attributeListSyntax in methodDeclarationSyntax.AttributeLists)
        {
            foreach (AttributeSyntax attributeSyntax in attributeListSyntax.Attributes)
            {
                if (context.SemanticModel.GetSymbolInfo(attributeSyntax).Symbol is IMethodSymbol attributeSymbol &&
                    attributeSymbol.ContainingType.ToDisplayString() == FILE_EMBED_ATTRIBUTE_FULL_NAME)
                {
                    return methodDeclarationSyntax;
                }
            }
        }

        return null;
    }

    private static object? GetMethodInfoOrDiagnostic(((MethodDeclarationSyntax, Compilation), AnalyzerConfigOptionsProvider) tuple, CancellationToken cancellationToken)
    {
        ((MethodDeclarationSyntax methodSyntax, Compilation compilation), AnalyzerConfigOptionsProvider config) = tuple;
        
        try
        {
            int customMaxEmbedSize = -1;
            if (config.GlobalOptions.TryGetValue($"build_property.{MaxEmbedSizeBuildProperty}", out string? maxEmbedSizeString))
            {
                if (!int.TryParse(maxEmbedSizeString, out customMaxEmbedSize))
                {
                    return Diagnostics.InvalidMaxLength(methodSyntax.GetLocation(), maxEmbedSizeString);
                }
            }
            if (!config.GlobalOptions.TryGetValue("build_property.projectdir", out string? projectDirPath))
            {
                return Diagnostics.NoProjectDir(methodSyntax.GetLocation());
            }

            INamedTypeSymbol? fileEmbedAttributeSymbol = compilation.GetTypeByMetadataName(FILE_EMBED_ATTRIBUTE_FULL_NAME);
            INamedTypeSymbol? readOnlySpanSymbol = compilation.GetTypeByMetadataName(READONLYSPAN_NAME);
            INamedTypeSymbol? byteSymbol = compilation.GetTypeByMetadataName("System.Byte");
            if (fileEmbedAttributeSymbol is null || readOnlySpanSymbol is null || byteSymbol is null)
            {
                return null;
            }
            INamedTypeSymbol readOnlyByteSpanSymbol = readOnlySpanSymbol.Construct(byteSymbol);

            if (methodSyntax.Parent is not TypeDeclarationSyntax typeDec)
            {
                return null;
            }

            SemanticModel sm = compilation.GetSemanticModel(methodSyntax.SyntaxTree);

            if (sm.GetDeclaredSymbol(methodSyntax, cancellationToken) is not IMethodSymbol embedMethodSymbol)
            {
                return null;
            }

            ImmutableArray<AttributeData> attributes = embedMethodSymbol.GetAttributes();
            if (attributes.Length == 0)
            {
                return null;
            }
        
            string? filePath = null;
            long offset = 0;
            int length = -1;
            foreach (AttributeData attributeData in attributes)
            {
                if (!SymbolEqualityComparer.Default.Equals(attributeData.AttributeClass, fileEmbedAttributeSymbol))
                {
                    continue;
                }

                if (filePath is not null)
                {
                    return Diagnostics.MultipleFileEmbedAttributes(methodSyntax.GetLocation());
                }

                ImmutableArray<TypedConstant> items = attributeData.ConstructorArguments;
                if (attributeData.ConstructorArguments.Any(ca => ca.Kind == TypedConstantKind.Error) || items.Length > 3)
                {
                    return Diagnostics.InvalidFileEmbedAttribute(methodSyntax.GetLocation());
                }
                if (items.Length is 0)
                {
                    return Diagnostics.MissingArgument(methodSyntax.GetLocation(), nameof(FileEmbedAttribute.FilePath));
                }

                filePath = items[0].Value as string;
                if (filePath is null)
                {
                    return Diagnostics.InvalidArgument(methodSyntax.GetLocation(), nameof(FileEmbedAttribute.FilePath), items[0].Value);
                }
                if (items.Length > 1)
                {
                    if (items[1].Value is not long off || off < 0)
                    {
                        return Diagnostics.InvalidArgument(methodSyntax.GetLocation(), nameof(FileEmbedAttribute.Offset), items[1].Value);
                    }
                    offset = off;
                    if (items.Length > 2)
                    {
                        if (items[2].Value is not int len || len < -1)
                        {
                            return Diagnostics.InvalidArgument(methodSyntax.GetLocation(), nameof(FileEmbedAttribute.Length), items[2].Value);
                        }
                        length = len;
                    }
                }
            }
            //will only be the case if the marker attribute wasn't there
            if (filePath is null)
            {
                return null;
            }

            if (!embedMethodSymbol.IsPartialDefinition ||
                !embedMethodSymbol.IsStatic ||
                embedMethodSymbol.IsAbstract ||
                embedMethodSymbol.Parameters.Length != 0 ||
                embedMethodSymbol.Arity != 0 ||
                !SymbolEqualityComparer.Default.Equals(embedMethodSymbol.ReturnType, readOnlyByteSpanSymbol))
            {
                return Diagnostics.InvalidSignature(methodSyntax.GetLocation());
            }

            //AdditionalText? additionalText = additionalTexts.FirstOrDefault(additionalText => additionalText.Path.EndsWith(filePath));
            //if (additionalText is null)
            //{
            //    return Diagnostics.CouldNotFindAdditionalFile(methodSyntax.GetLocation(), filePath);
            //}

            string? ns = embedMethodSymbol.ContainingType?.ContainingNamespace?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted));

            var embedMethod = new EmbedMethodGenerationData(embedMethodSymbol.Name, Path.Combine(projectDirPath, filePath), ns, methodSyntax.Modifiers.ToString(), offset, length, methodSyntax.GetLocation())
            {
                CustomMaxEmbedSize = customMaxEmbedSize,
                CanUseUtf8Literal = (sm.SyntaxTree.Options as CSharpParseOptions)?.LanguageVersion > LanguageVersion.CSharp10
            };
        
            embedMethod.ContainingTypes.Add(new ContainingType(typeDec));

            var parent = typeDec.Parent as TypeDeclarationSyntax;
            while (parent?.Kind() is SyntaxKind.ClassDeclaration or SyntaxKind.StructDeclaration or SyntaxKind.RecordDeclaration or SyntaxKind.RecordStructDeclaration or SyntaxKind.InterfaceDeclaration)
            {
                embedMethod.ContainingTypes.Add(new ContainingType(parent));
                parent = parent.Parent as TypeDeclarationSyntax;
            }

            return embedMethod;
        }
        catch (Exception e)
        {
            return Diagnostics.SourceGeneratorException(methodSyntax.GetLocation(), e);
        }
    }
}