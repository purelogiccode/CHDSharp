using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ZSTDMT_bufferPool_s
    {
        public void* poolMutex;
        public nuint bufferSize;
        public uint totalBuffers;
        public uint nbBuffers;
        public ZSTD_customMem cMem;
        public buffer_s* buffers;
    }
}
