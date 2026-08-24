using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct HufCStreamT
{
    public BitContainerEFixedBuffer bitContainer;
    public BitPosEFixedBuffer bitPos;
    public byte* startPtr;
    public byte* ptr;
    public byte* endPtr;

    [StructLayout(LayoutKind.Sequential)]
    public struct BitContainerEFixedBuffer
    {
        public nuint e0;
        public nuint e1;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitPosEFixedBuffer
    {
        public nuint e0;
        public nuint e1;
    }
}