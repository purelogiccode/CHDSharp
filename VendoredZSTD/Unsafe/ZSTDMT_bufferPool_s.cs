using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdmtBufferPoolS
{
    public void* poolMutex;
    public nuint bufferSize;
    public uint totalBuffers;
    public uint nbBuffers;
    public ZstdCustomMem cMem;
    public BufferS* buffers;
}