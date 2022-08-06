using VerifyXunit;
using Xunit;

namespace FileEmbedTests;

[UsesVerify] 
public class FileEmbedGeneratorTests
{
    [Fact]
    public Task GeneratesEmbeddedBytesCorrectly()
    {
        // The source code to test
        const string source = @"
using FileEmbed;
using System;

namespace EmbedExample;

public static partial class Program
{

    public static partial record struct MyStruct
    {
        [FileEmbed(@""Capture.PNG"", 15, 102400)]
        private static partial ReadOnlySpan<byte> StructBytes();
    }
}";

        // Pass the source code to our helper and snapshot test the output
        return TestHelper.Verify(source);
    }
}