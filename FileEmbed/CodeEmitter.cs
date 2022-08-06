using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace FileEmbed;

internal static class CodeEmitter
{
    private const int DEFAULT_MAXIMUM_GENERATED_SPAN_LENGTH = 1048576; //1mb

    private static readonly string GeneratedCodeAttribute = $"[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"{typeof(Diagnostics).Assembly.GetName().Name}\", \"{typeof(Diagnostics).Assembly.GetName().Version}\")]";

    public static void EmitAttribute(IncrementalGeneratorPostInitializationContext initializationContext)
    {
        const string attributeFileName = $@"{FileEmbedGenerator.FileEmbedAttributeName}.g.cs";
        string attributeSource = 
$@"namespace {FileEmbedGenerator.FileEmbedNamespaceName}
{{
    {GeneratedCodeAttribute}
    [global::System.Diagnostics.Conditional(""FILE_EMBED_KEEP_SOURCE_GENERATOR_ATTRIBUTE"")]
    [global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)]
    internal class {FileEmbedGenerator.FileEmbedAttributeName} : global::System.Attribute
    {{
        public string FilePath {{ get; }}

        public long Offset {{ get; }}

        public int Length {{ get; }}

        public {FileEmbedGenerator.FileEmbedAttributeName}(string filePath) : this(filePath, 0, -1) {{ }}

        public {FileEmbedGenerator.FileEmbedAttributeName}(string filePath, long offset) : this(filePath, offset, -1) {{ }}

        public {FileEmbedGenerator.FileEmbedAttributeName}(string filePath, long offset, int length)
        {{
            FilePath = filePath;
            Offset = offset;
            Length = length;
        }}
    }}
}}";
        initializationContext.AddSource(attributeFileName, attributeSource);
    }

    public static void EmitEmbedMethods(List<EmbedMethodGenerationData> methodsToGenerate, SourceProductionContext context)
    {
        int id = 0;
        var nameSet = new HashSet<string>();
        foreach (EmbedMethodGenerationData embedMethod in methodsToGenerate)
        {
            try
            {
                string filePath = embedMethod.FilePath;
                if (!File.Exists(filePath))
                {
                    context.ReportDiagnostic(Diagnostics.FileNotFound(embedMethod.DiagnosticLocation, filePath));
                    continue;
                }
                long fileLength = new FileInfo(filePath).Length;
                if (embedMethod.Offset >= fileLength)
                {
                    context.ReportDiagnostic(Diagnostics.OffsetPastEnd(embedMethod.DiagnosticLocation, embedMethod.Offset, fileLength));
                    continue;
                }

                int maximumGeneratedSpanLength = embedMethod.CustomMaxEmbedSize < 0 ? DEFAULT_MAXIMUM_GENERATED_SPAN_LENGTH : embedMethod.CustomMaxEmbedSize;
                if (embedMethod.Length == -1)
                {
                    long calculatedLength = fileLength - embedMethod.Offset;
                    if (calculatedLength > maximumGeneratedSpanLength)
                    {
                        context.ReportDiagnostic(Diagnostics.ExceedsMaxLength(embedMethod.DiagnosticLocation, calculatedLength, maximumGeneratedSpanLength));
                        continue;
                    }
                }
                else
                {
                    if (embedMethod.Offset + embedMethod.Length > fileLength)
                    {
                        context.ReportDiagnostic(Diagnostics.OffsetPlusLengthPastEnd(embedMethod.DiagnosticLocation, embedMethod.Offset + embedMethod.Length, fileLength));
                        continue;
                    }
                    if (embedMethod.Length > maximumGeneratedSpanLength)
                    {
                        context.ReportDiagnostic(Diagnostics.ExceedsMaxLength(embedMethod.DiagnosticLocation, embedMethod.Length, maximumGeneratedSpanLength));
                        continue;
                    }
                }
                string fileName = Path.GetFileName(filePath);
                if (nameSet.Contains(fileName))
                {
                    fileName = $"{fileName}_{id++}";
                }
                else
                {
                    nameSet.Add(fileName);
                }
                SourceText generatedCode = GenerateCode(filePath, embedMethod);
                context.AddSource($"EMBEDDED__{fileName}.g.cs", generatedCode);
            }
            catch (Exception e)
            {
                context.ReportDiagnostic(Diagnostics.SourceGeneratorException(embedMethod.DiagnosticLocation, e));
            }
        }
    }

    
    private static SourceText GenerateCode(string filePath, EmbedMethodGenerationData embedMethod)
    {

        using FileStream fs = File.OpenRead(filePath);
        fs.Seek(embedMethod.Offset, SeekOrigin.Begin);
        int length = embedMethod.Length;
        if (length == -1)
        {
            length = (int)(fs.Length - fs.Position);
        }

        var sb = new StringBuilder(512);

        int indentLevel = 0;
        if (embedMethod.Namespace is not null)
        {
            sb.Append($"namespace {embedMethod.Namespace}\n");
            sb.Append("{\n");
            indentLevel++;
        }

        for (int i = embedMethod.ContainingTypes.Count - 1; i >= 0; i--)
        {
            sb.Append('\t', indentLevel);
            sb.Append($"partial {embedMethod.ContainingTypes[i].Keyword} {embedMethod.ContainingTypes[i].Name}\n");
            sb.Append('\t', indentLevel);
            sb.Append("{\n");
            indentLevel++;
        }

        sb.Append('\t', indentLevel);
        sb.Append(GeneratedCodeAttribute);
        sb.Append('\n');
        sb.Append('\t', indentLevel);
        sb.Append($"{embedMethod.Modifiers} global::System.ReadOnlySpan<byte> {embedMethod.Name}() => ");

        if (embedMethod.CanUseUtf8Literal && TryGenerateCodeWithUtf8Literal(length, fs, sb, indentLevel, out SourceText? utf8LiteralSourceText))
        {
            return utf8LiteralSourceText!;
        }

        if (!BitConverter.IsLittleEndian //fancy algorithm does not work on BE systems
            || length <= 1024) //not worth dealing with this in the complex algorithm
        {
            return GenerateCodeWithStringBuilder(length, fs, sb, indentLevel);
        }
        return GenerateCodeWithPointers(embedMethod, length, fs, sb, indentLevel);
    }

    private static SourceText GenerateCodeWithStringBuilder(int length, FileStream fs, StringBuilder sb, int indentLevel)
    {
        sb.Append("new byte[] { ");

        const int bufferSize = 1024;
        byte[] buff = new byte[bufferSize];
        while (length > 0)
        {
            int readLength = length < bufferSize ? length : bufferSize;
            if (fs.Read(buff, 0, readLength) < readLength)
            {
                throw new EndOfStreamException($"File ended unexpectedly: {fs.Name} at position {fs.Position}");
            }
            for (int i = 0; i < readLength; i++)
            {
                byte b = buff[i];
                sb.Append(b);
                sb.Append(',');
            }
            length -= readLength;
        }

        sb.Append(" };\n");
        while (indentLevel-- > 0)
        {
            sb.Append('\t', indentLevel);
            sb.Append("}\n");
        }

        return SourceText.From(sb.ToString(), Encoding.UTF8);
    }

    private static bool TryGenerateCodeWithUtf8Literal(int length, FileStream fs, StringBuilder sb, int indentLevel, out SourceText? sourceText)
    {
        long originalFileStreamPosition = fs.Position;
        int originalStringBuilderLength = sb.Length;

        sb.Append('"');

        const int bufferSize = 1024;
        byte[] buff = new byte[bufferSize];
        while (length > 0)
        {
            int readLength = length < bufferSize ? length : bufferSize;
            if (fs.Read(buff, 0, readLength) < readLength)
            {
                throw new EndOfStreamException($"File ended unexpectedly: {fs.Name} at position {fs.Position}");
            }
            //if this is a binary file, this will probably return false the first time, so repetition will be minimal
            if (!Utf8LiteralConverter.TryConvertToUtf8String(sb, buff.AsSpan(0, readLength)))
            {
                sourceText = null;
                fs.Position = originalFileStreamPosition;
                sb.Length = originalStringBuilderLength;
                return false;
            }
            length -= readLength;
        }

        sb.Append("\"u8;\n");
        while (indentLevel-- > 0)
        {
            sb.Append('\t', indentLevel);
            sb.Append("}\n");
        }

        sourceText = SourceText.From(sb.ToString(), Encoding.UTF8);
        return true;
    }

    private static unsafe SourceText GenerateCodeWithPointers(EmbedMethodGenerationData embedMethod, int length, FileStream fs, StringBuilder sb, int indentLevel)
    {
        //This may look horrifically complicated, but it's almost 4x faster than the StringBuilder method!

        const int maxCharsPerByte = 4; //each byte can take up a max of 4 chars, eg: "255,"
        //Capacity CANNOT be smaller than the amount required, as we never resize the chunks array
        int capacity = 256 //base char amount
                       + embedMethod.ContainingTypes.Count * 128 //every type will need more nesting
                       + length * maxCharsPerByte;

        int numChunks = 1 + capacity / SmallObjectHeapChunkReader.ChunkSize;
        char[][] chunks = new char[numChunks][];
        int curChunkIdx = 0;
        chunks[0] = new char[SmallObjectHeapChunkReader.ChunkSize];

        sb.Append("new byte[] { ");
        sb.CopyTo(0, chunks[0], 0, sb.Length);
        int positionWithinChunk = sb.Length;

        const int bufferSize = 1024;
        byte[] buff = new byte[bufferSize];
        int finalStringLength = 0;

        //an explanation for how this algorithm works is on a comment above ByteStrLookup
        ReadOnlySpan<ulong> lookup = MemoryMarshal.Cast<byte, ulong>(ByteStrLookup);
        while (true)
        {
            fixed (char* cP = &chunks[curChunkIdx][0])
            {
                //we always want to write a newline at the end of each chunk.
                //since we write 2-4 chars at a time, this will gaurantee at least one char remains for that,
                //as well as make sure we don't write off the end of the array (since we write an 8-byte ulong for every byte)
                char* _4charsFromEnd = cP + SmallObjectHeapChunkReader.ChunkSize - maxCharsPerByte;

                //on the first chunk, some amount has already been written.
                char* position = curChunkIdx == 0 ? cP + positionWithinChunk : cP;

                while (position < _4charsFromEnd)
                {
                    int readLength = Math.Min(length, bufferSize);
                    if (fs.Read(buff, 0, readLength) < readLength)
                    {
                        throw new EndOfStreamException($"File ended unexpectedly: {fs.Name} at position {fs.Position}");
                    }
                    int i = 0;
                    for (; i < readLength && position < _4charsFromEnd; i++)
                    {
                        ulong val = lookup[buff[i]];
                        Unsafe.WriteUnaligned(position, val);
                        position += maxCharsPerByte - (val >> 56);
                    }
                    length -= i;
                    if (i < readLength)
                    {
                        fs.Seek(i - readLength, SeekOrigin.Current);
                    }
                    if (length == 0)
                    {
                        positionWithinChunk = (int)(position - cP);
                        finalStringLength += positionWithinChunk;
                        goto EndOfBytes;
                    }
                }
                //pad out the last few chars with spaces and end it with a newline
                while (position < _4charsFromEnd + (maxCharsPerByte - 1))
                {
                    *position++ = ' ';
                }
                *position++ = '\n';
                finalStringLength += (int)(position - cP);
            }
            curChunkIdx++;
            chunks[curChunkIdx] = new char[SmallObjectHeapChunkReader.ChunkSize];
        }
    EndOfBytes:
        int charsRemainingInChunk = SmallObjectHeapChunkReader.ChunkSize - positionWithinChunk;
        int charsRemainingToWrite = 4 // " };\n" 
                                    + 2 * indentLevel; // "}\n"
        //I'm sure there's some math trick to calculate this without a loop, but I can't think of it right now.
        for (int i = indentLevel - 1; i > 0; i--)
        {
            charsRemainingToWrite += i;
        }

        finalStringLength += charsRemainingToWrite;

        sb.Length = 0;
        sb.Append(" };\n");
        while (indentLevel-- > 0)
        {
            sb.Append('\t', indentLevel);
            sb.Append("}\n");
        }
        if (charsRemainingToWrite > charsRemainingInChunk)
        {
            sb.CopyTo(0, chunks[curChunkIdx], positionWithinChunk, charsRemainingInChunk);
            charsRemainingToWrite -= charsRemainingInChunk;
            curChunkIdx++;
            chunks[curChunkIdx] = new char[SmallObjectHeapChunkReader.ChunkSize];
            sb.CopyTo(charsRemainingInChunk, chunks[curChunkIdx], 0, charsRemainingToWrite);
        }
        else
        {
            sb.CopyTo(0, chunks[curChunkIdx], positionWithinChunk, charsRemainingToWrite);
        }

        var reader = new SmallObjectHeapChunkReader(chunks, finalStringLength);
        return SourceText.From(reader, finalStringLength, Encoding.UTF8);
    }

    /* This is written as a byte array solely to take advantage of the ReadOnlySpan<byte> embedding optimization. It is actually an array of (little endian) chars.
     * Each 4 chars correspond to the string representation of a byte, followed by a comma, then padded with \0s if neccesary (for 0-99).
     * So for example, the 4 char string for 53 is "53,\0", and for 168 it's "168,"
     * The second byte of the last char in each string is repurposed to hold the length of the non-null portion of the string, represented as (4 - length).
     * In the strings of length 2 and 3, the last char is unused, so writing 2 and 1, respectively, is not trampling over any data. And for strings of length 4, the last char is a comma, which has a second byte of 0.
     *
     * The purpose of all this is to create a branchless inner loop for an algorithm that turns a byte array into its string representation.
     * If we reinterpret this as a ReadOnlySpan<ulong>, then we can directly index it with a byte value to get a ulong containing that byte's string representation + comma.
     * Write the ulong to the current offset in a char array, then shift the ulong right by 56 bits and subtract that from 4 to get the number of chars that was just written.
     * Advance the char array offset by that much, and the next write will overwrite any extraneous data.
     */
    private static ReadOnlySpan<byte> ByteStrLookup => new byte[2048]
    {
            48, 0, 44, 0, 0, 0, 0, 2, 49, 0, 44, 0, 0, 0, 0, 2, 50, 0, 44, 0, 0, 0, 0, 2, 51, 0, 44, 0, 0, 0, 0, 2, 52, 0, 44, 0, 0, 0, 0, 2, 53, 0, 44, 0, 0, 0, 0, 2, 54, 0, 44, 0, 0, 0, 0, 2, 55, 0,
            44, 0, 0, 0, 0, 2, 56, 0, 44, 0, 0, 0, 0, 2, 57, 0, 44, 0, 0, 0, 0, 2, 49, 0, 48, 0, 44, 0, 0, 1, 49, 0, 49, 0, 44, 0, 0, 1, 49, 0, 50, 0, 44, 0, 0, 1, 49, 0, 51, 0, 44, 0, 0, 1, 49, 0,
            52, 0, 44, 0, 0, 1, 49, 0, 53, 0, 44, 0, 0, 1, 49, 0, 54, 0, 44, 0, 0, 1, 49, 0, 55, 0, 44, 0, 0, 1, 49, 0, 56, 0, 44, 0, 0, 1, 49, 0, 57, 0, 44, 0, 0, 1, 50, 0, 48, 0, 44, 0, 0, 1, 50, 0,
            49, 0, 44, 0, 0, 1, 50, 0, 50, 0, 44, 0, 0, 1, 50, 0, 51, 0, 44, 0, 0, 1, 50, 0, 52, 0, 44, 0, 0, 1, 50, 0, 53, 0, 44, 0, 0, 1, 50, 0, 54, 0, 44, 0, 0, 1, 50, 0, 55, 0, 44, 0, 0, 1, 50, 0,
            56, 0, 44, 0, 0, 1, 50, 0, 57, 0, 44, 0, 0, 1, 51, 0, 48, 0, 44, 0, 0, 1, 51, 0, 49, 0, 44, 0, 0, 1, 51, 0, 50, 0, 44, 0, 0, 1, 51, 0, 51, 0, 44, 0, 0, 1, 51, 0, 52, 0, 44, 0, 0, 1, 51, 0,
            53, 0, 44, 0, 0, 1, 51, 0, 54, 0, 44, 0, 0, 1, 51, 0, 55, 0, 44, 0, 0, 1, 51, 0, 56, 0, 44, 0, 0, 1, 51, 0, 57, 0, 44, 0, 0, 1, 52, 0, 48, 0, 44, 0, 0, 1, 52, 0, 49, 0, 44, 0, 0, 1, 52, 0,
            50, 0, 44, 0, 0, 1, 52, 0, 51, 0, 44, 0, 0, 1, 52, 0, 52, 0, 44, 0, 0, 1, 52, 0, 53, 0, 44, 0, 0, 1, 52, 0, 54, 0, 44, 0, 0, 1, 52, 0, 55, 0, 44, 0, 0, 1, 52, 0, 56, 0, 44, 0, 0, 1, 52, 0,
            57, 0, 44, 0, 0, 1, 53, 0, 48, 0, 44, 0, 0, 1, 53, 0, 49, 0, 44, 0, 0, 1, 53, 0, 50, 0, 44, 0, 0, 1, 53, 0, 51, 0, 44, 0, 0, 1, 53, 0, 52, 0, 44, 0, 0, 1, 53, 0, 53, 0, 44, 0, 0, 1, 53, 0,
            54, 0, 44, 0, 0, 1, 53, 0, 55, 0, 44, 0, 0, 1, 53, 0, 56, 0, 44, 0, 0, 1, 53, 0, 57, 0, 44, 0, 0, 1, 54, 0, 48, 0, 44, 0, 0, 1, 54, 0, 49, 0, 44, 0, 0, 1, 54, 0, 50, 0, 44, 0, 0, 1, 54, 0,
            51, 0, 44, 0, 0, 1, 54, 0, 52, 0, 44, 0, 0, 1, 54, 0, 53, 0, 44, 0, 0, 1, 54, 0, 54, 0, 44, 0, 0, 1, 54, 0, 55, 0, 44, 0, 0, 1, 54, 0, 56, 0, 44, 0, 0, 1, 54, 0, 57, 0, 44, 0, 0, 1, 55, 0,
            48, 0, 44, 0, 0, 1, 55, 0, 49, 0, 44, 0, 0, 1, 55, 0, 50, 0, 44, 0, 0, 1, 55, 0, 51, 0, 44, 0, 0, 1, 55, 0, 52, 0, 44, 0, 0, 1, 55, 0, 53, 0, 44, 0, 0, 1, 55, 0, 54, 0, 44, 0, 0, 1, 55, 0,
            55, 0, 44, 0, 0, 1, 55, 0, 56, 0, 44, 0, 0, 1, 55, 0, 57, 0, 44, 0, 0, 1, 56, 0, 48, 0, 44, 0, 0, 1, 56, 0, 49, 0, 44, 0, 0, 1, 56, 0, 50, 0, 44, 0, 0, 1, 56, 0, 51, 0, 44, 0, 0, 1, 56, 0,
            52, 0, 44, 0, 0, 1, 56, 0, 53, 0, 44, 0, 0, 1, 56, 0, 54, 0, 44, 0, 0, 1, 56, 0, 55, 0, 44, 0, 0, 1, 56, 0, 56, 0, 44, 0, 0, 1, 56, 0, 57, 0, 44, 0, 0, 1, 57, 0, 48, 0, 44, 0, 0, 1, 57, 0,
            49, 0, 44, 0, 0, 1, 57, 0, 50, 0, 44, 0, 0, 1, 57, 0, 51, 0, 44, 0, 0, 1, 57, 0, 52, 0, 44, 0, 0, 1, 57, 0, 53, 0, 44, 0, 0, 1, 57, 0, 54, 0, 44, 0, 0, 1, 57, 0, 55, 0, 44, 0, 0, 1, 57, 0,
            56, 0, 44, 0, 0, 1, 57, 0, 57, 0, 44, 0, 0, 1, 49, 0, 48, 0, 48, 0, 44, 0, 49, 0, 48, 0, 49, 0, 44, 0, 49, 0, 48, 0, 50, 0, 44, 0, 49, 0, 48, 0, 51, 0, 44, 0, 49, 0, 48, 0, 52, 0, 44, 0,
            49, 0, 48, 0, 53, 0, 44, 0, 49, 0, 48, 0, 54, 0, 44, 0, 49, 0, 48, 0, 55, 0, 44, 0, 49, 0, 48, 0, 56, 0, 44, 0, 49, 0, 48, 0, 57, 0, 44, 0, 49, 0, 49, 0, 48, 0, 44, 0, 49, 0, 49, 0, 49, 0,
            44, 0, 49, 0, 49, 0, 50, 0, 44, 0, 49, 0, 49, 0, 51, 0, 44, 0, 49, 0, 49, 0, 52, 0, 44, 0, 49, 0, 49, 0, 53, 0, 44, 0, 49, 0, 49, 0, 54, 0, 44, 0, 49, 0, 49, 0, 55, 0, 44, 0, 49, 0, 49, 0,
            56, 0, 44, 0, 49, 0, 49, 0, 57, 0, 44, 0, 49, 0, 50, 0, 48, 0, 44, 0, 49, 0, 50, 0, 49, 0, 44, 0, 49, 0, 50, 0, 50, 0, 44, 0, 49, 0, 50, 0, 51, 0, 44, 0, 49, 0, 50, 0, 52, 0, 44, 0, 49, 0,
            50, 0, 53, 0, 44, 0, 49, 0, 50, 0, 54, 0, 44, 0, 49, 0, 50, 0, 55, 0, 44, 0, 49, 0, 50, 0, 56, 0, 44, 0, 49, 0, 50, 0, 57, 0, 44, 0, 49, 0, 51, 0, 48, 0, 44, 0, 49, 0, 51, 0, 49, 0, 44, 0,
            49, 0, 51, 0, 50, 0, 44, 0, 49, 0, 51, 0, 51, 0, 44, 0, 49, 0, 51, 0, 52, 0, 44, 0, 49, 0, 51, 0, 53, 0, 44, 0, 49, 0, 51, 0, 54, 0, 44, 0, 49, 0, 51, 0, 55, 0, 44, 0, 49, 0, 51, 0, 56, 0,
            44, 0, 49, 0, 51, 0, 57, 0, 44, 0, 49, 0, 52, 0, 48, 0, 44, 0, 49, 0, 52, 0, 49, 0, 44, 0, 49, 0, 52, 0, 50, 0, 44, 0, 49, 0, 52, 0, 51, 0, 44, 0, 49, 0, 52, 0, 52, 0, 44, 0, 49, 0, 52, 0,
            53, 0, 44, 0, 49, 0, 52, 0, 54, 0, 44, 0, 49, 0, 52, 0, 55, 0, 44, 0, 49, 0, 52, 0, 56, 0, 44, 0, 49, 0, 52, 0, 57, 0, 44, 0, 49, 0, 53, 0, 48, 0, 44, 0, 49, 0, 53, 0, 49, 0, 44, 0, 49, 0,
            53, 0, 50, 0, 44, 0, 49, 0, 53, 0, 51, 0, 44, 0, 49, 0, 53, 0, 52, 0, 44, 0, 49, 0, 53, 0, 53, 0, 44, 0, 49, 0, 53, 0, 54, 0, 44, 0, 49, 0, 53, 0, 55, 0, 44, 0, 49, 0, 53, 0, 56, 0, 44, 0,
            49, 0, 53, 0, 57, 0, 44, 0, 49, 0, 54, 0, 48, 0, 44, 0, 49, 0, 54, 0, 49, 0, 44, 0, 49, 0, 54, 0, 50, 0, 44, 0, 49, 0, 54, 0, 51, 0, 44, 0, 49, 0, 54, 0, 52, 0, 44, 0, 49, 0, 54, 0, 53, 0,
            44, 0, 49, 0, 54, 0, 54, 0, 44, 0, 49, 0, 54, 0, 55, 0, 44, 0, 49, 0, 54, 0, 56, 0, 44, 0, 49, 0, 54, 0, 57, 0, 44, 0, 49, 0, 55, 0, 48, 0, 44, 0, 49, 0, 55, 0, 49, 0, 44, 0, 49, 0, 55, 0,
            50, 0, 44, 0, 49, 0, 55, 0, 51, 0, 44, 0, 49, 0, 55, 0, 52, 0, 44, 0, 49, 0, 55, 0, 53, 0, 44, 0, 49, 0, 55, 0, 54, 0, 44, 0, 49, 0, 55, 0, 55, 0, 44, 0, 49, 0, 55, 0, 56, 0, 44, 0, 49, 0,
            55, 0, 57, 0, 44, 0, 49, 0, 56, 0, 48, 0, 44, 0, 49, 0, 56, 0, 49, 0, 44, 0, 49, 0, 56, 0, 50, 0, 44, 0, 49, 0, 56, 0, 51, 0, 44, 0, 49, 0, 56, 0, 52, 0, 44, 0, 49, 0, 56, 0, 53, 0, 44, 0,
            49, 0, 56, 0, 54, 0, 44, 0, 49, 0, 56, 0, 55, 0, 44, 0, 49, 0, 56, 0, 56, 0, 44, 0, 49, 0, 56, 0, 57, 0, 44, 0, 49, 0, 57, 0, 48, 0, 44, 0, 49, 0, 57, 0, 49, 0, 44, 0, 49, 0, 57, 0, 50, 0,
            44, 0, 49, 0, 57, 0, 51, 0, 44, 0, 49, 0, 57, 0, 52, 0, 44, 0, 49, 0, 57, 0, 53, 0, 44, 0, 49, 0, 57, 0, 54, 0, 44, 0, 49, 0, 57, 0, 55, 0, 44, 0, 49, 0, 57, 0, 56, 0, 44, 0, 49, 0, 57, 0,
            57, 0, 44, 0, 50, 0, 48, 0, 48, 0, 44, 0, 50, 0, 48, 0, 49, 0, 44, 0, 50, 0, 48, 0, 50, 0, 44, 0, 50, 0, 48, 0, 51, 0, 44, 0, 50, 0, 48, 0, 52, 0, 44, 0, 50, 0, 48, 0, 53, 0, 44, 0, 50, 0,
            48, 0, 54, 0, 44, 0, 50, 0, 48, 0, 55, 0, 44, 0, 50, 0, 48, 0, 56, 0, 44, 0, 50, 0, 48, 0, 57, 0, 44, 0, 50, 0, 49, 0, 48, 0, 44, 0, 50, 0, 49, 0, 49, 0, 44, 0, 50, 0, 49, 0, 50, 0, 44, 0,
            50, 0, 49, 0, 51, 0, 44, 0, 50, 0, 49, 0, 52, 0, 44, 0, 50, 0, 49, 0, 53, 0, 44, 0, 50, 0, 49, 0, 54, 0, 44, 0, 50, 0, 49, 0, 55, 0, 44, 0, 50, 0, 49, 0, 56, 0, 44, 0, 50, 0, 49, 0, 57, 0,
            44, 0, 50, 0, 50, 0, 48, 0, 44, 0, 50, 0, 50, 0, 49, 0, 44, 0, 50, 0, 50, 0, 50, 0, 44, 0, 50, 0, 50, 0, 51, 0, 44, 0, 50, 0, 50, 0, 52, 0, 44, 0, 50, 0, 50, 0, 53, 0, 44, 0, 50, 0, 50, 0,
            54, 0, 44, 0, 50, 0, 50, 0, 55, 0, 44, 0, 50, 0, 50, 0, 56, 0, 44, 0, 50, 0, 50, 0, 57, 0, 44, 0, 50, 0, 51, 0, 48, 0, 44, 0, 50, 0, 51, 0, 49, 0, 44, 0, 50, 0, 51, 0, 50, 0, 44, 0, 50, 0,
            51, 0, 51, 0, 44, 0, 50, 0, 51, 0, 52, 0, 44, 0, 50, 0, 51, 0, 53, 0, 44, 0, 50, 0, 51, 0, 54, 0, 44, 0, 50, 0, 51, 0, 55, 0, 44, 0, 50, 0, 51, 0, 56, 0, 44, 0, 50, 0, 51, 0, 57, 0, 44, 0,
            50, 0, 52, 0, 48, 0, 44, 0, 50, 0, 52, 0, 49, 0, 44, 0, 50, 0, 52, 0, 50, 0, 44, 0, 50, 0, 52, 0, 51, 0, 44, 0, 50, 0, 52, 0, 52, 0, 44, 0, 50, 0, 52, 0, 53, 0, 44, 0, 50, 0, 52, 0, 54, 0,
            44, 0, 50, 0, 52, 0, 55, 0, 44, 0, 50, 0, 52, 0, 56, 0, 44, 0, 50, 0, 52, 0, 57, 0, 44, 0, 50, 0, 53, 0, 48, 0, 44, 0, 50, 0, 53, 0, 49, 0, 44, 0, 50, 0, 53, 0, 50, 0, 44, 0, 50, 0, 53, 0,
            51, 0, 44, 0, 50, 0, 53, 0, 52, 0, 44, 0, 50, 0, 53, 0, 53, 0, 44, 0
    };

    //code to generate the ByteStrLookup (retained here just in case it needs to be modified)
    //public static string GenerateByteStrLookup()
    //{
    //    Span<ulong> ulongSpan = stackalloc ulong[256];
    //    ulongSpan.Clear();

    //    for (int i = 0; i < 256; i++)
    //    {
    //        int digits = i < 10 ? 1 : i < 100 ? 2 : 3;
    //        Span<char> chars = MemoryMarshal.Cast<ulong, char>(ulongSpan.Slice(i));
    //        int value = i;
    //        int p = digits;
    //        do
    //        {
    //            value = Math.DivRem(value, 10, out int rem);
    //            chars[--p] = (char)(rem + '0');
    //        }
    //        while (p > 0);
    //        chars[digits] = ',';
    //        MemoryMarshal.AsBytes(chars)[7] = (byte)(4 - (digits + 1));
    //    }
    //    var sb = new StringBuilder();
    //    foreach (byte b in MemoryMarshal.AsBytes(ulongSpan))
    //    {
    //        sb.Append(b);
    //        sb.Append(',');
    //    }
    //    return sb.ToString();
    //}

    //Designed to be easily copied into a Microsoft.CodeAnalysis.Text.LargeText by the SourceText.From method
    //Will put nothing on the large object heap, unless the source file is larger than 108mb,
    //in which case the char[][] itself will end up there (on a 64-bit system)
    private sealed class SmallObjectHeapChunkReader : TextReader
    {
        internal const int ChunkSize = 40 * 1024; //char array of this size will be 80kb, less than the LOH threshold of 85kb
        private readonly int Length;
        private readonly char[][] Chunks;
        
        private int curChunk;
        private int positionWithinChunk;

        private int Position => curChunk * ChunkSize + positionWithinChunk;

        //char arrays in chunks MUST be exactly ChunkSize in length!
        public SmallObjectHeapChunkReader(char[][] chunks, int length)
        {
            Chunks = chunks;
            Length = length;
        }

        public override int Peek()
        {
            if (Position >= Length)
                return -1;
            return Chunks[curChunk][positionWithinChunk];
        }

        public override int Read()
        {
            if (Position >= Length)
                return -1;
            char c = Chunks[curChunk][positionWithinChunk];
            if (positionWithinChunk == ChunkSize - 1)
            {
                positionWithinChunk = 0;
                curChunk++;
            }
            else
            {
                positionWithinChunk++;
            }
            return c;
        }

        public override int Read(char[] buffer, int index, int count)
        {
            if (buffer is null)
                throw new ArgumentNullException(nameof(buffer));

            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (index + count > buffer.Length)
                throw new ArgumentException();

            int length = Math.Min(count, Length - Position);

            int remainingLength = length;
            while (remainingLength > 0)
            {
                int lengthInChunk = Math.Min(ChunkSize - positionWithinChunk, remainingLength);
                Array.Copy(Chunks[curChunk], positionWithinChunk, buffer, index, lengthInChunk);
                index += lengthInChunk;
                positionWithinChunk += lengthInChunk;
                remainingLength -= lengthInChunk;
                if (positionWithinChunk == ChunkSize)
                {
                    positionWithinChunk = 0;
                    curChunk++;
                }
            }

            return length;
        }

        public override int ReadBlock(char[] buffer, int index, int count) =>
            Read(buffer, index, count);

//just in case source generators ever break free from NS2.0
#if NETCOREAPP
        public override int Read(Span<char> buffer)
        {
            var length = Math.Min(buffer.Length, Length - Position);
            int remainingLength = length;
            while (remainingLength > 0)
            {
                int lengthInChunk = Math.Min(ChunkSize - positionWithinChunk, remainingLength);
                Chunks[curChunk].AsSpan(positionWithinChunk, lengthInChunk).CopyTo(buffer);
                buffer = buffer[lengthInChunk..];
                positionWithinChunk += lengthInChunk;
                remainingLength -= lengthInChunk;
                if (positionWithinChunk == ChunkSize)
                {
                    positionWithinChunk = 0;
                    curChunk++;
                }
            }

            return length;
        }

        public override int ReadBlock(Span<char> buffer) =>
            Read(buffer);
#endif

        public override string ReadToEnd()
        {
            if (Position == Length)
                return "";
            int finalChunk = Math.DivRem(Length, ChunkSize, out int lengthInFinalChunk);

            string[] strings = new string[finalChunk - curChunk + 1];
            int i = 0;
            while (curChunk < finalChunk)
            {
                strings[i] = new string(Chunks[curChunk], positionWithinChunk, ChunkSize - positionWithinChunk);
                curChunk++;
                positionWithinChunk = 0;
                i++;
            }
            strings[i] = new string(Chunks[curChunk], positionWithinChunk, lengthInFinalChunk - positionWithinChunk);
            if (lengthInFinalChunk == ChunkSize)
            {
                curChunk++;
                positionWithinChunk = 0;
            }
            else
            {
                positionWithinChunk = lengthInFinalChunk;
            }
            return string.Join(null, strings);
        }
    }
}