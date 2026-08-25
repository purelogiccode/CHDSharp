using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/**
 * Struct used for the dictionary selection function.
 */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct COVER_dictSelection
{
    public byte* dictContent;
    public nuint dictSize;
    public nuint totalCompressedSize;
}