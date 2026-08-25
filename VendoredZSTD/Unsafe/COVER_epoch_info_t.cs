using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/*
 *Number of epochs and size of each epoch.
 */
[StructLayout(LayoutKind.Sequential)]
public struct CoverEpochInfoT
{
    public uint num;
    public uint size;
}