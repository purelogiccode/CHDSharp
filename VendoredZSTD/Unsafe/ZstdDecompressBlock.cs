using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using InlineMethod;
using static VendoredZSTD.UnsafeHelper;

namespace VendoredZSTD.Unsafe;

public static unsafe partial class Methods
{
    /*_*******************************************************
     *  Memory operations
     **********************************************************/
    private static void ZSTD_copy4(void* dst, void* src)
    {
        memcpy(dst, src, 4);
    }

    /*! ZSTD_getcBlockSize() :
     *  Provides the size of compressed block from block header `src` */
    private static nuint ZSTD_getcBlockSize(void* src, nuint srcSize, BlockPropertiesT* bpPtr)
    {
        if (srcSize < ZstdBlockHeaderSize)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));

        {
            var cBlockHeader = MEM_readLE24(src);
            var cSize = cBlockHeader >> 3;
            bpPtr->lastBlock = cBlockHeader & 1;
            bpPtr->blockType = (BlockTypeE)((cBlockHeader >> 1) & 3);
            bpPtr->origSize = cSize;
            if (bpPtr->blockType == BlockTypeE.BtRle)
                return 1;
            if (bpPtr->blockType == BlockTypeE.BtReserved)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

            return cSize;
        }
    }

    /* Allocate buffer for literals, either overlapping current dst, or split between dst and litExtraBuffer, or stored entirely within litExtraBuffer */
    private static void ZSTD_allocateLiteralsBuffer(
        ZstdDCtxS* dctx,
        void* dst,
        nuint dstCapacity,
        nuint litSize,
        StreamingOperation streaming,
        nuint expectedWriteSize,
        uint splitImmediately
    )
    {
        if (
            streaming == StreamingOperation.NotStreaming
            && dstCapacity > (1 << 17) + 32 + litSize + 32
        )
        {
            dctx->litBuffer = (byte*)dst + (1 << 17) + 32;
            dctx->litBufferEnd = dctx->litBuffer + litSize;
            dctx->litBufferLocation = ZstdLitLocationE.ZstdInDst;
        }
        else if (
            litSize
            > (
                1 << 16 <= 64 ? 64
                : 1 << 16 <= 128 << 10 ? 1 << 16
                : 128 << 10
            )
        )
        {
            if (splitImmediately != 0)
            {
                dctx->litBuffer =
                    (byte*)dst
                    + expectedWriteSize
                    - litSize
                    + (
                        1 << 16 <= 64 ? 64
                        : 1 << 16 <= 128 << 10 ? 1 << 16
                        : 128 << 10
                    )
                    - 32;
                dctx->litBufferEnd =
                    dctx->litBuffer
                    + litSize
                    - (
                        1 << 16 <= 64 ? 64
                        : 1 << 16 <= 128 << 10 ? 1 << 16
                        : 128 << 10
                    );
            }
            else
            {
                dctx->litBuffer = (byte*)dst + expectedWriteSize - litSize;
                dctx->litBufferEnd = (byte*)dst + expectedWriteSize;
            }

            dctx->litBufferLocation = ZstdLitLocationE.ZstdSplit;
        }
        else
        {
            dctx->litBuffer = dctx->litExtraBuffer;
            dctx->litBufferEnd = dctx->litBuffer + litSize;
            dctx->litBufferLocation = ZstdLitLocationE.ZstdNotInDst;
        }
    }

    /*! ZSTD_decodeLiteralsBlock() :
     * Where it is possible to do so without being stomped by the output during decompression, the literals block will be stored
     * in the dstBuffer.  If there is room to do so, it will be stored in full in the excess dst space after where the current
     * block will be output.  Otherwise it will be stored at the end of the current dst blockspace, with a small portion being
     * stored in dctx->litExtraBuffer to help keep it "ahead" of the current output write.
     *
     * @return : nb of bytes read from src (< srcSize )
     *  note : symbol not declared but exposed for fullbench */
    private static nuint ZSTD_decodeLiteralsBlock(
        ZstdDCtxS* dctx,
        void* src,
        nuint srcSize,
        void* dst,
        nuint dstCapacity,
        StreamingOperation streaming
    )
    {
        if (srcSize < 1 + 1)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

        {
            var istart = (byte*)src;
            var litEncType = (SymbolEncodingTypeE)(istart[0] & 3);
            switch (litEncType)
            {
                case SymbolEncodingTypeE.SetRepeat:
                    if (dctx->litEntropy == 0)
                        return unchecked(
                            (nuint)(-(int)ZstdErrorCode.ZstdErrorDictionaryCorrupted)
                        );

                    goto case SymbolEncodingTypeE.SetCompressed;
                case SymbolEncodingTypeE.SetCompressed:
                    if (srcSize < 5)
                        return unchecked(
                            (nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected)
                        );

                {
                    nuint lhSize,
                        litSize,
                        litCSize;
                    uint singleStream = 0;
                    var lhlCode = (uint)((istart[0] >> 2) & 3);
                    var lhc = MEM_readLE32(istart);
                    nuint hufSuccess;
                    var expectedWriteSize = 1 << 17 < dstCapacity ? 1 << 17 : dstCapacity;
                    var flags =
                        0
                        | (ZSTD_DCtx_get_bmi2(dctx) != 0 ? (int)HufFlagsE.HufFlagsBmi2 : 0)
                        | (
                            dctx->disableHufAsm != 0 ? (int)HufFlagsE.HufFlagsDisableAsm : 0
                        );
                    switch (lhlCode)
                    {
                        case 0:
                        case 1:
                        default:
                            singleStream = lhlCode == 0 ? 1U : 0U;
                            lhSize = 3;
                            litSize = (lhc >> 4) & 0x3FF;
                            litCSize = (lhc >> 14) & 0x3FF;
                            break;
                        case 2:
                            lhSize = 4;
                            litSize = (lhc >> 4) & 0x3FFF;
                            litCSize = lhc >> 18;
                            break;
                        case 3:
                            lhSize = 5;
                            litSize = (lhc >> 4) & 0x3FFFF;
                            litCSize = (lhc >> 22) + ((nuint)istart[4] << 10);
                            break;
                    }

                    if (litSize > 0 && dst == null)
                        return unchecked(
                            (nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall)
                        );

                    if (litSize > 1 << 17)
                        return unchecked(
                            (nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected)
                        );

                    if (singleStream == 0)
                        if (litSize < 6)
                            return unchecked(
                                (nuint)(-(int)ZstdErrorCode.ZstdErrorLiteralsHeaderWrong)
                            );

                    if (litCSize + lhSize > srcSize)
                        return unchecked(
                            (nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected)
                        );

                    if (expectedWriteSize < litSize)
                        return unchecked(
                            (nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall)
                        );

                    ZSTD_allocateLiteralsBuffer(
                        dctx,
                        dst,
                        dstCapacity,
                        litSize,
                        streaming,
                        expectedWriteSize,
                        0
                    );
                    if (dctx->ddictIsCold != 0 && litSize > 768)
                    {
                        var ptr = (sbyte*)dctx->HUFptr;
                        const nuint size = sizeof(uint) * 4097;
                        nuint pos;
                        for (pos = 0; pos < size; pos += 64)
                        {
#if NETCOREAPP3_0_OR_GREATER
                            if (Sse.IsSupported)
                                Sse.Prefetch1(ptr + pos);
#endif
                        }
                    }

                    if (litEncType == SymbolEncodingTypeE.SetRepeat)
                    {
                        if (singleStream != 0)
                        {
                            hufSuccess = HUF_decompress1X_usingDTable(
                                dctx->litBuffer,
                                litSize,
                                istart + lhSize,
                                litCSize,
                                dctx->HUFptr,
                                flags
                            );
                        }
                        else
                        {
                            assert(litSize >= 6);
                            hufSuccess = HUF_decompress4X_usingDTable(
                                dctx->litBuffer,
                                litSize,
                                istart + lhSize,
                                litCSize,
                                dctx->HUFptr,
                                flags
                            );
                        }
                    }
                    else
                    {
                        if (singleStream != 0)
                            hufSuccess = HUF_decompress1X1_DCtx_wksp(
                                dctx->entropy.hufTable,
                                dctx->litBuffer,
                                litSize,
                                istart + lhSize,
                                litCSize,
                                dctx->workspace,
                                sizeof(uint) * 640,
                                flags
                            );
                        else
                            hufSuccess = HUF_decompress4X_hufOnly_wksp(
                                dctx->entropy.hufTable,
                                dctx->litBuffer,
                                litSize,
                                istart + lhSize,
                                litCSize,
                                dctx->workspace,
                                sizeof(uint) * 640,
                                flags
                            );
                    }

                    if (dctx->litBufferLocation == ZstdLitLocationE.ZstdSplit)
                    {
                        memcpy(
                            dctx->litExtraBuffer,
                            dctx->litBufferEnd
                            - (
                                1 << 16 <= 64 ? 64
                                : 1 << 16 <= 128 << 10 ? 1 << 16
                                : 128 << 10
                            ),
                            1 << 16 <= 64 ? 64
                            : 1 << 16 <= 128 << 10 ? 1 << 16
                            : 128 << 10
                        );
                        memmove(
                            dctx->litBuffer
                            + (
                                1 << 16 <= 64 ? 64
                                : 1 << 16 <= 128 << 10 ? 1 << 16
                                : 128 << 10
                            )
                            - 32,
                            dctx->litBuffer,
                            litSize
                            - (
                                1 << 16 <= 64 ? 64
                                : 1 << 16 <= 128 << 10 ? 1 << 16
                                : 128 << 10
                            )
                        );
                        dctx->litBuffer +=
                        (
                            1 << 16 <= 64 ? 64
                            : 1 << 16 <= 128 << 10 ? 1 << 16
                            : 128 << 10
                        ) - 32;
                        dctx->litBufferEnd -= 32;
                    }

                    if (ERR_isError(hufSuccess))
                        return unchecked(
                            (nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected)
                        );

                    dctx->litPtr = dctx->litBuffer;
                    dctx->litSize = litSize;
                    dctx->litEntropy = 1;
                    if (litEncType == SymbolEncodingTypeE.SetCompressed)
                        dctx->HUFptr = dctx->entropy.hufTable;
                    return litCSize + lhSize;
                }

                case SymbolEncodingTypeE.SetBasic:
                {
                    nuint litSize,
                        lhSize;
                    var lhlCode = (uint)((istart[0] >> 2) & 3);
                    var expectedWriteSize = 1 << 17 < dstCapacity ? 1 << 17 : dstCapacity;
                    switch (lhlCode)
                    {
                        case 0:
                        case 2:
                        default:
                            lhSize = 1;
                            litSize = (nuint)(istart[0] >> 3);
                            break;
                        case 1:
                            lhSize = 2;
                            litSize = (nuint)(MEM_readLE16(istart) >> 4);
                            break;
                        case 3:
                            lhSize = 3;
                            if (srcSize < 3)
                                return unchecked(
                                    (nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected)
                                );

                            litSize = MEM_readLE24(istart) >> 4;
                            break;
                    }

                    if (litSize > 0 && dst == null)
                        return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));

                    if (expectedWriteSize < litSize)
                        return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));

                    ZSTD_allocateLiteralsBuffer(
                        dctx,
                        dst,
                        dstCapacity,
                        litSize,
                        streaming,
                        expectedWriteSize,
                        1
                    );
                    if (lhSize + litSize + 32 > srcSize)
                    {
                        if (litSize + lhSize > srcSize)
                            return unchecked(
                                (nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected)
                            );

                        if (dctx->litBufferLocation == ZstdLitLocationE.ZstdSplit)
                        {
                            memcpy(
                                dctx->litBuffer,
                                istart + lhSize,
                                (uint)(
                                    litSize
                                    - (
                                        1 << 16 <= 64 ? 64
                                        : 1 << 16 <= 128 << 10 ? 1 << 16
                                        : 128 << 10
                                    )
                                )
                            );
                            memcpy(
                                dctx->litExtraBuffer,
                                istart
                                + lhSize
                                + litSize
                                - (
                                    1 << 16 <= 64 ? 64
                                    : 1 << 16 <= 128 << 10 ? 1 << 16
                                    : 128 << 10
                                ),
                                1 << 16 <= 64 ? 64
                                : 1 << 16 <= 128 << 10 ? 1 << 16
                                : 128 << 10
                            );
                        }
                        else
                        {
                            memcpy(dctx->litBuffer, istart + lhSize, (uint)litSize);
                        }

                        dctx->litPtr = dctx->litBuffer;
                        dctx->litSize = litSize;
                        return lhSize + litSize;
                    }

                    dctx->litPtr = istart + lhSize;
                    dctx->litSize = litSize;
                    dctx->litBufferEnd = dctx->litPtr + litSize;
                    dctx->litBufferLocation = ZstdLitLocationE.ZstdNotInDst;
                    return lhSize + litSize;
                }

                case SymbolEncodingTypeE.SetRle:
                {
                    var lhlCode = (uint)((istart[0] >> 2) & 3);
                    nuint litSize,
                        lhSize;
                    var expectedWriteSize = 1 << 17 < dstCapacity ? 1 << 17 : dstCapacity;
                    switch (lhlCode)
                    {
                        case 0:
                        case 2:
                        default:
                            lhSize = 1;
                            litSize = (nuint)(istart[0] >> 3);
                            break;
                        case 1:
                            lhSize = 2;
                            if (srcSize < 3)
                                return unchecked(
                                    (nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected)
                                );

                            litSize = (nuint)(MEM_readLE16(istart) >> 4);
                            break;
                        case 3:
                            lhSize = 3;
                            if (srcSize < 4)
                                return unchecked(
                                    (nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected)
                                );

                            litSize = MEM_readLE24(istart) >> 4;
                            break;
                    }

                    if (litSize > 0 && dst == null)
                        return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));

                    if (litSize > 1 << 17)
                        return unchecked(
                            (nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected)
                        );

                    if (expectedWriteSize < litSize)
                        return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));

                    ZSTD_allocateLiteralsBuffer(
                        dctx,
                        dst,
                        dstCapacity,
                        litSize,
                        streaming,
                        expectedWriteSize,
                        1
                    );
                    if (dctx->litBufferLocation == ZstdLitLocationE.ZstdSplit)
                    {
                        memset(
                            dctx->litBuffer,
                            istart[lhSize],
                            (uint)(
                                litSize
                                - (
                                    1 << 16 <= 64 ? 64
                                    : 1 << 16 <= 128 << 10 ? 1 << 16
                                    : 128 << 10
                                )
                            )
                        );
                        memset(
                            dctx->litExtraBuffer,
                            istart[lhSize],
                            1 << 16 <= 64 ? 64
                            : 1 << 16 <= 128 << 10 ? 1 << 16
                            : 128 << 10
                        );
                    }
                    else
                    {
                        memset(dctx->litBuffer, istart[lhSize], (uint)litSize);
                    }

                    dctx->litPtr = dctx->litBuffer;
                    dctx->litSize = litSize;
                    return lhSize + 1;
                }

                default:
                {
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
                }
            }
        }
    }

    private static readonly ZstdSeqSymbol* LlDefaultDTable = GetArrayPointer(
        new ZstdSeqSymbol[65]
        {
            new(1, 1, 1, 6),
            new(0, 0, 4, 0),
            new(16, 0, 4, 0),
            new(32, 0, 5, 1),
            new(0, 0, 5, 3),
            new(0, 0, 5, 4),
            new(0, 0, 5, 6),
            new(0, 0, 5, 7),
            new(0, 0, 5, 9),
            new(0, 0, 5, 10),
            new(0, 0, 5, 12),
            new(0, 0, 6, 14),
            new(0, 1, 5, 16),
            new(0, 1, 5, 20),
            new(0, 1, 5, 22),
            new(0, 2, 5, 28),
            new(0, 3, 5, 32),
            new(0, 4, 5, 48),
            new(32, 6, 5, 64),
            new(0, 7, 5, 128),
            new(0, 8, 6, 256),
            new(0, 10, 6, 1024),
            new(0, 12, 6, 4096),
            new(32, 0, 4, 0),
            new(0, 0, 4, 1),
            new(0, 0, 5, 2),
            new(32, 0, 5, 4),
            new(0, 0, 5, 5),
            new(32, 0, 5, 7),
            new(0, 0, 5, 8),
            new(32, 0, 5, 10),
            new(0, 0, 5, 11),
            new(0, 0, 6, 13),
            new(32, 1, 5, 16),
            new(0, 1, 5, 18),
            new(32, 1, 5, 22),
            new(0, 2, 5, 24),
            new(32, 3, 5, 32),
            new(0, 3, 5, 40),
            new(0, 6, 4, 64),
            new(16, 6, 4, 64),
            new(32, 7, 5, 128),
            new(0, 9, 6, 512),
            new(0, 11, 6, 2048),
            new(48, 0, 4, 0),
            new(16, 0, 4, 1),
            new(32, 0, 5, 2),
            new(32, 0, 5, 3),
            new(32, 0, 5, 5),
            new(32, 0, 5, 6),
            new(32, 0, 5, 8),
            new(32, 0, 5, 9),
            new(32, 0, 5, 11),
            new(32, 0, 5, 12),
            new(0, 0, 6, 15),
            new(32, 1, 5, 18),
            new(32, 1, 5, 20),
            new(32, 2, 5, 24),
            new(32, 2, 5, 28),
            new(32, 3, 5, 40),
            new(32, 4, 5, 48),
            new(0, 16, 6, 65536),
            new(0, 15, 6, 32768),
            new(0, 14, 6, 16384),
            new(0, 13, 6, 8192)
        }
    );

    private static readonly ZstdSeqSymbol* OfDefaultDTable = GetArrayPointer(
        new ZstdSeqSymbol[33]
        {
            new(1, 1, 1, 5),
            new(0, 0, 5, 0),
            new(0, 6, 4, 61),
            new(0, 9, 5, 509),
            new(0, 15, 5, 32765),
            new(0, 21, 5, 2097149),
            new(0, 3, 5, 5),
            new(0, 7, 4, 125),
            new(0, 12, 5, 4093),
            new(0, 18, 5, 262141),
            new(0, 23, 5, 8388605),
            new(0, 5, 5, 29),
            new(0, 8, 4, 253),
            new(0, 14, 5, 16381),
            new(0, 20, 5, 1048573),
            new(0, 2, 5, 1),
            new(16, 7, 4, 125),
            new(0, 11, 5, 2045),
            new(0, 17, 5, 131069),
            new(0, 22, 5, 4194301),
            new(0, 4, 5, 13),
            new(16, 8, 4, 253),
            new(0, 13, 5, 8189),
            new(0, 19, 5, 524285),
            new(0, 1, 5, 1),
            new(16, 6, 4, 61),
            new(0, 10, 5, 1021),
            new(0, 16, 5, 65533),
            new(0, 28, 5, 268435453),
            new(0, 27, 5, 134217725),
            new(0, 26, 5, 67108861),
            new(0, 25, 5, 33554429),
            new(0, 24, 5, 16777213)
        }
    );

    private static readonly ZstdSeqSymbol* MlDefaultDTable = GetArrayPointer(
        new ZstdSeqSymbol[65]
        {
            new(1, 1, 1, 6),
            new(0, 0, 6, 3),
            new(0, 0, 4, 4),
            new(32, 0, 5, 5),
            new(0, 0, 5, 6),
            new(0, 0, 5, 8),
            new(0, 0, 5, 9),
            new(0, 0, 5, 11),
            new(0, 0, 6, 13),
            new(0, 0, 6, 16),
            new(0, 0, 6, 19),
            new(0, 0, 6, 22),
            new(0, 0, 6, 25),
            new(0, 0, 6, 28),
            new(0, 0, 6, 31),
            new(0, 0, 6, 34),
            new(0, 1, 6, 37),
            new(0, 1, 6, 41),
            new(0, 2, 6, 47),
            new(0, 3, 6, 59),
            new(0, 4, 6, 83),
            new(0, 7, 6, 131),
            new(0, 9, 6, 515),
            new(16, 0, 4, 4),
            new(0, 0, 4, 5),
            new(32, 0, 5, 6),
            new(0, 0, 5, 7),
            new(32, 0, 5, 9),
            new(0, 0, 5, 10),
            new(0, 0, 6, 12),
            new(0, 0, 6, 15),
            new(0, 0, 6, 18),
            new(0, 0, 6, 21),
            new(0, 0, 6, 24),
            new(0, 0, 6, 27),
            new(0, 0, 6, 30),
            new(0, 0, 6, 33),
            new(0, 1, 6, 35),
            new(0, 1, 6, 39),
            new(0, 2, 6, 43),
            new(0, 3, 6, 51),
            new(0, 4, 6, 67),
            new(0, 5, 6, 99),
            new(0, 8, 6, 259),
            new(32, 0, 4, 4),
            new(48, 0, 4, 4),
            new(16, 0, 4, 5),
            new(32, 0, 5, 7),
            new(32, 0, 5, 8),
            new(32, 0, 5, 10),
            new(32, 0, 5, 11),
            new(0, 0, 6, 14),
            new(0, 0, 6, 17),
            new(0, 0, 6, 20),
            new(0, 0, 6, 23),
            new(0, 0, 6, 26),
            new(0, 0, 6, 29),
            new(0, 0, 6, 32),
            new(0, 16, 6, 65539),
            new(0, 15, 6, 32771),
            new(0, 14, 6, 16387),
            new(0, 13, 6, 8195),
            new(0, 12, 6, 4099),
            new(0, 11, 6, 2051),
            new(0, 10, 6, 1027)
        }
    );

    private static void ZSTD_buildSeqTable_rle(ZstdSeqSymbol* dt, uint baseValue, byte nbAddBits)
    {
        void* ptr = dt;
        var dTableH = (ZstdSeqSymbolHeader*)ptr;
        var cell = dt + 1;
        dTableH->tableLog = 0;
        dTableH->fastMode = 0;
        cell->nbBits = 0;
        cell->nextState = 0;
        assert(nbAddBits < 255);
        cell->nbAdditionalBits = nbAddBits;
        cell->baseValue = baseValue;
    }

    /* ZSTD_buildFSETable() :
     * generate FSE decoding table for one symbol (ll, ml or off)
     * cannot fail if input is valid =>
     * all inputs are presumed validated at this stage */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZSTD_buildFSETable_body(
        ZstdSeqSymbol* dt,
        short* normalizedCounter,
        uint maxSymbolValue,
        uint* baseValue,
        byte* nbAdditionalBits,
        uint tableLog,
        void* wksp,
        nuint wkspSize
    )
    {
        var tableDecode = dt + 1;
        var maxSv1 = maxSymbolValue + 1;
        var tableSize = (uint)(1 << (int)tableLog);
        var symbolNext = (ushort*)wksp;
        var spread = (byte*)(symbolNext + (35 > 52 ? 35 : 52) + 1);
        var highThreshold = tableSize - 1;
        assert(maxSymbolValue <= (35 > 52 ? 35 : 52));
        assert(tableLog <= 9);
        assert(wkspSize >= sizeof(short) * (52 + 1) + (1U << 9) + sizeof(ulong));
        {
            ZstdSeqSymbolHeader dTableH;
            dTableH.tableLog = tableLog;
            dTableH.fastMode = 1;
            {
                var largeLimit = (short)(1 << (int)(tableLog - 1));
                uint s;
                for (s = 0; s < maxSv1; s++)
                    if (normalizedCounter[s] == -1)
                    {
                        tableDecode[highThreshold--].baseValue = s;
                        symbolNext[s] = 1;
                    }
                    else
                    {
                        if (normalizedCounter[s] >= largeLimit)
                            dTableH.fastMode = 0;
                        assert(normalizedCounter[s] >= 0);
                        symbolNext[s] = (ushort)normalizedCounter[s];
                    }
            }

            memcpy(dt, &dTableH, (uint)sizeof(ZstdSeqSymbolHeader));
        }

        assert(tableSize <= 512);
        if (highThreshold == tableSize - 1)
        {
            nuint tableMask = tableSize - 1;
            nuint step = (tableSize >> 1) + (tableSize >> 3) + 3;
            {
                const ulong add = 0x0101010101010101UL;
                nuint pos = 0;
                ulong sv = 0;
                uint s;
                for (s = 0; s < maxSv1; ++s, sv += add)
                {
                    int i;
                    int n = normalizedCounter[s];
                    MEM_write64(spread + pos, sv);
                    for (i = 8; i < n; i += 8)
                        MEM_write64(spread + pos + i, sv);

                    assert(n >= 0);
                    pos += (nuint)n;
                }
            }

            {
                nuint position = 0;
                nuint s;
                const nuint unroll = 2;
                assert(tableSize % unroll == 0);
                for (s = 0; s < tableSize; s += unroll)
                {
                    nuint u;
                    for (u = 0; u < unroll; ++u)
                    {
                        var uPosition = (position + u * step) & tableMask;
                        tableDecode[uPosition].baseValue = spread[s + u];
                    }

                    position = (position + unroll * step) & tableMask;
                }

                assert(position == 0);
            }
        }
        else
        {
            var tableMask = tableSize - 1;
            var step = (tableSize >> 1) + (tableSize >> 3) + 3;
            uint s,
                position = 0;
            for (s = 0; s < maxSv1; s++)
            {
                int i;
                int n = normalizedCounter[s];
                for (i = 0; i < n; i++)
                {
                    tableDecode[position].baseValue = s;
                    position = (position + step) & tableMask;
                    while (position > highThreshold)
                        position = (position + step) & tableMask;
                }
            }

            assert(position == 0);
        }

        {
            uint u;
            for (u = 0; u < tableSize; u++)
            {
                var symbol = tableDecode[u].baseValue;
                uint nextState = symbolNext[symbol]++;
                tableDecode[u].nbBits = (byte)(tableLog - ZSTD_highbit32(nextState));
                tableDecode[u].nextState = (ushort)(
                    (nextState << tableDecode[u].nbBits) - tableSize
                );
                assert(nbAdditionalBits[symbol] < 255);
                tableDecode[u].nbAdditionalBits = nbAdditionalBits[symbol];
                tableDecode[u].baseValue = baseValue[symbol];
            }
        }
    }

    /* Avoids the FORCE_INLINE of the _body() function. */
    private static void ZSTD_buildFSETable_body_default(
        ZstdSeqSymbol* dt,
        short* normalizedCounter,
        uint maxSymbolValue,
        uint* baseValue,
        byte* nbAdditionalBits,
        uint tableLog,
        void* wksp,
        nuint wkspSize
    )
    {
        ZSTD_buildFSETable_body(
            dt,
            normalizedCounter,
            maxSymbolValue,
            baseValue,
            nbAdditionalBits,
            tableLog,
            wksp,
            wkspSize
        );
    }

    /* ZSTD_buildFSETable() :
     * generate FSE decoding table for one symbol (ll, ml or off)
     * this function must be called with valid parameters only
     * (dt is large enough, normalizedCounter distribution total is a power of 2, max is within range, etc.)
     * in which case it cannot fail.
     * The workspace must be 4-byte aligned and at least ZSTD_BUILD_FSE_TABLE_WKSP_SIZE bytes, which is
     * defined in zstd_decompress_internal.h.
     * Internal use only.
     */
    private static void ZSTD_buildFSETable(
        ZstdSeqSymbol* dt,
        short* normalizedCounter,
        uint maxSymbolValue,
        uint* baseValue,
        byte* nbAdditionalBits,
        uint tableLog,
        void* wksp,
        nuint wkspSize,
        // ReSharper disable once UnusedParameter.Local
        int bmi2
    )
    {
        ZSTD_buildFSETable_body_default(
            dt,
            normalizedCounter,
            maxSymbolValue,
            baseValue,
            nbAdditionalBits,
            tableLog,
            wksp,
            wkspSize
        );
    }

    /*! ZSTD_buildSeqTable() :
     * @return : nb bytes read from src,
     *           or an error code if it fails */
    private static nuint ZSTD_buildSeqTable(
        ZstdSeqSymbol* dTableSpace,
        ZstdSeqSymbol** dTablePtr,
        SymbolEncodingTypeE type,
        uint max,
        uint maxLog,
        void* src,
        nuint srcSize,
        uint* baseValue,
        byte* nbAdditionalBits,
        ZstdSeqSymbol* defaultTable,
        uint flagRepeatTable,
        int ddictIsCold,
        int nbSeq,
        uint* wksp,
        nuint wkspSize,
        int bmi2
    )
    {
        switch (type)
        {
            case SymbolEncodingTypeE.SetRle:
                if (srcSize == 0)
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));

                if (*(byte*)src > max)
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

            {
                uint symbol = *(byte*)src;
                var baseline = baseValue[symbol];
                var nbBits = nbAdditionalBits[symbol];
                ZSTD_buildSeqTable_rle(dTableSpace, baseline, nbBits);
            }

                *dTablePtr = dTableSpace;
                return 1;
            case SymbolEncodingTypeE.SetBasic:
                *dTablePtr = defaultTable;
                return 0;
            case SymbolEncodingTypeE.SetRepeat:
                if (flagRepeatTable == 0)
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

                if (ddictIsCold != 0 && nbSeq > 24)
                {
                    void* pStart = *dTablePtr;
                    var pSize = (nuint)(sizeof(ZstdSeqSymbol) * (1 + (1 << (int)maxLog)));
                    {
                        var ptr = (sbyte*)pStart;
                        var size = pSize;
                        nuint pos;
                        for (pos = 0; pos < size; pos += 64)
                        {
#if NETCOREAPP3_0_OR_GREATER
                            if (Sse.IsSupported)
                                Sse.Prefetch1(ptr + pos);
#endif
                        }
                    }
                }

                return 0;
            case SymbolEncodingTypeE.SetCompressed:
            {
                uint tableLog;
                var norm = stackalloc short[53];
                var headerSize = FSE_readNCount(norm, &max, &tableLog, src, srcSize);
                if (ERR_isError(headerSize))
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

                if (tableLog > maxLog)
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

                ZSTD_buildFSETable(
                    dTableSpace,
                    norm,
                    max,
                    baseValue,
                    nbAdditionalBits,
                    tableLog,
                    wksp,
                    wkspSize,
                    bmi2
                );
                *dTablePtr = dTableSpace;
                return headerSize;
            }

            default:
                assert(0 != 0);

            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorGeneric));
            }
        }
    }

    /*! ZSTD_decodeSeqHeaders() :
     *  decode sequence header from src */
    /* Used by: decompress, fullbench (does not get its definition from here) */
    private static nuint ZSTD_decodeSeqHeaders(
        ZstdDCtxS* dctx,
        int* nbSeqPtr,
        void* src,
        nuint srcSize
    )
    {
        var istart = (byte*)src;
        var iend = istart + srcSize;
        var ip = istart;
        int nbSeq;
        if (srcSize < 1)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));

        nbSeq = *ip++;
        if (nbSeq == 0)
        {
            *nbSeqPtr = 0;
            if (srcSize != 1)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));

            return 1;
        }

        if (nbSeq > 0x7F)
        {
            if (nbSeq == 0xFF)
            {
                if (ip + 2 > iend)
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));

                nbSeq = MEM_readLE16(ip) + 0x7F00;
                ip += 2;
            }
            else
            {
                if (ip >= iend)
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));

                nbSeq = ((nbSeq - 0x80) << 8) + *ip++;
            }
        }

        *nbSeqPtr = nbSeq;
        if (ip + 1 > iend)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));

        {
            var lLtype = (SymbolEncodingTypeE)(*ip >> 6);
            var oFtype = (SymbolEncodingTypeE)((*ip >> 4) & 3);
            var mLtype = (SymbolEncodingTypeE)((*ip >> 2) & 3);
            ip++;
            {
                var llhSize = ZSTD_buildSeqTable(
                    &dctx->entropy.LLTable.e0,
                    &dctx->LLTptr,
                    lLtype,
                    35,
                    9,
                    ip,
                    (nuint)(iend - ip),
                    LlBase,
                    LlBits,
                    LlDefaultDTable,
                    dctx->fseEntropy,
                    dctx->ddictIsCold,
                    nbSeq,
                    dctx->workspace,
                    sizeof(uint) * 640,
                    ZSTD_DCtx_get_bmi2(dctx)
                );
                if (ERR_isError(llhSize))
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

                ip += llhSize;
            }

            {
                var ofhSize = ZSTD_buildSeqTable(
                    &dctx->entropy.OFTable.e0,
                    &dctx->OFTptr,
                    oFtype,
                    31,
                    8,
                    ip,
                    (nuint)(iend - ip),
                    OfBase,
                    OfBits,
                    OfDefaultDTable,
                    dctx->fseEntropy,
                    dctx->ddictIsCold,
                    nbSeq,
                    dctx->workspace,
                    sizeof(uint) * 640,
                    ZSTD_DCtx_get_bmi2(dctx)
                );
                if (ERR_isError(ofhSize))
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

                ip += ofhSize;
            }

            {
                var mlhSize = ZSTD_buildSeqTable(
                    &dctx->entropy.MLTable.e0,
                    &dctx->MLTptr,
                    mLtype,
                    52,
                    9,
                    ip,
                    (nuint)(iend - ip),
                    MlBase,
                    MlBits,
                    MlDefaultDTable,
                    dctx->fseEntropy,
                    dctx->ddictIsCold,
                    nbSeq,
                    dctx->workspace,
                    sizeof(uint) * 640,
                    ZSTD_DCtx_get_bmi2(dctx)
                );
                if (ERR_isError(mlhSize))
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

                ip += mlhSize;
            }
        }

        return (nuint)(ip - istart);
    }

#if NET8_0_OR_GREATER
    private static ReadOnlySpan<uint> SpanDec32Table => new uint[8] { 0, 1, 2, 1, 4, 4, 4, 4 };
    private static uint* Dec32Table =>
        (uint*)
        System.Runtime.CompilerServices.Unsafe.AsPointer(
            ref MemoryMarshal.GetReference(SpanDec32Table)
        );
#else
    private static readonly uint* dec32table = GetArrayPointer(
        new uint[8] { 0, 1, 2, 1, 4, 4, 4, 4 }
    );
#endif
#if NET8_0_OR_GREATER
    private static ReadOnlySpan<int> SpanDec64Table => new int[8] { 8, 8, 8, 7, 8, 9, 10, 11 };
    private static int* Dec64Table =>
        (int*)
        System.Runtime.CompilerServices.Unsafe.AsPointer(
            ref MemoryMarshal.GetReference(SpanDec64Table)
        );
#else
    private static readonly int* dec64table = GetArrayPointer(
        new int[8] { 8, 8, 8, 7, 8, 9, 10, 11 }
    );
#endif
    /*! ZSTD_overlapCopy8() :
     *  Copies 8 bytes from ip to op and updates op and ip where ip <= op.
     *  If the offset is < 8 then the offset is spread to at least 8 bytes.
     *
     *  Precondition: *ip <= *op
     *  Postcondition: *op - *op >= 8
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZSTD_overlapCopy8(byte** op, byte** ip, nuint offset)
    {
        assert(*ip <= *op);
        if (offset < 8)
        {
            var sub2 = Dec64Table[offset];
            (*op)[0] = (*ip)[0];
            (*op)[1] = (*ip)[1];
            (*op)[2] = (*ip)[2];
            (*op)[3] = (*ip)[3];
            *ip += Dec32Table[offset];
            ZSTD_copy4(*op + 4, *ip);
            *ip -= sub2;
        }
        else
        {
            ZSTD_copy8(*op, *ip);
        }

        *ip += 8;
        *op += 8;
        assert(*op - *ip >= 8);
    }

    /*! ZSTD_safecopy() :
     *  Specialized version of memcpy() that is allowed to READ up to WILDCOPY_OVERLENGTH past the input buffer
     *  and write up to 16 bytes past oend_w (op >= oend_w is allowed).
     *  This function is only called in the uncommon case where the sequence is near the end of the block. It
     *  should be fast for a single long sequence, but can be slow for several short sequences.
     *
     *  @param ovtype controls the overlap detection
     *         - ZSTD_no_overlap: The source and destination are guaranteed to be at least WILDCOPY_VECLEN bytes apart.
     *         - ZSTD_overlap_src_before_dst: The src and dst may overlap and may be any distance apart.
     *           The src buffer must be before the dst buffer.
     */
    private static void ZSTD_safecopy(
        byte* op,
        byte* oendW,
        byte* ip,
        nint length,
        ZstdOverlapE ovtype
    )
    {
        var diff = (nint)(op - ip);
        var oend = op + length;
        assert(
            (ovtype == ZstdOverlapE.ZstdNoOverlap && (diff <= -8 || diff >= 8 || op >= oendW))
            || (ovtype == ZstdOverlapE.ZstdOverlapSrcBeforeDst && diff >= 0)
        );
        if (length < 8)
        {
            while (op < oend)
                *op++ = *ip++;
            return;
        }

        if (ovtype == ZstdOverlapE.ZstdOverlapSrcBeforeDst)
        {
            assert(length >= 8);
            ZSTD_overlapCopy8(&op, &ip, (nuint)diff);
            length -= 8;
            assert(op - ip >= 8);
            assert(op <= oend);
        }

        if (oend <= oendW)
        {
            ZSTD_wildcopy(op, ip, length, ovtype);
            return;
        }

        if (op <= oendW)
        {
            assert(oend > oendW);
            ZSTD_wildcopy(op, ip, (nint)(oendW - op), ovtype);
            ip += oendW - op;
            op += oendW - op;
        }

        while (op < oend)
            *op++ = *ip++;
    }

    /* ZSTD_safecopyDstBeforeSrc():
     * This version allows overlap with dst before src, or handles the non-overlap case with dst after src
     * Kept separate from more common ZSTD_safecopy case to avoid performance impact to the safecopy common case */
    private static void ZSTD_safecopyDstBeforeSrc(byte* op, byte* ip, nint length)
    {
        var diff = (nint)(op - ip);
        var oend = op + length;
        if (length < 8 || diff > -8)
        {
            while (op < oend)
                *op++ = *ip++;
            return;
        }

        if (op <= oend - 32 && diff < -16)
        {
            ZSTD_wildcopy(op, ip, (nint)(oend - 32 - op), ZstdOverlapE.ZstdNoOverlap);
            ip += oend - 32 - op;
            op += oend - 32 - op;
        }

        while (op < oend)
            *op++ = *ip++;
    }

    /* ZSTD_execSequenceEnd():
     * This version handles cases that are near the end of the output buffer. It requires
     * more careful checks to make sure there is no overflow. By separating out these hard
     * and unlikely cases, we can speed up the common cases.
     *
     * NOTE: This function needs to be fast for a single long sequence, but doesn't need
     * to be optimized for many small sequences, since those fall into ZSTD_execSequence().
     */
    private static nuint ZSTD_execSequenceEnd(
        byte* op,
        byte* oend,
        SeqT sequence,
        byte** litPtr,
        byte* litLimit,
        byte* prefixStart,
        byte* virtualStart,
        byte* dictEnd
    )
    {
        var oLitEnd = op + sequence.litLength;
        var sequenceLength = sequence.litLength + sequence.matchLength;
        var iLitEnd = *litPtr + sequence.litLength;
        var match = oLitEnd - sequence.offset;
        var oendW = oend - 32;
        if (sequenceLength > (nuint)(oend - op))
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));

        if (sequence.litLength > (nuint)(litLimit - *litPtr))
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

        assert(op < op + sequenceLength);
        assert(oLitEnd < op + sequenceLength);
        ZSTD_safecopy(
            op,
            oendW,
            *litPtr,
            (nint)sequence.litLength,
            ZstdOverlapE.ZstdNoOverlap
        );
        op = oLitEnd;
        *litPtr = iLitEnd;
        if (sequence.offset > (nuint)(oLitEnd - prefixStart))
        {
            if (sequence.offset > (nuint)(oLitEnd - virtualStart))
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

            match = dictEnd - (prefixStart - match);
            if (match + sequence.matchLength <= dictEnd)
            {
                memmove(oLitEnd, match, sequence.matchLength);
                return sequenceLength;
            }

            {
                var length1 = (nuint)(dictEnd - match);
                memmove(oLitEnd, match, length1);
                op = oLitEnd + length1;
                sequence.matchLength -= length1;
                match = prefixStart;
            }
        }

        ZSTD_safecopy(
            op,
            oendW,
            match,
            (nint)sequence.matchLength,
            ZstdOverlapE.ZstdOverlapSrcBeforeDst
        );
        return sequenceLength;
    }

    /* ZSTD_execSequenceEndSplitLitBuffer():
     * This version is intended to be used during instances where the litBuffer is still split.  It is kept separate to avoid performance impact for the good case.
     */
    private static nuint ZSTD_execSequenceEndSplitLitBuffer(
        byte* op,
        byte* oend,
        byte* oendW,
        SeqT sequence,
        byte** litPtr,
        byte* litLimit,
        byte* prefixStart,
        byte* virtualStart,
        byte* dictEnd
    )
    {
        var oLitEnd = op + sequence.litLength;
        var sequenceLength = sequence.litLength + sequence.matchLength;
        var iLitEnd = *litPtr + sequence.litLength;
        var match = oLitEnd - sequence.offset;
        if (sequenceLength > (nuint)(oend - op))
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));

        if (sequence.litLength > (nuint)(litLimit - *litPtr))
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

        assert(op < op + sequenceLength);
        assert(oLitEnd < op + sequenceLength);
        if (op > *litPtr && op < *litPtr + sequence.litLength)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));

        ZSTD_safecopyDstBeforeSrc(op, *litPtr, (nint)sequence.litLength);
        op = oLitEnd;
        *litPtr = iLitEnd;
        if (sequence.offset > (nuint)(oLitEnd - prefixStart))
        {
            if (sequence.offset > (nuint)(oLitEnd - virtualStart))
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

            match = dictEnd - (prefixStart - match);
            if (match + sequence.matchLength <= dictEnd)
            {
                memmove(oLitEnd, match, sequence.matchLength);
                return sequenceLength;
            }

            {
                var length1 = (nuint)(dictEnd - match);
                memmove(oLitEnd, match, length1);
                op = oLitEnd + length1;
                sequence.matchLength -= length1;
                match = prefixStart;
            }
        }

        ZSTD_safecopy(
            op,
            oendW,
            match,
            (nint)sequence.matchLength,
            ZstdOverlapE.ZstdOverlapSrcBeforeDst
        );
        return sequenceLength;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_execSequence(
        byte* op,
        byte* oend,
        SeqT sequence,
        byte** litPtr,
        byte* litLimit,
        byte* prefixStart,
        byte* virtualStart,
        byte* dictEnd
    )
    {
        var sequenceLitLength = sequence.litLength;
        var sequenceMatchLength = sequence.matchLength;
        var sequenceOffset = sequence.offset;
        var oLitEnd = op + sequenceLitLength;
        var sequenceLength = sequenceLitLength + sequenceMatchLength;
        /* risk : address space overflow (32-bits) */
        var oMatchEnd = op + sequenceLength;
        /* risk : address space underflow on oend=NULL */
        var oendW = oend - 32;
        var iLitEnd = *litPtr + sequenceLitLength;
        var match = oLitEnd - sequenceOffset;
        assert(op != null);
        assert(oendW < oend);
        if (
            iLitEnd > litLimit
            || oMatchEnd > oendW
            || (MEM_32bits && (nuint)(oend - op) < sequenceLength + 32)
        )
            return ZSTD_execSequenceEnd(
                op,
                oend,
                new SeqT
                {
                    litLength = sequenceLitLength,
                    matchLength = sequenceMatchLength,
                    offset = sequenceOffset
                },
                litPtr,
                litLimit,
                prefixStart,
                virtualStart,
                dictEnd
            );
        assert(op <= oLitEnd);
        assert(oLitEnd < oMatchEnd);
        assert(oMatchEnd <= oend);
        assert(iLitEnd <= litLimit);
        assert(oLitEnd <= oendW);
        assert(oMatchEnd <= oendW);
        assert(32 >= 16);
        ZSTD_copy16(op, *litPtr);
        if (sequenceLitLength > 16)
            ZSTD_wildcopy(
                op + 16,
                *litPtr + 16,
                (nint)(sequenceLitLength - 16),
                ZstdOverlapE.ZstdNoOverlap
            );

        op = oLitEnd;
        *litPtr = iLitEnd;
        if (sequenceOffset > (nuint)(oLitEnd - prefixStart))
        {
            if (sequenceOffset > (nuint)(oLitEnd - virtualStart))
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

            match = dictEnd + (match - prefixStart);
            if (match + sequenceMatchLength <= dictEnd)
            {
                memmove(oLitEnd, match, sequenceMatchLength);
                return sequenceLength;
            }

            {
                var length1 = (nuint)(dictEnd - match);
                memmove(oLitEnd, match, length1);
                op = oLitEnd + length1;
                sequenceMatchLength -= length1;
                match = prefixStart;
            }
        }

        assert(op <= oMatchEnd);
        assert(oMatchEnd <= oendW);
        assert(match >= prefixStart);
        assert(sequenceMatchLength >= 1);
        if (sequenceOffset >= 16)
        {
            ZSTD_wildcopy(op, match, (nint)sequenceMatchLength, ZstdOverlapE.ZstdNoOverlap);
            return sequenceLength;
        }

        assert(sequenceOffset < 16);
        ZSTD_overlapCopy8(ref op, ref match, sequenceOffset);
        if (sequenceMatchLength > 8)
        {
            assert(op < oMatchEnd);
            ZSTD_wildcopy(
                op,
                match,
                (nint)sequenceMatchLength - 8,
                ZstdOverlapE.ZstdOverlapSrcBeforeDst
            );
        }

        return sequenceLength;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_execSequenceSplitLitBuffer(
        byte* op,
        byte* oend,
        byte* oendW,
        SeqT sequence,
        byte** litPtr,
        byte* litLimit,
        byte* prefixStart,
        byte* virtualStart,
        byte* dictEnd
    )
    {
        var oLitEnd = op + sequence.litLength;
        var sequenceLength = sequence.litLength + sequence.matchLength;
        /* risk : address space overflow (32-bits) */
        var oMatchEnd = op + sequenceLength;
        var iLitEnd = *litPtr + sequence.litLength;
        var match = oLitEnd - sequence.offset;
        assert(op != null);
        assert(oendW < oend);
        if (
            iLitEnd > litLimit
            || oMatchEnd > oendW
            || (MEM_32bits && (nuint)(oend - op) < sequenceLength + 32)
        )
            return ZSTD_execSequenceEndSplitLitBuffer(
                op,
                oend,
                oendW,
                sequence,
                litPtr,
                litLimit,
                prefixStart,
                virtualStart,
                dictEnd
            );
        assert(op <= oLitEnd);
        assert(oLitEnd < oMatchEnd);
        assert(oMatchEnd <= oend);
        assert(iLitEnd <= litLimit);
        assert(oLitEnd <= oendW);
        assert(oMatchEnd <= oendW);
        assert(32 >= 16);
        ZSTD_copy16(op, *litPtr);
        if (sequence.litLength > 16)
            ZSTD_wildcopy(
                op + 16,
                *litPtr + 16,
                (nint)(sequence.litLength - 16),
                ZstdOverlapE.ZstdNoOverlap
            );

        op = oLitEnd;
        *litPtr = iLitEnd;
        if (sequence.offset > (nuint)(oLitEnd - prefixStart))
        {
            if (sequence.offset > (nuint)(oLitEnd - virtualStart))
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

            match = dictEnd + (match - prefixStart);
            if (match + sequence.matchLength <= dictEnd)
            {
                memmove(oLitEnd, match, sequence.matchLength);
                return sequenceLength;
            }

            {
                var length1 = (nuint)(dictEnd - match);
                memmove(oLitEnd, match, length1);
                op = oLitEnd + length1;
                sequence.matchLength -= length1;
                match = prefixStart;
            }
        }

        assert(op <= oMatchEnd);
        assert(oMatchEnd <= oendW);
        assert(match >= prefixStart);
        assert(sequence.matchLength >= 1);
        if (sequence.offset >= 16)
        {
            ZSTD_wildcopy(op, match, (nint)sequence.matchLength, ZstdOverlapE.ZstdNoOverlap);
            return sequenceLength;
        }

        assert(sequence.offset < 16);
        ZSTD_overlapCopy8(&op, &match, sequence.offset);
        if (sequence.matchLength > 8)
        {
            assert(op < oMatchEnd);
            ZSTD_wildcopy(
                op,
                match,
                (nint)sequence.matchLength - 8,
                ZstdOverlapE.ZstdOverlapSrcBeforeDst
            );
        }

        return sequenceLength;
    }

    private static void ZSTD_initFseState(
        ZstdFseState* dStatePtr,
        BitDStreamT* bitD,
        ZstdSeqSymbol* dt
    )
    {
        void* ptr = dt;
        var dTableH = (ZstdSeqSymbolHeader*)ptr;
        dStatePtr->state = BIT_readBits(bitD, dTableH->tableLog);
        BIT_reloadDStream(bitD);
        dStatePtr->table = dt + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZSTD_updateFseStateWithDInfo(
        ZstdFseState* dStatePtr,
        BitDStreamT* bitD,
        ushort nextState,
        uint nbBits
    )
    {
        var lowBits = BIT_readBits(bitD, nbBits);
        dStatePtr->state = nextState + lowBits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Inline]
    private static SeqT ZSTD_decodeSequence(SeqStateT* seqState, ZstdLongOffsetE longOffsets)
    {
        SeqT seq;
        var llDInfo = seqState->stateLL.table + seqState->stateLL.state;
        var mlDInfo = seqState->stateML.table + seqState->stateML.state;
        var ofDInfo = seqState->stateOffb.table + seqState->stateOffb.state;
        seq.matchLength = mlDInfo->baseValue;
        seq.litLength = llDInfo->baseValue;
        {
            var ofBase = ofDInfo->baseValue;
            var llBits = llDInfo->nbAdditionalBits;
            var mlBits = mlDInfo->nbAdditionalBits;
            var ofBits = ofDInfo->nbAdditionalBits;
            var totalBits = (byte)(llBits + mlBits + ofBits);
            var llNext = llDInfo->nextState;
            var mlNext = mlDInfo->nextState;
            var ofNext = ofDInfo->nextState;
            uint llnbBits = llDInfo->nbBits;
            uint mlnbBits = mlDInfo->nbBits;
            uint ofnbBits = ofDInfo->nbBits;
            assert(llBits <= 16);
            assert(mlBits <= 16);
            assert(ofBits <= 31);
            {
                nuint offset;
                if (ofBits > 1)
                {
                    if (MEM_32bits && longOffsets != default && ofBits >= 25)
                    {
                        /* Always read extra bits, this keeps the logic simple,
                         * avoids branches, and avoids accidentally reading 0 bits.
                         */
                        uint extraBits = 30 > 25 ? 30 - 25 : 0;
                        offset =
                            ofBase
                            + (
                                BIT_readBitsFast(&seqState->DStream, ofBits - extraBits)
                                << (int)extraBits
                            );
                        BIT_reloadDStream(&seqState->DStream);
                        offset += BIT_readBitsFast(&seqState->DStream, extraBits);
                    }
                    else
                    {
                        offset = ofBase + BIT_readBitsFast(&seqState->DStream, ofBits);
                        if (MEM_32bits)
                            BIT_reloadDStream(&seqState->DStream);
                    }

                    seqState->prevOffset.e2 = seqState->prevOffset.e1;
                    seqState->prevOffset.e1 = seqState->prevOffset.e0;
                    seqState->prevOffset.e0 = offset;
                }
                else
                {
                    var ll0 = llDInfo->baseValue == 0 ? 1U : 0U;
                    if (ofBits == 0)
                    {
                        offset = (&seqState->prevOffset.e0)[ll0];
                        seqState->prevOffset.e1 = (&seqState->prevOffset.e0)[ll0 == 0 ? 1 : 0];
                        seqState->prevOffset.e0 = offset;
                    }
                    else
                    {
                        offset = ofBase + ll0 + BIT_readBitsFast(&seqState->DStream, 1);
                        {
                            var temp =
                                offset == 3
                                    ? seqState->prevOffset.e0 - 1
                                    : (&seqState->prevOffset.e0)[offset];
                            temp += temp == 0 ? 1U : 0U;
                            if (offset != 1)
                                seqState->prevOffset.e2 = seqState->prevOffset.e1;
                            seqState->prevOffset.e1 = seqState->prevOffset.e0;
                            seqState->prevOffset.e0 = offset = temp;
                        }
                    }
                }

                seq.offset = offset;
            }

            if (mlBits > 0)
                seq.matchLength += BIT_readBitsFast(&seqState->DStream, mlBits);
            if (MEM_32bits && mlBits + llBits >= 25 - (30 > 25 ? 30 - 25 : 0))
                BIT_reloadDStream(&seqState->DStream);
            if (MEM_64bits && totalBits >= 57 - (9 + 9 + 8))
                BIT_reloadDStream(&seqState->DStream);
            if (llBits > 0)
                seq.litLength += BIT_readBitsFast(&seqState->DStream, llBits);
            if (MEM_32bits)
                BIT_reloadDStream(&seqState->DStream);
            ZSTD_updateFseStateWithDInfo(&seqState->stateLL, &seqState->DStream, llNext, llnbBits);
            ZSTD_updateFseStateWithDInfo(&seqState->stateML, &seqState->DStream, mlNext, mlnbBits);
            if (MEM_32bits)
                BIT_reloadDStream(&seqState->DStream);
            ZSTD_updateFseStateWithDInfo(
                &seqState->stateOffb,
                &seqState->DStream,
                ofNext,
                ofnbBits
            );
        }

        return seq;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SuppressMessage("ReSharper", "HeuristicUnreachableCode")]
    private static nuint ZSTD_decompressSequences_bodySplitLitBuffer(
        ZstdDCtxS* dctx,
        void* dst,
        nuint maxDstSize,
        void* seqStart,
        nuint seqSize,
        int nbSeq,
        ZstdLongOffsetE isLongOffset,
        // ReSharper disable once UnusedParameter.Local
        int frame
    )
    {
        var ip = (byte*)seqStart;
        var iend = ip + seqSize;
        var ostart = (byte*)dst;
        var oend = ostart + maxDstSize;
        var op = ostart;
        var litPtr = dctx->litPtr;
        var litBufferEnd = dctx->litBufferEnd;
        var prefixStart = (byte*)dctx->prefixStart;
        var vBase = (byte*)dctx->virtualStart;
        var dictEnd = (byte*)dctx->dictEnd;
        if (nbSeq != 0)
        {
            SeqStateT seqState;
            dctx->fseEntropy = 1;
            {
                uint i;
                for (i = 0; i < 3; i++)
                    (&seqState.prevOffset.e0)[i] = dctx->entropy.rep[i];
            }

            if (ERR_isError(BIT_initDStream(&seqState.DStream, ip, (nuint)(iend - ip))))
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

            ZSTD_initFseState(&seqState.stateLL, &seqState.DStream, dctx->LLTptr);
            ZSTD_initFseState(&seqState.stateOffb, &seqState.DStream, dctx->OFTptr);
            ZSTD_initFseState(&seqState.stateML, &seqState.DStream, dctx->MLTptr);
            assert(dst != null);
            {
                var sequence = ZSTD_decodeSequence(&seqState, isLongOffset);
                for (; litPtr + sequence.litLength <= dctx->litBufferEnd;)
                {
                    var oneSeqSize = ZSTD_execSequenceSplitLitBuffer(
                        op,
                        oend,
                        litPtr + sequence.litLength - 32,
                        sequence,
                        &litPtr,
                        litBufferEnd,
                        prefixStart,
                        vBase,
                        dictEnd
                    );
                    if (ERR_isError(oneSeqSize))
                        return oneSeqSize;
                    op += oneSeqSize;
                    if (--nbSeq == 0)
                        break;
                    BIT_reloadDStream(&seqState.DStream);
                    sequence = ZSTD_decodeSequence(&seqState, isLongOffset);
                }

                if (nbSeq > 0)
                {
                    var leftoverLit = (nuint)(dctx->litBufferEnd - litPtr);
                    if (leftoverLit != 0)
                    {
                        if (leftoverLit > (nuint)(oend - op))
                            return unchecked(
                                (nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall)
                            );

                        ZSTD_safecopyDstBeforeSrc(op, litPtr, (nint)leftoverLit);
                        sequence.litLength -= leftoverLit;
                        op += leftoverLit;
                    }

                    litPtr = dctx->litExtraBuffer;
                    litBufferEnd =
                        dctx->litExtraBuffer
                        + (
                            1 << 16 <= 64 ? 64
                            : 1 << 16 <= 128 << 10 ? 1 << 16
                            : 128 << 10
                        );
                    dctx->litBufferLocation = ZstdLitLocationE.ZstdNotInDst;
                    {
                        var oneSeqSize = ZSTD_execSequence(
                            op,
                            oend,
                            sequence,
                            &litPtr,
                            litBufferEnd,
                            prefixStart,
                            vBase,
                            dictEnd
                        );
                        if (ERR_isError(oneSeqSize))
                            return oneSeqSize;
                        op += oneSeqSize;
                        if (--nbSeq != 0)
                            BIT_reloadDStream(&seqState.DStream);
                    }
                }
            }

            if (nbSeq > 0)
                for (;;)
                {
                    var sequence = ZSTD_decodeSequence(&seqState, isLongOffset);
                    var oneSeqSize = ZSTD_execSequence(
                        op,
                        oend,
                        sequence,
                        &litPtr,
                        litBufferEnd,
                        prefixStart,
                        vBase,
                        dictEnd
                    );
                    if (ERR_isError(oneSeqSize))
                        return oneSeqSize;
                    op += oneSeqSize;
                    if (--nbSeq == 0)
                        break;
                    BIT_reloadDStream(&seqState.DStream);
                }

            if (nbSeq != 0)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

            if (BIT_reloadDStream(&seqState.DStream) < BitDStreamStatus.BitDStreamCompleted)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

            {
                uint i;
                for (i = 0; i < 3; i++)
                    dctx->entropy.rep[i] = (uint)(&seqState.prevOffset.e0)[i];
            }
        }

        if (dctx->litBufferLocation == ZstdLitLocationE.ZstdSplit)
        {
            var lastLlSize = (nuint)(litBufferEnd - litPtr);
            if (lastLlSize > (nuint)(oend - op))
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));

            if (op != null)
            {
                memmove(op, litPtr, lastLlSize);
                op += lastLlSize;
            }

            litPtr = dctx->litExtraBuffer;
            litBufferEnd =
                dctx->litExtraBuffer
                + (
                    1 << 16 <= 64 ? 64
                    : 1 << 16 <= 128 << 10 ? 1 << 16
                    : 128 << 10
                );
            dctx->litBufferLocation = ZstdLitLocationE.ZstdNotInDst;
        }

        {
            var lastLlSize = (nuint)(litBufferEnd - litPtr);
            if (lastLlSize > (nuint)(oend - op))
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));

            if (op != null)
            {
                memcpy(op, litPtr, (uint)lastLlSize);
                op += lastLlSize;
            }
        }

        return (nuint)(op - ostart);
    }

#if NET8_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_decompressSequences_body(
        ZstdDCtxS* dctx,
        void* dst,
        nuint maxDstSize,
        void* seqStart,
        nuint seqSize,
        int nbSeq,
        ZstdLongOffsetE isLongOffset,
        // ReSharper disable once UnusedParameter.Local
        int frame
    )
    {
        // HACK, force nbSeq to stack (better register usage)
        Volatile.Read(ref nbSeq);
        var ip = (byte*)seqStart;
        var iend = ip + seqSize;
        var ostart = (byte*)dst;
        var oend =
            dctx->litBufferLocation == ZstdLitLocationE.ZstdNotInDst
                ? ostart + maxDstSize
                : dctx->litBuffer;
        var op = ostart;
        var litPtr = dctx->litPtr;
        var litEnd = litPtr + dctx->litSize;
        var prefixStart = (byte*)dctx->prefixStart;
        var vBase = (byte*)dctx->virtualStart;
        var dictEnd = (byte*)dctx->dictEnd;
        if (nbSeq != 0)
        {
            // ReSharper disable once InlineOutVariableDeclaration
            SeqStateT seqState;
            SkipInit(out seqState);
            dctx->fseEntropy = 1;
            {
                uint i;
                for (i = 0; i < 3; i++)
                    System.Runtime.CompilerServices.Unsafe.Add(ref seqState.prevOffset.e0, (int)i) =
                        dctx->entropy.rep[i];
            }

            if (ERR_isError(BIT_initDStream(ref seqState.DStream, ip, (nuint)(iend - ip))))
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

            ZSTD_initFseState(ref seqState.stateLL, ref seqState.DStream, dctx->LLTptr);
            ZSTD_initFseState(ref seqState.stateOffb, ref seqState.DStream, dctx->OFTptr);
            ZSTD_initFseState(ref seqState.stateML, ref seqState.DStream, dctx->MLTptr);
            assert(dst != null);
            for (;;)
            {
                nuint sequenceLitLength;
                nuint sequenceMatchLength;
                nuint sequenceOffset;
                var llDInfo = seqState.stateLL.table + seqState.stateLL.state;
                var mlDInfo = seqState.stateML.table + seqState.stateML.state;
                var ofDInfo = seqState.stateOffb.table + seqState.stateOffb.state;
                sequenceMatchLength = mlDInfo->baseValue;
                sequenceLitLength = llDInfo->baseValue;
                {
                    var ofBase = ofDInfo->baseValue;
                    var llBits = llDInfo->nbAdditionalBits;
                    var mlBits = mlDInfo->nbAdditionalBits;
                    var ofBits = ofDInfo->nbAdditionalBits;
                    var totalBits = (byte)(llBits + mlBits + ofBits);
                    var llNext = llDInfo->nextState;
                    var mlNext = mlDInfo->nextState;
                    var ofNext = ofDInfo->nextState;
                    uint llnbBits = llDInfo->nbBits;
                    uint mlnbBits = mlDInfo->nbBits;
                    uint ofnbBits = ofDInfo->nbBits;
                    assert(llBits <= 16);
                    assert(mlBits <= 16);
                    assert(ofBits <= 31);
                    {
                        nuint offset;
                        if (ofBits > 1)
                        {
                            if (MEM_32bits && isLongOffset != default && ofBits >= 25)
                            {
                                /* Always read extra bits, this keeps the logic simple,
                                 * avoids branches, and avoids accidentally reading 0 bits.
                                 */
                                uint extraBits = 30 > 25 ? 30 - 25 : 0;
                                offset =
                                    ofBase
                                    + (
                                        BIT_readBitsFast(ref seqState.DStream, ofBits - extraBits)
                                        << (int)extraBits
                                    );
                                BIT_reloadDStream(ref seqState.DStream);
                                offset += BIT_readBitsFast(ref seqState.DStream, extraBits);
                            }
                            else
                            {
                                offset = ofBase + BIT_readBitsFast(ref seqState.DStream, ofBits);
                                if (MEM_32bits)
                                    BIT_reloadDStream(ref seqState.DStream);
                            }

                            seqState.prevOffset.e2 = seqState.prevOffset.e1;
                            seqState.prevOffset.e1 = seqState.prevOffset.e0;
                            seqState.prevOffset.e0 = offset;
                        }
                        else
                        {
                            var ll0 = llDInfo->baseValue == 0 ? 1U : 0U;
                            if (ofBits == 0)
                            {
                                offset = System.Runtime.CompilerServices.Unsafe.Add(
                                    ref seqState.prevOffset.e0,
                                    (int)ll0
                                );
                                seqState.prevOffset.e1 = System.Runtime.CompilerServices.Unsafe.Add(
                                    ref seqState.prevOffset.e0,
                                    ll0 == 0 ? 1 : 0
                                );
                                seqState.prevOffset.e0 = offset;
                            }
                            else
                            {
                                offset = ofBase + ll0 + BIT_readBitsFast(ref seqState.DStream, 1);
                                {
                                    var temp =
                                        offset == 3
                                            ? seqState.prevOffset.e0 - 1
                                            : System.Runtime.CompilerServices.Unsafe.Add(
                                                ref seqState.prevOffset.e0,
                                                (int)offset
                                            );
                                    temp += temp == 0 ? 1U : 0U;
                                    if (offset != 1)
                                        seqState.prevOffset.e2 = seqState.prevOffset.e1;
                                    seqState.prevOffset.e1 = seqState.prevOffset.e0;
                                    seqState.prevOffset.e0 = offset = temp;
                                }
                            }
                        }

                        sequenceOffset = offset;
                    }

                    if (mlBits > 0)
                        sequenceMatchLength += BIT_readBitsFast(ref seqState.DStream, mlBits);
                    if (MEM_32bits && mlBits + llBits >= 25 - (30 > 25 ? 30 - 25 : 0))
                        BIT_reloadDStream(ref seqState.DStream);
                    if (MEM_64bits && totalBits >= 57 - (9 + 9 + 8))
                        BIT_reloadDStream(ref seqState.DStream);
                    if (llBits > 0)
                        sequenceLitLength += BIT_readBitsFast(ref seqState.DStream, llBits);
                    if (MEM_32bits)
                        BIT_reloadDStream(ref seqState.DStream);
                    ZSTD_updateFseStateWithDInfo(
                        ref seqState.stateLL,
                        ref seqState.DStream,
                        llNext,
                        llnbBits
                    );
                    ZSTD_updateFseStateWithDInfo(
                        ref seqState.stateML,
                        ref seqState.DStream,
                        mlNext,
                        mlnbBits
                    );
                    if (MEM_32bits)
                        BIT_reloadDStream(ref seqState.DStream);
                    ZSTD_updateFseStateWithDInfo(
                        ref seqState.stateOffb,
                        ref seqState.DStream,
                        ofNext,
                        ofnbBits
                    );
                }

                nuint oneSeqSize;
                {
                    var oLitEnd = op + sequenceLitLength;
                    oneSeqSize = sequenceLitLength + sequenceMatchLength;
                    /* risk : address space overflow (32-bits) */
                    var oMatchEnd = op + oneSeqSize;
                    /* risk : address space underflow on oend=NULL */
                    var oendW = oend - 32;
                    var iLitEnd = litPtr + sequenceLitLength;
                    var match = oLitEnd - sequenceOffset;
                    assert(op != null);
                    assert(oendW < oend);
                    if (
                        iLitEnd > litEnd
                        || oMatchEnd > oendW
                        || (MEM_32bits && (nuint)(oend - op) < oneSeqSize + 32)
                    )
                    {
                        oneSeqSize = ZSTD_execSequenceEnd(
                            op,
                            oend,
                            new SeqT
                            {
                                litLength = sequenceLitLength,
                                matchLength = sequenceMatchLength,
                                offset = sequenceOffset
                            },
                            &litPtr,
                            litEnd,
                            prefixStart,
                            vBase,
                            dictEnd
                        );
                        goto returnOneSeqSize;
                    }

                    assert(op <= oLitEnd);
                    assert(oLitEnd < oMatchEnd);
                    assert(oMatchEnd <= oend);
                    assert(iLitEnd <= litEnd);
                    assert(oLitEnd <= oendW);
                    assert(oMatchEnd <= oendW);
                    assert(32 >= 16);
                    ZSTD_copy16(op, litPtr);
                    if (sequenceLitLength > 16)
                        ZSTD_wildcopy(
                            op + 16,
                            litPtr + 16,
                            (nint)(sequenceLitLength - 16),
                            ZstdOverlapE.ZstdNoOverlap
                        );

                    var opInner = oLitEnd;
                    litPtr = iLitEnd;
                    if (sequenceOffset > (nuint)(oLitEnd - prefixStart))
                    {
                        if (sequenceOffset > (nuint)(oLitEnd - vBase))
                        {
                            oneSeqSize = unchecked(
                                (nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected)
                            );
                            goto returnOneSeqSize;
                        }

                        match = dictEnd + (match - prefixStart);
                        if (match + sequenceMatchLength <= dictEnd)
                        {
                            memmove(oLitEnd, match, sequenceMatchLength);
                            goto returnOneSeqSize;
                        }

                        {
                            var length1 = (nuint)(dictEnd - match);
                            memmove(oLitEnd, match, length1);
                            opInner = oLitEnd + length1;
                            sequenceMatchLength -= length1;
                            match = prefixStart;
                        }
                    }

                    assert(opInner <= oMatchEnd);
                    assert(oMatchEnd <= oendW);
                    assert(match >= prefixStart);
                    assert(sequenceMatchLength >= 1);
                    if (sequenceOffset >= 16)
                    {
                        ZSTD_wildcopy(
                            opInner,
                            match,
                            (nint)sequenceMatchLength,
                            ZstdOverlapE.ZstdNoOverlap
                        );
                        goto returnOneSeqSize;
                    }

                    assert(sequenceOffset < 16);
                    ZSTD_overlapCopy8(ref opInner, ref match, sequenceOffset);
                    if (sequenceMatchLength > 8)
                    {
                        assert(opInner < oMatchEnd);
                        ZSTD_wildcopy(
                            opInner,
                            match,
                            (nint)sequenceMatchLength - 8,
                            ZstdOverlapE.ZstdOverlapSrcBeforeDst
                        );
                    }

                    returnOneSeqSize: ;
                }

                if (ERR_isError(oneSeqSize))
                    return oneSeqSize;
                op += oneSeqSize;
                if (--nbSeq == 0)
                    break;
                BIT_reloadDStream(ref seqState.DStream);
            }

            if (nbSeq != 0)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

            if (BIT_reloadDStream(ref seqState.DStream) < BitDStreamStatus.BitDStreamCompleted)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

            {
                uint i;
                for (i = 0; i < 3; i++)
                    dctx->entropy.rep[i] = (uint)
                        System.Runtime.CompilerServices.Unsafe.Add(
                            ref seqState.prevOffset.e0,
                            (int)i
                        );
            }
        }

        {
            var lastLlSize = (nuint)(litEnd - litPtr);
            if (lastLlSize > (nuint)(oend - op))
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));

            if (op != null)
            {
                memcpy(op, litPtr, (uint)lastLlSize);
                op += lastLlSize;
            }
        }

        return (nuint)(op - ostart);
    }
#endif

    private static nuint ZSTD_decompressSequences_default(
        ZstdDCtxS* dctx,
        void* dst,
        nuint maxDstSize,
        void* seqStart,
        nuint seqSize,
        int nbSeq,
        ZstdLongOffsetE isLongOffset,
        int frame
    )
    {
        return ZSTD_decompressSequences_body(
            dctx,
            dst,
            maxDstSize,
            seqStart,
            seqSize,
            nbSeq,
            isLongOffset,
            frame
        );
    }

    private static nuint ZSTD_decompressSequencesSplitLitBuffer_default(
        ZstdDCtxS* dctx,
        void* dst,
        nuint maxDstSize,
        void* seqStart,
        nuint seqSize,
        int nbSeq,
        ZstdLongOffsetE isLongOffset,
        int frame
    )
    {
        return ZSTD_decompressSequences_bodySplitLitBuffer(
            dctx,
            dst,
            maxDstSize,
            seqStart,
            seqSize,
            nbSeq,
            isLongOffset,
            frame
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_prefetchMatch(
        nuint prefetchPos,
        SeqT sequence,
        byte* prefixStart,
        byte* dictEnd
    )
    {
        prefetchPos += sequence.litLength;
        {
            var matchBase = sequence.offset > prefetchPos ? dictEnd : prefixStart;
            /* note : this operation can overflow when seq.offset is really too large, which can only happen when input is corrupted.
             * No consequence though : memory address is only used for prefetching, not for dereferencing */
            var match = matchBase + prefetchPos - sequence.offset;
#if NETCOREAPP3_0_OR_GREATER
            if (Sse.IsSupported)
            {
                Sse.Prefetch0(match);
                Sse.Prefetch0(match + 64);
            }
#endif
        }

        return prefetchPos + sequence.matchLength;
    }

    /* This decoding function employs prefetching
     * to reduce latency impact of cache misses.
     * It's generally employed when block contains a significant portion of long-distance matches
     * or when coupled with a "cold" dictionary */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_decompressSequencesLong_body(
        ZstdDCtxS* dctx,
        void* dst,
        nuint maxDstSize,
        void* seqStart,
        nuint seqSize,
        int nbSeq,
        ZstdLongOffsetE isLongOffset,
        // ReSharper disable once UnusedParameter.Local
        int frame
    )
    {
        var ip = (byte*)seqStart;
        var iend = ip + seqSize;
        var ostart = (byte*)dst;
        var oend =
            dctx->litBufferLocation == ZstdLitLocationE.ZstdInDst
                ? dctx->litBuffer
                : ostart + maxDstSize;
        var op = ostart;
        var litPtr = dctx->litPtr;
        var litBufferEnd = dctx->litBufferEnd;
        var prefixStart = (byte*)dctx->prefixStart;
        var dictStart = (byte*)dctx->virtualStart;
        var dictEnd = (byte*)dctx->dictEnd;
        if (nbSeq != 0)
        {
            var sequences = stackalloc SeqT[8];
            var seqAdvance = nbSeq < 8 ? nbSeq : 8;
            SeqStateT seqState;
            int seqNb;
            /* track position relative to prefixStart */
            var prefetchPos = (nuint)(op - prefixStart);
            dctx->fseEntropy = 1;
            {
                int i;
                for (i = 0; i < 3; i++)
                    (&seqState.prevOffset.e0)[i] = dctx->entropy.rep[i];
            }

            assert(dst != null);
            assert(iend >= ip);
            if (ERR_isError(BIT_initDStream(&seqState.DStream, ip, (nuint)(iend - ip))))
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

            ZSTD_initFseState(&seqState.stateLL, &seqState.DStream, dctx->LLTptr);
            ZSTD_initFseState(&seqState.stateOffb, &seqState.DStream, dctx->OFTptr);
            ZSTD_initFseState(&seqState.stateML, &seqState.DStream, dctx->MLTptr);
            for (
                seqNb = 0;
                BIT_reloadDStream(&seqState.DStream) <= BitDStreamStatus.BitDStreamCompleted
                && seqNb < seqAdvance;
                seqNb++
            )
            {
                var sequence = ZSTD_decodeSequence(&seqState, isLongOffset);
                prefetchPos = ZSTD_prefetchMatch(prefetchPos, sequence, prefixStart, dictEnd);
                sequences[seqNb] = sequence;
            }

            if (seqNb < seqAdvance)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

            for (
                ;
                BIT_reloadDStream(&seqState.DStream) <= BitDStreamStatus.BitDStreamCompleted
                && seqNb < nbSeq;
                seqNb++
            )
            {
                var sequence = ZSTD_decodeSequence(&seqState, isLongOffset);
                nuint oneSeqSize;
                if (
                    dctx->litBufferLocation == ZstdLitLocationE.ZstdSplit
                    && litPtr + sequences[(seqNb - 8) & (8 - 1)].litLength > dctx->litBufferEnd
                )
                {
                    /* lit buffer is reaching split point, empty out the first buffer and transition to litExtraBuffer */
                    var leftoverLit = (nuint)(dctx->litBufferEnd - litPtr);
                    if (leftoverLit != 0)
                    {
                        if (leftoverLit > (nuint)(oend - op))
                            return unchecked(
                                (nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall)
                            );

                        ZSTD_safecopyDstBeforeSrc(op, litPtr, (nint)leftoverLit);
                        sequences[(seqNb - 8) & (8 - 1)].litLength -= leftoverLit;
                        op += leftoverLit;
                    }

                    litPtr = dctx->litExtraBuffer;
                    litBufferEnd =
                        dctx->litExtraBuffer
                        + (
                            1 << 16 <= 64 ? 64
                            : 1 << 16 <= 128 << 10 ? 1 << 16
                            : 128 << 10
                        );
                    dctx->litBufferLocation = ZstdLitLocationE.ZstdNotInDst;
                    oneSeqSize = ZSTD_execSequence(
                        op,
                        oend,
                        sequences[(seqNb - 8) & (8 - 1)],
                        &litPtr,
                        litBufferEnd,
                        prefixStart,
                        dictStart,
                        dictEnd
                    );
                    if (ERR_isError(oneSeqSize))
                        return oneSeqSize;
                    prefetchPos = ZSTD_prefetchMatch(prefetchPos, sequence, prefixStart, dictEnd);
                    sequences[seqNb & (8 - 1)] = sequence;
                    op += oneSeqSize;
                }
                else
                {
                    oneSeqSize =
                        dctx->litBufferLocation == ZstdLitLocationE.ZstdSplit
                            ? ZSTD_execSequenceSplitLitBuffer(
                                op,
                                oend,
                                litPtr + sequences[(seqNb - 8) & (8 - 1)].litLength - 32,
                                sequences[(seqNb - 8) & (8 - 1)],
                                &litPtr,
                                litBufferEnd,
                                prefixStart,
                                dictStart,
                                dictEnd
                            )
                            : ZSTD_execSequence(
                                op,
                                oend,
                                sequences[(seqNb - 8) & (8 - 1)],
                                &litPtr,
                                litBufferEnd,
                                prefixStart,
                                dictStart,
                                dictEnd
                            );
                    if (ERR_isError(oneSeqSize))
                        return oneSeqSize;
                    prefetchPos = ZSTD_prefetchMatch(prefetchPos, sequence, prefixStart, dictEnd);
                    sequences[seqNb & (8 - 1)] = sequence;
                    op += oneSeqSize;
                }
            }

            if (seqNb < nbSeq)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

            seqNb -= seqAdvance;
            for (; seqNb < nbSeq; seqNb++)
            {
                var sequence = &sequences[seqNb & (8 - 1)];
                if (
                    dctx->litBufferLocation == ZstdLitLocationE.ZstdSplit
                    && litPtr + sequence->litLength > dctx->litBufferEnd
                )
                {
                    var leftoverLit = (nuint)(dctx->litBufferEnd - litPtr);
                    if (leftoverLit != 0)
                    {
                        if (leftoverLit > (nuint)(oend - op))
                            return unchecked(
                                (nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall)
                            );

                        ZSTD_safecopyDstBeforeSrc(op, litPtr, (nint)leftoverLit);
                        sequence->litLength -= leftoverLit;
                        op += leftoverLit;
                    }

                    litPtr = dctx->litExtraBuffer;
                    litBufferEnd =
                        dctx->litExtraBuffer
                        + (
                            1 << 16 <= 64 ? 64
                            : 1 << 16 <= 128 << 10 ? 1 << 16
                            : 128 << 10
                        );
                    dctx->litBufferLocation = ZstdLitLocationE.ZstdNotInDst;
                    {
                        var oneSeqSize = ZSTD_execSequence(
                            op,
                            oend,
                            *sequence,
                            &litPtr,
                            litBufferEnd,
                            prefixStart,
                            dictStart,
                            dictEnd
                        );
                        if (ERR_isError(oneSeqSize))
                            return oneSeqSize;
                        op += oneSeqSize;
                    }
                }
                else
                {
                    var oneSeqSize =
                        dctx->litBufferLocation == ZstdLitLocationE.ZstdSplit
                            ? ZSTD_execSequenceSplitLitBuffer(
                                op,
                                oend,
                                litPtr + sequence->litLength - 32,
                                *sequence,
                                &litPtr,
                                litBufferEnd,
                                prefixStart,
                                dictStart,
                                dictEnd
                            )
                            : ZSTD_execSequence(
                                op,
                                oend,
                                *sequence,
                                &litPtr,
                                litBufferEnd,
                                prefixStart,
                                dictStart,
                                dictEnd
                            );
                    if (ERR_isError(oneSeqSize))
                        return oneSeqSize;
                    op += oneSeqSize;
                }
            }

            {
                uint i;
                for (i = 0; i < 3; i++)
                    dctx->entropy.rep[i] = (uint)(&seqState.prevOffset.e0)[i];
            }
        }

        if (dctx->litBufferLocation == ZstdLitLocationE.ZstdSplit)
        {
            var lastLlSize = (nuint)(litBufferEnd - litPtr);
            if (lastLlSize > (nuint)(oend - op))
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));

            if (op != null)
            {
                memmove(op, litPtr, lastLlSize);
                op += lastLlSize;
            }

            litPtr = dctx->litExtraBuffer;
            litBufferEnd =
                dctx->litExtraBuffer
                + (
                    1 << 16 <= 64 ? 64
                    : 1 << 16 <= 128 << 10 ? 1 << 16
                    : 128 << 10
                );
        }

        {
            var lastLlSize = (nuint)(litBufferEnd - litPtr);
            if (lastLlSize > (nuint)(oend - op))
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));

            if (op != null)
            {
                memmove(op, litPtr, lastLlSize);
                op += lastLlSize;
            }
        }

        return (nuint)(op - ostart);
    }

    private static nuint ZSTD_decompressSequencesLong_default(
        ZstdDCtxS* dctx,
        void* dst,
        nuint maxDstSize,
        void* seqStart,
        nuint seqSize,
        int nbSeq,
        ZstdLongOffsetE isLongOffset,
        int frame
    )
    {
        return ZSTD_decompressSequencesLong_body(
            dctx,
            dst,
            maxDstSize,
            seqStart,
            seqSize,
            nbSeq,
            isLongOffset,
            frame
        );
    }

    private static nuint ZSTD_decompressSequences(
        ZstdDCtxS* dctx,
        void* dst,
        nuint maxDstSize,
        void* seqStart,
        nuint seqSize,
        int nbSeq,
        ZstdLongOffsetE isLongOffset,
        int frame
    )
    {
        return ZSTD_decompressSequences_default(
            dctx,
            dst,
            maxDstSize,
            seqStart,
            seqSize,
            nbSeq,
            isLongOffset,
            frame
        );
    }

    private static nuint ZSTD_decompressSequencesSplitLitBuffer(
        ZstdDCtxS* dctx,
        void* dst,
        nuint maxDstSize,
        void* seqStart,
        nuint seqSize,
        int nbSeq,
        ZstdLongOffsetE isLongOffset,
        int frame
    )
    {
        return ZSTD_decompressSequencesSplitLitBuffer_default(
            dctx,
            dst,
            maxDstSize,
            seqStart,
            seqSize,
            nbSeq,
            isLongOffset,
            frame
        );
    }

    /* ZSTD_decompressSequencesLong() :
     * decompression function triggered when a minimum share of offsets is considered "long",
     * aka out of cache.
     * note : "long" definition seems overloaded here, sometimes meaning "wider than bitstream register", and sometimes meaning "farther than memory cache distance".
     * This function will try to mitigate main memory latency through the use of prefetching */
    private static nuint ZSTD_decompressSequencesLong(
        ZstdDCtxS* dctx,
        void* dst,
        nuint maxDstSize,
        void* seqStart,
        nuint seqSize,
        int nbSeq,
        ZstdLongOffsetE isLongOffset,
        int frame
    )
    {
        return ZSTD_decompressSequencesLong_default(
            dctx,
            dst,
            maxDstSize,
            seqStart,
            seqSize,
            nbSeq,
            isLongOffset,
            frame
        );
    }

    /*
     * @returns The total size of the history referenceable by zstd, including
     * both the prefix and the extDict. At @p op any offset larger than this
     * is invalid.
     */
    private static nuint ZSTD_totalHistorySize(byte* op, byte* virtualStart)
    {
        return (nuint)(op - virtualStart);
    }

    /* ZSTD_getOffsetInfo() :
     * condition : offTable must be valid
     * @return : "share" of long offsets (arbitrarily defined as > (1<<23))
     *           compared to maximum possible of (1<<OffFSELog),
     *           as well as the maximum number additional bits required.
     */
    private static ZstdOffsetInfo ZSTD_getOffsetInfo(ZstdSeqSymbol* offTable, int nbSeq)
    {
        var info = new ZstdOffsetInfo { longOffsetShare = 0, maxNbAdditionalBits = 0 };
        if (nbSeq != 0)
        {
            void* ptr = offTable;
            var tableLog = ((ZstdSeqSymbolHeader*)ptr)[0].tableLog;
            var table = offTable + 1;
            var max = (uint)(1 << (int)tableLog);
            uint u;
            assert(max <= 1 << 8);
            for (u = 0; u < max; u++)
            {
                info.maxNbAdditionalBits =
                    info.maxNbAdditionalBits > table[u].nbAdditionalBits
                        ? info.maxNbAdditionalBits
                        : table[u].nbAdditionalBits;
                if (table[u].nbAdditionalBits > 22)
                    info.longOffsetShare += 1;
            }

            assert(tableLog <= 8);
            info.longOffsetShare <<= (int)(8 - tableLog);
        }

        return info;
    }

    /*
     * @returns The maximum offset we can decode in one read of our bitstream, without
     * reloading more bits in the middle of the offset bits read. Any offsets larger
     * than this must use the long offset decoder.
     */
    private static nuint ZSTD_maxShortOffset()
    {
        if (MEM_64bits)
            return unchecked((nuint)(-1));

        /* The maximum offBase is (1 << (STREAM_ACCUMULATOR_MIN + 1)) - 1.
         * This offBase would require STREAM_ACCUMULATOR_MIN extra bits.
         * Then we have to subtract ZSTD_REP_NUM to get the maximum possible offset.
         */
        var maxOffbase = ((nuint)1 << (int)((uint)(MEM_32bits ? 25 : 57) + 1)) - 1;
        var maxOffset = maxOffbase - 3;
        assert(ZSTD_highbit32((uint)maxOffbase) == (uint)(MEM_32bits ? 25 : 57));
        return maxOffset;
    }

    /* ZSTD_decompressBlock_internal() :
     * decompress block, starting at `src`,
     * into destination buffer `dst`.
     * @return : decompressed block size,
     *           or an error code (which can be tested using ZSTD_isError())
     */
    private static nuint ZSTD_decompressBlock_internal(
        ZstdDCtxS* dctx,
        void* dst,
        nuint dstCapacity,
        void* src,
        nuint srcSize,
        int frame,
        StreamingOperation streaming
    )
    {
        var ip = (byte*)src;
        if (srcSize > 1 << 17)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));

        {
            var litCSize = ZSTD_decodeLiteralsBlock(
                dctx,
                src,
                srcSize,
                dst,
                dstCapacity,
                streaming
            );
            if (ERR_isError(litCSize))
                return litCSize;
            ip += litCSize;
            srcSize -= litCSize;
        }

        {
            /* Compute the maximum block size, which must also work when !frame and fParams are unset.
             * Additionally, take the min with dstCapacity to ensure that the totalHistorySize fits in a size_t.
             */
            var blockSizeMax =
                dstCapacity < (frame != 0 ? dctx->fParams.blockSizeMax : 1 << 17) ? dstCapacity
                : frame != 0 ? dctx->fParams.blockSizeMax
                : 1 << 17;
            var totalHistorySize = ZSTD_totalHistorySize(
                (byte*)dst + blockSizeMax,
                (byte*)dctx->virtualStart
            );
            /* isLongOffset must be true if there are long offsets.
             * Offsets are long if they are larger than ZSTD_maxShortOffset().
             * We don't expect that to be the case in 64-bit mode.
             *
             * We check here to see if our history is large enough to allow long offsets.
             * If it isn't, then we can't possible have (valid) long offsets. If the offset
             * is invalid, then it is okay to read it incorrectly.
             *
             * If isLongOffsets is true, then we will later check our decoding table to see
             * if it is even possible to generate long offsets.
             */
            var isLongOffset = (ZstdLongOffsetE)(
                MEM_32bits && totalHistorySize > ZSTD_maxShortOffset() ? 1 : 0
            );
            var usePrefetchDecoder = dctx->ddictIsCold;
            int nbSeq;
            var seqHSize = ZSTD_decodeSeqHeaders(dctx, &nbSeq, ip, srcSize);
            if (ERR_isError(seqHSize))
                return seqHSize;
            ip += seqHSize;
            srcSize -= seqHSize;
            if ((dst == null || dstCapacity == 0) && nbSeq > 0)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));

            if (
                MEM_64bits
                && sizeof(nuint) == sizeof(void*)
                && unchecked((nuint)(-1)) - (nuint)dst < 1 << 20
            )
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));

            if (
                isLongOffset != default
                || (usePrefetchDecoder == 0 && totalHistorySize > 1U << 24 && nbSeq > 8)
            )
            {
                var info = ZSTD_getOffsetInfo(dctx->OFTptr, nbSeq);
                if (
                    isLongOffset != default
                    && info.maxNbAdditionalBits <= (uint)(MEM_32bits ? 25 : 57)
                )
                    isLongOffset = ZstdLongOffsetE.ZstdLoIsRegularOffset;

                if (usePrefetchDecoder == 0)
                {
                    /* heuristic values, correspond to 2.73% and 7.81% */
                    var minShare = (uint)(MEM_64bits ? 7 : 20);
                    usePrefetchDecoder = info.longOffsetShare >= minShare ? 1 : 0;
                }
            }

            dctx->ddictIsCold = 0;
            if (usePrefetchDecoder != 0)
                return ZSTD_decompressSequencesLong(
                    dctx,
                    dst,
                    dstCapacity,
                    ip,
                    srcSize,
                    nbSeq,
                    isLongOffset,
                    frame
                );

            if (dctx->litBufferLocation == ZstdLitLocationE.ZstdSplit)
                return ZSTD_decompressSequencesSplitLitBuffer(
                    dctx,
                    dst,
                    dstCapacity,
                    ip,
                    srcSize,
                    nbSeq,
                    isLongOffset,
                    frame
                );
            return ZSTD_decompressSequences(
                dctx,
                dst,
                dstCapacity,
                ip,
                srcSize,
                nbSeq,
                isLongOffset,
                frame
            );
        }
    }

    /*! ZSTD_checkContinuity() :
     *  check if next `dst` follows previous position, where decompression ended.
     *  If yes, do nothing (continue on current segment).
     *  If not, classify previous segment as "external dictionary", and start a new segment.
     *  This function cannot fail. */
    private static void ZSTD_checkContinuity(ZstdDCtxS* dctx, void* dst, nuint dstSize)
    {
        if (dst != dctx->previousDstEnd && dstSize > 0)
        {
            dctx->dictEnd = dctx->previousDstEnd;
            dctx->virtualStart =
                (sbyte*)dst - ((sbyte*)dctx->previousDstEnd - (sbyte*)dctx->prefixStart);
            dctx->prefixStart = dst;
            dctx->previousDstEnd = dst;
        }
    }

    /* Internal definition of ZSTD_decompressBlock() to avoid deprecation warnings. */
    private static nuint ZSTD_decompressBlock_deprecated(
        ZstdDCtxS* dctx,
        void* dst,
        nuint dstCapacity,
        void* src,
        nuint srcSize
    )
    {
        nuint dSize;
        ZSTD_checkContinuity(dctx, dst, dstCapacity);
        dSize = ZSTD_decompressBlock_internal(
            dctx,
            dst,
            dstCapacity,
            src,
            srcSize,
            0,
            StreamingOperation.NotStreaming
        );
        dctx->previousDstEnd = (sbyte*)dst + dSize;
        return dSize;
    }

    /* NOTE: Must just wrap ZSTD_decompressBlock_deprecated() */
    public static nuint ZSTD_decompressBlock(
        ZstdDCtxS* dctx,
        void* dst,
        nuint dstCapacity,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_decompressBlock_deprecated(dctx, dst, dstCapacity, src, srcSize);
    }

    private static void ZSTD_initFseState(
        ref ZstdFseState dStatePtr,
        ref BitDStreamT bitD,
        ZstdSeqSymbol* dt
    )
    {
        void* ptr = dt;
        var dTableH = (ZstdSeqSymbolHeader*)ptr;
        dStatePtr.state = BIT_readBits(ref bitD, dTableH->tableLog);
        BIT_reloadDStream(ref bitD);
        dStatePtr.table = dt + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZSTD_updateFseStateWithDInfo(
        ref ZstdFseState dStatePtr,
        ref BitDStreamT bitD,
        ushort nextState,
        uint nbBits
    )
    {
        var lowBits = BIT_readBits(ref bitD, nbBits);
        dStatePtr.state = nextState + lowBits;
    }

    /*! ZSTD_overlapCopy8() :
     *  Copies 8 bytes from ip to op and updates op and ip where ip <= op.
     *  If the offset is < 8 then the offset is spread to at least 8 bytes.
     *
     *  Precondition: *ip <= *op
     *  Postcondition: *op - *op >= 8
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZSTD_overlapCopy8(ref byte* op, ref byte* ip, nuint offset)
    {
        assert(ip <= op);
        if (offset < 8)
        {
            var sub2 = Dec64Table[offset];
            op[0] = ip[0];
            op[1] = ip[1];
            op[2] = ip[2];
            op[3] = ip[3];
            ip += Dec32Table[offset];
            ZSTD_copy4(op + 4, ip);
            ip -= sub2;
        }
        else
        {
            ZSTD_copy8(op, ip);
        }

        ip += 8;
        op += 8;
        assert(op - ip >= 8);
    }

#if !NET8_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_decompressSequences_body(
        ZSTD_DCtx_s* dctx,
        void* dst,
        nuint maxDstSize,
        void* seqStart,
        nuint seqSize,
        int nbSeq,
        ZSTD_longOffset_e isLongOffset,
        int frame
    )
    {
        // HACK, force nbSeq to stack (better register usage)
        System.Threading.Volatile.Read(ref nbSeq);
        byte* ip = (byte*)(seqStart);
        byte* iend = ip + seqSize;
        byte* ostart = (byte*)(dst);
        byte* oend =
            dctx->litBufferLocation == ZSTD_litLocation_e.ZSTD_not_in_dst
                ? ostart + maxDstSize
                : dctx->litBuffer;
        byte* op = ostart;
        byte* litPtr = dctx->litPtr;
        byte* litEnd = litPtr + dctx->litSize;
        byte* prefixStart = (byte*)((dctx->prefixStart));
        byte* vBase = (byte*)((dctx->virtualStart));
        byte* dictEnd = (byte*)((dctx->dictEnd));
        if (((nbSeq) != 0))
        {
            seqState_t seqState;
            dctx->fseEntropy = 1;
            {
                uint i;
                for (i = 0; i < 3; i++)
                    (&seqState.prevOffset.e0)[i] = dctx->entropy.rep[i];
            }

            if (ERR_isError(BIT_initDStream(&seqState.DStream, ip, (nuint)(iend - ip))))
            {
                return (unchecked((nuint)(-(int)(ZSTD_ErrorCode.ZSTD_error_corruption_detected))));
            }

            ZSTD_initFseState(&seqState.stateLL, &seqState.DStream, dctx->LLTptr);
            ZSTD_initFseState(&seqState.stateOffb, &seqState.DStream, dctx->OFTptr);
            ZSTD_initFseState(&seqState.stateML, &seqState.DStream, dctx->MLTptr);
            assert(dst != (null));
            for (; ; )
            {
                seq_t sequence = ZSTD_decodeSequence(&seqState, isLongOffset);
                nuint oneSeqSize;
                {
                    var sequence_litLength = sequence.litLength;
                    var sequence_matchLength = sequence.matchLength;
                    var sequence_offset = sequence.offset;
                    byte* oLitEnd = op + sequence_litLength;
                    oneSeqSize = sequence_litLength + sequence_matchLength;
                    /* risk : address space overflow (32-bits) */
                    byte* oMatchEnd = op + oneSeqSize;
                    /* risk : address space underflow on oend=NULL */
                    byte* oend_w = oend - 32;
                    byte* iLitEnd = litPtr + sequence_litLength;
                    byte* match = oLitEnd - sequence_offset;
                    assert(op != (null));
                    assert(oend_w < oend);
                    if (
                        (
                            iLitEnd > litEnd
                            || oMatchEnd > oend_w
                            || (MEM_32bits && (nuint)((oend - op)) < oneSeqSize + 32)
                        )
                    )
                    {
                        oneSeqSize = ZSTD_execSequenceEnd(
                            op,
                            oend,
                            new seq_t
                            {
                                litLength = sequence_litLength,
                                matchLength = sequence_matchLength,
                                offset = sequence_offset,
                            },
                            &litPtr,
                            litEnd,
                            prefixStart,
                            vBase,
                            dictEnd
                        );
                        goto returnOneSeqSize;
                    }

                    assert(op <= oLitEnd);
                    assert(oLitEnd < oMatchEnd);
                    assert(oMatchEnd <= oend);
                    assert(iLitEnd <= litEnd);
                    assert(oLitEnd <= oend_w);
                    assert(oMatchEnd <= oend_w);
                    assert(32 >= 16);
                    ZSTD_copy16(op, (litPtr));
                    if ((sequence_litLength > 16))
                    {
                        ZSTD_wildcopy(
                            op + 16,
                            (litPtr) + 16,
                            (nint)(sequence_litLength - 16),
                            ZSTD_overlap_e.ZSTD_no_overlap
                        );
                    }

                    byte* opInner = oLitEnd;
                    litPtr = iLitEnd;
                    if (sequence_offset > (nuint)((oLitEnd - prefixStart)))
                    {
                        if ((sequence_offset > (nuint)((oLitEnd - vBase))))
                        {
                            oneSeqSize = (
                                unchecked(
                                    (nuint)(-(int)(ZSTD_ErrorCode.ZSTD_error_corruption_detected))
                                )
                            );
                            goto returnOneSeqSize;
                        }

                        match = dictEnd + (match - prefixStart);
                        if (match + sequence_matchLength <= dictEnd)
                        {
                            memmove((oLitEnd), (match), (ulong)((sequence_matchLength)));
                            goto returnOneSeqSize;
                        }

                        {
                            nuint length1 = (nuint)(dictEnd - match);
                            memmove((oLitEnd), (match), (ulong)((length1)));
                            opInner = oLitEnd + length1;
                            sequence_matchLength -= length1;
                            match = prefixStart;
                        }
                    }

                    assert(opInner <= oMatchEnd);
                    assert(oMatchEnd <= oend_w);
                    assert(match >= prefixStart);
                    assert(sequence_matchLength >= 1);
                    if ((sequence_offset >= 16))
                    {
                        ZSTD_wildcopy(
                            opInner,
                            match,
                            (nint)(sequence_matchLength),
                            ZSTD_overlap_e.ZSTD_no_overlap
                        );
                        goto returnOneSeqSize;
                    }

                    assert(sequence_offset < 16);
                    ZSTD_overlapCopy8(ref opInner, ref match, sequence_offset);
                    if (sequence_matchLength > 8)
                    {
                        assert(opInner < oMatchEnd);
                        ZSTD_wildcopy(
                            opInner,
                            match,
                            (nint)(sequence_matchLength) - 8,
                            ZSTD_overlap_e.ZSTD_overlap_src_before_dst
                        );
                    }

                    returnOneSeqSize:
                    ;
                }

                if ((ERR_isError(oneSeqSize)))
                    return oneSeqSize;
                op += oneSeqSize;
                if ((((--nbSeq) == 0)))
                    break;
                BIT_reloadDStream(&(seqState.DStream));
            }

            if (((nbSeq) != 0))
            {
                return (unchecked((nuint)(-(int)(ZSTD_ErrorCode.ZSTD_error_corruption_detected))));
            }

            if (BIT_reloadDStream(&seqState.DStream) < BIT_DStream_status.BIT_DStream_completed)
            {
                return (unchecked((nuint)(-(int)(ZSTD_ErrorCode.ZSTD_error_corruption_detected))));
            }

            {
                uint i;
                for (i = 0; i < 3; i++)
                    dctx->entropy.rep[i] = (uint)(((&seqState.prevOffset.e0)[i]));
            }
        }

        {
            nuint lastLLSize = (nuint)(litEnd - litPtr);
            if (lastLLSize > (nuint)((oend - op)))
            {
                return (unchecked((nuint)(-(int)(ZSTD_ErrorCode.ZSTD_error_dstSize_tooSmall))));
            }

            if (op != (null))
            {
                memcpy((op), (litPtr), (uint)((lastLLSize)));
                op += lastLLSize;
            }
        }

        return (nuint)(op - ostart);
    }
#endif
}