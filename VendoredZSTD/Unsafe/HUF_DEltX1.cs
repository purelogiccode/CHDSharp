using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
{
    /*-***************************/
    /*  single-symbol decoding   */
    /*-***************************/
    [StructLayout(LayoutKind.Sequential)]
    public struct HUF_DEltX1
    {
        /* single-symbol decoding */
        public byte nbBits;
        public byte @byte;
    }
}
