using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate nuint ZstdBlockCompressorF(ZstdMatchStateT* bs, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize);