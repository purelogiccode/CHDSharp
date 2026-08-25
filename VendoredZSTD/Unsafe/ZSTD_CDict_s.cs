using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/*-*************************************
 *  Context memory management
 ***************************************/
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdCDictS
{
    public void* dictContent;
    public nuint dictContentSize;

    /* The dictContentType the CDict was created with */
    public ZstdDictContentTypeE dictContentType;

    /* entropy workspace of HUF_WORKSPACE_SIZE bytes */
    public uint* entropyWorkspace;
    public ZstdCwksp workspace;
    public ZstdMatchStateT matchState;
    public ZstdCompressedBlockStateT cBlockState;
    public ZstdCustomMem customMem;
    public uint dictID;

    /* 0 indicates that advanced API was used to select CDict params */
    public int compressionLevel;

    /* Indicates whether the CDict was created with params that would use
     * row-based matchfinder. Unless the cdict is reloaded, we will use
     * the same greedy/lazy matchfinder at compression time.
     */
    public ZstdParamSwitchE useRowMatchFinder;
}