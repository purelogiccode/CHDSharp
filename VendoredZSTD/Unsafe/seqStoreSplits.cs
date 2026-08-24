using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/* Struct to keep track of where we are in our recursive calls. */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SeqStoreSplits
{
    /* Array of split indices */
    public uint* splitLocations;

    /* The current index within splitLocations being worked on */
    public nuint idx;
}