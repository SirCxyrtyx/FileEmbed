using System;
using Microsoft.CodeAnalysis;

namespace FileEmbed;

internal static class Diagnostics
{
    private const string DIAGNOSTIC_CATEGORY = "FileEmbed";
    private const string DIAGNOSTIC_TITLE = $"Invalid {FileEmbedGenerator.FileEmbedAttributeName} usage";

    private static string DiagnosticId(int id) => DIAGNOSTIC_CATEGORY + id.ToString("D2");

    public static Diagnostic InvalidFileEmbedAttribute(Location location) => Diagnostic.Create(InvalidFileEmbedAttributeDescriptor, location);

    private static readonly DiagnosticDescriptor InvalidFileEmbedAttributeDescriptor = new(
        DiagnosticId(1),
        DIAGNOSTIC_TITLE,
        $"The {FileEmbedGenerator.FileEmbedAttributeName} is malformed.",
        DIAGNOSTIC_CATEGORY,
        DiagnosticSeverity.Error,
        true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static Diagnostic MultipleFileEmbedAttributes(Location location) => Diagnostic.Create(MultipleFileEmbedAttributesDescriptor, location);

    private static readonly DiagnosticDescriptor MultipleFileEmbedAttributesDescriptor = new(
        DiagnosticId(2),
        DIAGNOSTIC_TITLE,
        $"Multiple {FileEmbedGenerator.FileEmbedAttributeName}s were applied to the same method, but only one is allowed.",
        DIAGNOSTIC_CATEGORY,
        DiagnosticSeverity.Error,
        true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static Diagnostic InvalidArgument(Location location, string argName, object? argValue) => Diagnostic.Create(InvalidArgumentDescriptor, location, argName, argValue ?? "null");

    private static readonly DiagnosticDescriptor InvalidArgumentDescriptor = new(
        DiagnosticId(3),
        DIAGNOSTIC_TITLE,
        "Invalid '{0}' value: '{1}'.",
        DIAGNOSTIC_CATEGORY,
        DiagnosticSeverity.Error,
        true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static Diagnostic MissingArgument(Location location, string argName) => Diagnostic.Create(MissingArgumentDescriptor, location, argName);

    private static readonly DiagnosticDescriptor MissingArgumentDescriptor = new(
        DiagnosticId(4),
        DIAGNOSTIC_TITLE,
        "Missing '{0}' argument.",
        DIAGNOSTIC_CATEGORY,
        DiagnosticSeverity.Error,
        true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static Diagnostic FileNotFound(Location location, string filePath) => Diagnostic.Create(FileNotFoundDescriptor, location, filePath);

    private static readonly DiagnosticDescriptor FileNotFoundDescriptor = new(
        DiagnosticId(5),
        DIAGNOSTIC_TITLE,
        "File not found: '{0}'.",
        DIAGNOSTIC_CATEGORY,
        DiagnosticSeverity.Error,
        true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static Diagnostic SourceGeneratorException(Location location, Exception exception) => Diagnostic.Create(SourceGeneratorExceptionDescriptor, location, exception.GetType().FullName, exception.Message);

    private static readonly DiagnosticDescriptor SourceGeneratorExceptionDescriptor = new(
        DiagnosticId(6),
        DIAGNOSTIC_TITLE,
        "Internal SourceGenerator error: {0}: {1}.",
        DIAGNOSTIC_CATEGORY,
        DiagnosticSeverity.Error,
        true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static Diagnostic OffsetPastEnd(Location location, long offset, long fileLength) => Diagnostic.Create(OffsetPastEndDescriptor, location, offset, fileLength);

    private static readonly DiagnosticDescriptor OffsetPastEndDescriptor = new(
        DiagnosticId(7),
        DIAGNOSTIC_TITLE,
        "Offset must be less than the length of the file. Offset is '{0}', file length is '{1}'.",
        DIAGNOSTIC_CATEGORY,
        DiagnosticSeverity.Error,
        true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static Diagnostic OffsetPlusLengthPastEnd(Location location, long offsetPlusLength, long fileLength) => Diagnostic.Create(OffsetPlusLengthPastEndDescriptor, location, offsetPlusLength, fileLength);

    private static readonly DiagnosticDescriptor OffsetPlusLengthPastEndDescriptor = new(
        DiagnosticId(8),
        DIAGNOSTIC_TITLE,
        "Offset plus Length must be less than or equal to the length of the file. Offset plus Length is '{0}', file length is '{1}'.",
        DIAGNOSTIC_CATEGORY,
        DiagnosticSeverity.Error,
        true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static Diagnostic InvalidSignature(Location location) => Diagnostic.Create(InvalidSignatureDescriptor, location);

    private static readonly DiagnosticDescriptor InvalidSignatureDescriptor = new(
        DiagnosticId(9),
        DIAGNOSTIC_TITLE,
        $"{FileEmbedGenerator.FileEmbedAttributeName} method must be static, partial, parameterless, non-generic, non-abstract, and return ReadOnlySpan<byte>.",
        DIAGNOSTIC_CATEGORY,
        DiagnosticSeverity.Error,
        true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static Diagnostic CouldNotFindAdditionalFile(Location location, string path) => Diagnostic.Create(CouldNotFindAdditionalFileDescriptor, location, path);

    private static readonly DiagnosticDescriptor CouldNotFindAdditionalFileDescriptor = new(
        DiagnosticId(10),
        DIAGNOSTIC_TITLE,
        "No AdditionalFile found with path ending in: '{0}'",
        DIAGNOSTIC_CATEGORY,
        DiagnosticSeverity.Error,
        true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static Diagnostic ExceedsMaxLength(Location location, long length, int maxLength) => Diagnostic.Create(ExceedsMaxLengthDescriptor, location, length, maxLength);

    private static readonly DiagnosticDescriptor ExceedsMaxLengthDescriptor = new(
        DiagnosticId(11),
        DIAGNOSTIC_TITLE,
        "Length of '{0}' exceeds max allowed length of '{1}' bytes. See documentation for how to change this limit.",
        DIAGNOSTIC_CATEGORY,
        DiagnosticSeverity.Error,
        true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static Diagnostic NoProjectDir(Location location) => Diagnostic.Create(NoProjectDirDescriptor, location);

    private static readonly DiagnosticDescriptor NoProjectDirDescriptor = new(
        DiagnosticId(12),
        DIAGNOSTIC_TITLE,
        "'ProjectDir' MSBuild property not found.",
        DIAGNOSTIC_CATEGORY,
        DiagnosticSeverity.Error,
        true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static Diagnostic InvalidMaxLength(Location location, string value) => Diagnostic.Create(InvalidMaxLengthDescriptor, location, value);

    private static readonly DiagnosticDescriptor InvalidMaxLengthDescriptor = new(
        DiagnosticId(13),
        DIAGNOSTIC_TITLE,
        $"Invalid value for '{FileEmbedGenerator.MaxEmbedSizeBuildProperty}' MSBuild property: '{{0}}'. Value must be an integer in the range 0 - {int.MaxValue}.",
        DIAGNOSTIC_CATEGORY,
        DiagnosticSeverity.Error,
        true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);
}