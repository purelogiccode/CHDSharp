using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/*-*******************************************************
 *  Types
 *********************************************************/
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdDDictS
{
    public void* dictBuffer;
    public void* dictContent;
    public nuint dictSize;
    public ZstdEntropyDTablesT entropy;
    public uint dictID;
    public uint entropyPresent;
    public ZstdCustomMem cMem;
}