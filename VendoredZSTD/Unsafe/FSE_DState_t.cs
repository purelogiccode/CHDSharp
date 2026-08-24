using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
{
    /* *****************************************
     *  FSE symbol decompression API
     *******************************************/
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct FSE_DState_t
    {
        public nuint state;
        /* precise table may vary, depending on U16 */
        public void* table;
    }
}
