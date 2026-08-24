using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct BlockPropertiesT
{
    public BlockTypeE blockType;
    public uint lastBlock;
    public uint origSize;
}