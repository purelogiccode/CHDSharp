using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/**
 *Number of epochs and size of each epoch.
 */
[StructLayout(LayoutKind.Sequential)]
public struct COVER_epoch_info_t
{
    public uint num;
    public uint size;
}
