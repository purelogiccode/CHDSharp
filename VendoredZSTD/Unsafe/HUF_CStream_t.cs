using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct HUF_CStream_t
    {
        public _bitContainer_e__FixedBuffer bitContainer;
        public _bitPos_e__FixedBuffer bitPos;
        public byte* startPtr;
        public byte* ptr;
        public byte* endPtr;

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct _bitContainer_e__FixedBuffer
        {
            public nuint e0;
            public nuint e1;
        }

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct _bitPos_e__FixedBuffer
        {
            public nuint e0;
            public nuint e1;
        }
    }
}
