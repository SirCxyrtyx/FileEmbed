//attribute is in this namespace
using FileEmbed;

namespace EmbedExample;

//partial methods must be in partial types
public static partial class Program
{
    //Place the attribute on a static partial method that returns a ReadOnlySpan<byte>
    [FileEmbed(@"Capture.PNG")]
    public static partial ReadOnlySpan<byte> Bytes();


    //works in any type that can contain a static method
    private partial record struct MyStruct
    {
        //Path is relative to your project directory (specifically, the ProjectDir MSBuild property)
        [FileEmbed(@"Resources\Capture.7z")]
        internal static partial ReadOnlySpan<byte> StructBytes();
    }

    public partial interface IExampleInterface
    {
        //two optional arguments, Offset and Length, allow you to embed a slice of the file
        [FileEmbed(@"Resources\Capture.7z", 4, 8)]
        internal static partial ReadOnlySpan<byte> InterfaceBytes();
    }

    public static void Main()
    {
        Console.WriteLine($"{Bytes().Length} bytes");
        Console.WriteLine($"{MyStruct.StructBytes().Length} bytes");
        Console.WriteLine($"{IExampleInterface.InterfaceBytes().Length} bytes");
    }
}