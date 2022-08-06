//HintName: FileEmbedAttribute.g.cs
namespace FileEmbed
{
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("FileEmbed", "0.1.0.0")]
    [global::System.Diagnostics.Conditional("FILE_EMBED_KEEP_SOURCE_GENERATOR_ATTRIBUTE")]
    [global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)]
    internal class FileEmbedAttribute : global::System.Attribute
    {
        public string FilePath { get; }

        public long Offset { get; }

        public int Length { get; }

        public FileEmbedAttribute(string filePath) : this(filePath, 0, -1) { }

        public FileEmbedAttribute(string filePath, long offset) : this(filePath, offset, -1) { }

        public FileEmbedAttribute(string filePath, long offset, int length)
        {
            FilePath = filePath;
            Offset = offset;
            Length = length;
        }
    }
}