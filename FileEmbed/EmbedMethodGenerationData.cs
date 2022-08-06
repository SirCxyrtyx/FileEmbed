using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FileEmbed;

internal class EmbedMethodGenerationData
{
    public string Name { get; }
    public string FilePath { get; }
    public string? Namespace { get; }
    public string Modifiers { get; }
    public List<ContainingType> ContainingTypes { get; }
    public long Offset { get; }
    public int Length { get; }
    public Location DiagnosticLocation { get; }
    public bool CanUseUtf8Literal { get; init; }
    public int CustomMaxEmbedSize { get; init; }

    public EmbedMethodGenerationData(string name, string filePath, string? ns, string modifiers, long offset, int length, Location diagnosticLocation)
    {
        Name = name;
        FilePath = filePath;
        Namespace = ns;
        Modifiers = modifiers;
        ContainingTypes = new List<ContainingType>();
        Offset = offset;
        Length = length;
        DiagnosticLocation = diagnosticLocation;
    }
}

internal readonly record struct ContainingType(string Name, string Keyword)
{
    public ContainingType(TypeDeclarationSyntax typeDec) : this(
        Name: $"{typeDec.Identifier}{typeDec.TypeParameterList}",
        Keyword: typeDec is RecordDeclarationSyntax rds ? $"{typeDec.Keyword.ValueText} {rds.ClassOrStructKeyword}" : typeDec.Keyword.ValueText)
    { }
}