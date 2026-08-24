using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate uint ZstdGetAllMatchesFn(ZstdMatchT* param0, ZstdMatchStateT* param1, uint* param2, byte* param3, byte* param4, uint* rep, uint ll0, uint lengthToBeat);