using System.Runtime.CompilerServices;
using System.Text;

namespace Mocha2023.Classes;

public static class LogBuffer
{
    private const int MaxLines = 5000;
    private static readonly object Lock = new();
    private static readonly LinkedList<string> Lines = new();

    public static void Add(string line)
    {
        lock (Lock)
        {
            Lines.AddLast(line);
            while (Lines.Count > MaxLines)
                Lines.RemoveFirst();
        }
    }

    public static List<string> Snapshot(int take = 500)
    {
        lock (Lock)
            return Lines.Skip(Math.Max(0, Lines.Count - take)).ToList();
    }
}

internal sealed class TeeTextWriter : TextWriter
{
    private readonly TextWriter inner;

    public TeeTextWriter(TextWriter inner) => this.inner = inner;

    public override Encoding Encoding => inner.Encoding;

    public override void Write(char value) => inner.Write(value);

    public override void Write(string? value) => inner.Write(value);

    public override void WriteLine(string? value)
    {
        inner.WriteLine(value);
        LogBuffer.Add($"[{DateTime.UtcNow:HH:mm:ss}] {value}");
    }
}

internal static class LogBufferInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        Console.SetOut(new TeeTextWriter(Console.Out));
    }
}
