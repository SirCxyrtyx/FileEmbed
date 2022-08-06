using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using FileEmbed;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FileEmbedTests
{
    //from https://andrewlock.net/creating-a-source-generator-part-2-testing-an-incremental-generator-with-snapshot-testing/
    internal static class TestHelper
    {
        [ModuleInitializer]
        public static void Init()
        {
            VerifySourceGenerators.Enable();
        }

        public static Task Verify(string source)
        {
            // Parse the provided string into a C# syntax tree
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);

            // Create references for assemblies we require
            // We could add multiple references if required
            IEnumerable<PortableExecutableReference> references = new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            };

            var compilation = CSharpCompilation.Create(
                assemblyName: "Tests",
                syntaxTrees: new[] { syntaxTree },
                references: references); 


            // Create an instance of our FileEmbedGenerator incremental source generator
            var generator = new FileEmbedGenerator();

            // The GeneratorDriver is used to run our generator against a compilation
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
                .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider());

            // Run the source generator!
            driver = driver.RunGenerators(compilation);

            // Use verify to snapshot test the source generator output!
            return Verifier.Verify(driver).UseDirectory("Snapshots");
        }
    }

    public class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            //the source generator should never access this
            throw new NotImplementedException();
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            //the source generator should never access this
            throw new NotImplementedException();
        }

        public override AnalyzerConfigOptions GlobalOptions => Instance;

        private static readonly TestAnalyzerConfigOptions Instance = new();
    }

    public class TestAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
        {
            switch (key.ToLower())
            {
                case "build_property.projectdir":
                    value = Path.GetFullPath(@"..\..\..\");
                    return true;
                case "build_property.fileembed_maxembedsize":
                    value = "102400";
                    return true;
                default:
                    value = null;
                    return false;
            }
        }
    }
}
