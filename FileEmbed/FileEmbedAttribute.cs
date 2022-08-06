namespace FileEmbed;

[global::System.Diagnostics.Conditional("COMPILE_TIME_ONLY")]
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