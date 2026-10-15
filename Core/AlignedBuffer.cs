using System.Runtime.InteropServices;

namespace UsbCopyStation.Core;

/// <summary>Bafer poravnat na 4 KB — obavezno za FILE_FLAG_NO_BUFFERING I/O.</summary>
internal sealed unsafe class AlignedBuffer : IDisposable
{
    public byte* Pointer { get; private set; }
    public int Length { get; }

    public AlignedBuffer(int length)
    {
        Length = length;
        Pointer = (byte*)NativeMemory.AlignedAlloc((nuint)length, 4096);
    }

    public Span<byte> Span => new(Pointer, Length);

    public void Dispose()
    {
        if (Pointer == null) return;
        NativeMemory.AlignedFree(Pointer);
        Pointer = null;
    }
}
