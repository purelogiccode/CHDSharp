using System.Runtime.InteropServices;
namespace VendoredZSTD.Unsafe
{
    /* *************************/
    /* double-symbols decoding */
    /* *************************/
    [StructLayout(LayoutKind.Sequential)]
    public struct HUF_DEltX2
    {
        /* double-symbols decoding */
        public ushort sequence;
        /* double-symbols decoding */
        public byte nbBits;
        /* double-symbols decoding */
        public byte length;
    }
}
