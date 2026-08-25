using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct SyncPointT
{
    /* The number of bytes to load from the input. */
    public nuint toLoad;

    /* Boolean declaring if we must flush because we found a synchronization point. */
    public int flush;
}