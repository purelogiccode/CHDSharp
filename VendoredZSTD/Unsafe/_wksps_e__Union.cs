using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct WkspsEUnion
{
    [FieldOffset(0)] public HufBuildCTableWkspTables buildCTable_wksp;

    [FieldOffset(0)] public HufWriteCTableWksp writeCTable_wksp;

    [FieldOffset(0)] public fixed uint hist_wksp[1024];
}