using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct SeqStateT
{
    public BitDStreamT DStream;
    public ZstdFseState stateLL;
    public ZstdFseState stateOffb;
    public ZstdFseState stateML;
    public PrevOffsetEFixedBuffer prevOffset;

    [StructLayout(LayoutKind.Sequential)]
    public struct PrevOffsetEFixedBuffer
    {
        public nuint e0;
        public nuint e1;
        public nuint e2;
    }
}