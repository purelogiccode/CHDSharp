using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ZSTD_prefixDict_s
    {
        public void* dict;
        public nuint dictSize;
        public ZSTD_dictContentType_e dictContentType;
    }
}
