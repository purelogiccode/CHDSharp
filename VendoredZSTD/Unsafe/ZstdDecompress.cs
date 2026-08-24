using static VendoredZSTD.UnsafeHelper;

namespace VendoredZSTD.Unsafe;

public static unsafe partial class Methods
{
    /* Hash function to determine starting position of dict insertion within the table
     * Returns an index between [0, hashSet->ddictPtrTableSize]
     */
    private static nuint ZSTD_DDictHashSet_getIndex(ZstdDDictHashSet* hashSet, uint dictId)
    {
        var hash = ZSTD_XXH64(&dictId, sizeof(uint), 0);
        return (nuint)(hash & (hashSet->ddictPtrTableSize - 1));
    }

    /* Adds DDict to a hashset without resizing it.
     * If inserting a DDict with a dictID that already exists in the set, replaces the one in the set.
     * Returns 0 if successful, or a zstd error code if something went wrong.
     */
    private static nuint ZSTD_DDictHashSet_emplaceDDict(ZstdDDictHashSet* hashSet, ZstdDDictS* ddict)
    {
        var dictId = ZSTD_getDictID_fromDDict(ddict);
        var idx = ZSTD_DDictHashSet_getIndex(hashSet, dictId);
        var idxRangeMask = hashSet->ddictPtrTableSize - 1;
        if (hashSet->ddictPtrCount == hashSet->ddictPtrTableSize)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorGeneric));
        }

        while (hashSet->ddictPtrTable[idx] != null)
        {
            if (ZSTD_getDictID_fromDDict(hashSet->ddictPtrTable[idx]) == dictId)
            {
                hashSet->ddictPtrTable[idx] = ddict;
                return 0;
            }

            idx &= idxRangeMask;
            idx++;
        }

        hashSet->ddictPtrTable[idx] = ddict;
        hashSet->ddictPtrCount++;
        return 0;
    }

    /* Expands hash table by factor of DDICT_HASHSET_RESIZE_FACTOR and
     * rehashes all values, allocates new table, frees old table.
     * Returns 0 on success, otherwise a zstd error code.
     */
    private static nuint ZSTD_DDictHashSet_expand(ZstdDDictHashSet* hashSet, ZstdCustomMem customMem)
    {
        var newTableSize = hashSet->ddictPtrTableSize * 2;
        var newTable = (ZstdDDictS**)ZSTD_customCalloc((nuint)sizeof(ZstdDDictS*) * newTableSize, customMem);
        var oldTable = hashSet->ddictPtrTable;
        var oldTableSize = hashSet->ddictPtrTableSize;
        nuint i;
        if (newTable == null)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMemoryAllocation));
        }

        hashSet->ddictPtrTable = newTable;
        hashSet->ddictPtrTableSize = newTableSize;
        hashSet->ddictPtrCount = 0;
        for (i = 0; i < oldTableSize; ++i)
        {
            if (oldTable[i] != null)
            {
                var errCode = ZSTD_DDictHashSet_emplaceDDict(hashSet, oldTable[i]);
                if (ERR_isError(errCode))
                {
                    return errCode;
                }
            }
        }

        ZSTD_customFree(oldTable, customMem);
        return 0;
    }

    /* Fetches a DDict with the given dictID
     * Returns the ZSTD_DDict* with the requested dictID. If it doesn't exist, then returns NULL.
     */
    private static ZstdDDictS* ZSTD_DDictHashSet_getDDict(ZstdDDictHashSet* hashSet, uint dictId)
    {
        var idx = ZSTD_DDictHashSet_getIndex(hashSet, dictId);
        var idxRangeMask = hashSet->ddictPtrTableSize - 1;
        for (; ; )
        {
            nuint currDictId = ZSTD_getDictID_fromDDict(hashSet->ddictPtrTable[idx]);
            if (currDictId == dictId || currDictId == 0)
            {
                break;
            }
            else
            {
                idx &= idxRangeMask;
                idx++;
            }
        }

        return hashSet->ddictPtrTable[idx];
    }

    /* Allocates space for and returns a ddict hash set
     * The hash set's ZSTD_DDict* table has all values automatically set to NULL to begin with.
     * Returns NULL if allocation failed.
     */
    private static ZstdDDictHashSet* ZSTD_createDDictHashSet(ZstdCustomMem customMem)
    {
        var ret = (ZstdDDictHashSet*)ZSTD_customMalloc((nuint)sizeof(ZstdDDictHashSet), customMem);
        if (ret == null)
            return null;

        ret->ddictPtrTable = (ZstdDDictS**)ZSTD_customCalloc((nuint)(64 * sizeof(ZstdDDictS*)), customMem);
        if (ret->ddictPtrTable == null)
        {
            ZSTD_customFree(ret, customMem);
            return null;
        }

        ret->ddictPtrTableSize = 64;
        ret->ddictPtrCount = 0;
        return ret;
    }

    /* Frees the table of ZSTD_DDict* within a hashset, then frees the hashset itself.
     * Note: The ZSTD_DDict* within the table are NOT freed.
     */
    private static void ZSTD_freeDDictHashSet(ZstdDDictHashSet* hashSet, ZstdCustomMem customMem)
    {
        if (hashSet != null && hashSet->ddictPtrTable != null)
        {
            ZSTD_customFree(hashSet->ddictPtrTable, customMem);
        }

        if (hashSet != null)
        {
            ZSTD_customFree(hashSet, customMem);
        }
    }

    /* Public function: Adds a DDict into the ZSTD_DDictHashSet, possibly triggering a resize of the hash set.
     * Returns 0 on success, or a ZSTD error.
     */
    private static nuint ZSTD_DDictHashSet_addDDict(ZstdDDictHashSet* hashSet, ZstdDDictS* ddict, ZstdCustomMem customMem)
    {
        if (hashSet->ddictPtrCount * 4 / hashSet->ddictPtrTableSize * 3 != 0)
        {
            var errCode = ZSTD_DDictHashSet_expand(hashSet, customMem);
            if (ERR_isError(errCode))
            {
                return errCode;
            }
        }

        {
            var errCode = ZSTD_DDictHashSet_emplaceDDict(hashSet, ddict);
            if (ERR_isError(errCode))
            {
                return errCode;
            }
        }

        return 0;
    }

    /*-*************************************************************
     *   Context management
     ***************************************************************/
    public static nuint ZSTD_sizeof_DCtx(ZstdDCtxS* dctx)
    {
        if (dctx == null)
            return 0;

        return (nuint)sizeof(ZstdDCtxS) + ZSTD_sizeof_DDict(dctx->ddictLocal) + dctx->inBuffSize + dctx->outBuffSize;
    }

    public static nuint ZSTD_estimateDCtxSize()
    {
        return (nuint)sizeof(ZstdDCtxS);
    }

    private static nuint ZSTD_startingInputLength(ZstdFormatE format)
    {
        var startingInputLength = (nuint)(format == ZstdFormatE.ZstdFZstd1 ? 5 : 1);
        assert(format is ZstdFormatE.ZstdFZstd1 or ZstdFormatE.ZstdFZstd1Magicless);
        return startingInputLength;
    }

    private static void ZSTD_DCtx_resetParameters(ZstdDCtxS* dctx)
    {
        assert(dctx->streamStage == ZstdDStreamStage.ZdssInit);
        dctx->format = ZstdFormatE.ZstdFZstd1;
        dctx->maxWindowSize = ((uint)1 << 27) + 1;
        dctx->outBufferMode = ZstdBufferModeE.ZstdBmBuffered;
        dctx->forceIgnoreChecksum = ZstdForceIgnoreChecksumE.ZstdDValidateChecksum;
        dctx->refMultipleDDicts = ZstdRefMultipleDDictsE.ZstdRmdRefSingleDDict;
        dctx->disableHufAsm = 0;
        dctx->maxBlockSizeParam = 0;
    }

    private static void ZSTD_initDCtx_internal(ZstdDCtxS* dctx)
    {
        dctx->staticSize = 0;
        dctx->ddict = null;
        dctx->ddictLocal = null;
        dctx->dictEnd = null;
        dctx->ddictIsCold = 0;
        dctx->dictUses = ZstdDictUsesE.ZstdDontUse;
        dctx->inBuff = null;
        dctx->inBuffSize = 0;
        dctx->outBuffSize = 0;
        dctx->streamStage = ZstdDStreamStage.ZdssInit;
        dctx->noForwardProgress = 0;
        dctx->oversizedDuration = 0;
        dctx->isFrameDecompression = 1;
        dctx->ddictSet = null;
        ZSTD_DCtx_resetParameters(dctx);
    }

    public static ZstdDCtxS* ZSTD_initStaticDCtx(void* workspace, nuint workspaceSize)
    {
        var dctx = (ZstdDCtxS*)workspace;
        if (((nuint)workspace & 7) != 0)
            return null;
        if (workspaceSize < (nuint)sizeof(ZstdDCtxS))
            return null;

        ZSTD_initDCtx_internal(dctx);
        dctx->staticSize = workspaceSize;
        dctx->inBuff = (sbyte*)(dctx + 1);
        return dctx;
    }

    private static ZstdDCtxS* ZSTD_createDCtx_internal(ZstdCustomMem customMem)
    {
        if (((customMem.customAlloc == null ? 1 : 0) ^ (customMem.customFree == null ? 1 : 0)) != 0)
            return null;

        {
            var dctx = (ZstdDCtxS*)ZSTD_customMalloc((nuint)sizeof(ZstdDCtxS), customMem);
            if (dctx == null)
                return null;

            dctx->customMem = customMem;
            ZSTD_initDCtx_internal(dctx);
            return dctx;
        }
    }

    public static ZstdDCtxS* ZSTD_createDCtx_advanced(ZstdCustomMem customMem)
    {
        return ZSTD_createDCtx_internal(customMem);
    }

    public static ZstdDCtxS* ZSTD_createDCtx()
    {
        return ZSTD_createDCtx_internal(ZstdDefaultCMem);
    }

    private static void ZSTD_clearDict(ZstdDCtxS* dctx)
    {
        ZSTD_freeDDict(dctx->ddictLocal);
        dctx->ddictLocal = null;
        dctx->ddict = null;
        dctx->dictUses = ZstdDictUsesE.ZstdDontUse;
    }

    public static nuint ZSTD_freeDCtx(ZstdDCtxS* dctx)
    {
        if (dctx == null)
            return 0;

        if (dctx->staticSize != 0)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMemoryAllocation));
        }

        {
            var cMem = dctx->customMem;
            ZSTD_clearDict(dctx);
            ZSTD_customFree(dctx->inBuff, cMem);
            dctx->inBuff = null;
            if (dctx->ddictSet != null)
            {
                ZSTD_freeDDictHashSet(dctx->ddictSet, cMem);
                dctx->ddictSet = null;
            }

            ZSTD_customFree(dctx, cMem);
            return 0;
        }
    }

    /* no longer useful */
    public static void ZSTD_copyDCtx(ZstdDCtxS* dstDCtx, ZstdDCtxS* srcDCtx)
    {
        var toCopy = (nuint)((sbyte*)&dstDCtx->inBuff - (sbyte*)dstDCtx);
        memcpy(dstDCtx, srcDCtx, (uint)toCopy);
    }

    /* Given a dctx with a digested frame params, re-selects the correct ZSTD_DDict based on
     * the requested dict ID from the frame. If there exists a reference to the correct ZSTD_DDict, then
     * accordingly sets the ddict to be used to decompress the frame.
     *
     * If no DDict is found, then no action is taken, and the ZSTD_DCtx::ddict remains as-is.
     *
     * ZSTD_d_refMultipleDDicts must be enabled for this function to be called.
     */
    private static void ZSTD_DCtx_selectFrameDDict(ZstdDCtxS* dctx)
    {
        assert(dctx->refMultipleDDicts != default && dctx->ddictSet != null);
        if (dctx->ddict != null)
        {
            var frameDDict = ZSTD_DDictHashSet_getDDict(dctx->ddictSet, dctx->fParams.dictID);
            if (frameDDict != null)
            {
                ZSTD_clearDict(dctx);
                dctx->dictID = dctx->fParams.dictID;
                dctx->ddict = frameDDict;
                dctx->dictUses = ZstdDictUsesE.ZstdUseIndefinitely;
            }
        }
    }

    /*! ZSTD_isFrame() :
     *  Tells if the content of `buffer` starts with a valid Frame Identifier.
     *  Note : Frame Identifier is 4 bytes. If `size < 4`, @return will always be 0.
     *  Note 2 : Legacy Frame Identifiers are considered valid only if Legacy Support is enabled.
     *  Note 3 : Skippable Frame Identifiers are considered valid. */
    public static uint ZSTD_isFrame(void* buffer, nuint size)
    {
        if (size < 4)
            return 0;

        {
            var magic = MEM_readLE32(buffer);
            if (magic == 0xFD2FB528)
                return 1;
            if ((magic & 0xFFFFFFF0) == 0x184D2A50)
                return 1;
        }

        return 0;
    }

    /*! ZSTD_isSkippableFrame() :
     *  Tells if the content of `buffer` starts with a valid Frame Identifier for a skippable frame.
     *  Note : Frame Identifier is 4 bytes. If `size < 4`, @return will always be 0.
     */
    public static uint ZSTD_isSkippableFrame(void* buffer, nuint size)
    {
        if (size < 4)
            return 0;

        {
            var magic = MEM_readLE32(buffer);
            if ((magic & 0xFFFFFFF0) == 0x184D2A50)
                return 1;
        }

        return 0;
    }

    /** ZSTD_frameHeaderSize_internal() :
     *  srcSize must be large enough to reach header size fields.
     *  note : only works for formats ZSTD_f_zstd1 and ZSTD_f_zstd1_magicless.
     * @return : size of the Frame Header
     *           or an error code, which can be tested with ZSTD_isError() */
    private static nuint ZSTD_frameHeaderSize_internal(void* src, nuint srcSize, ZstdFormatE format)
    {
        var minInputSize = ZSTD_startingInputLength(format);
        if (srcSize < minInputSize)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
        }

        {
            var fhd = ((byte*)src)[minInputSize - 1];
            var dictId = (uint)(fhd & 3);
            var singleSegment = (uint)((fhd >> 5) & 1);
            var fcsId = (uint)(fhd >> 6);
            return minInputSize + (nuint)(singleSegment == 0 ? 1 : 0) + ZstdDidFieldSize[dictId] + ZstdFcsFieldSize[fcsId] + (nuint)(singleSegment != 0 && fcsId == 0 ? 1 : 0);
        }
    }

    /** ZSTD_frameHeaderSize() :
     *  srcSize must be >= ZSTD_frameHeaderSize_prefix.
     * @return : size of the Frame Header,
     *           or an error code (if srcSize is too small) */
    public static nuint ZSTD_frameHeaderSize(void* src, nuint srcSize)
    {
        return ZSTD_frameHeaderSize_internal(src, srcSize, ZstdFormatE.ZstdFZstd1);
    }

    /** ZSTD_getFrameHeader_advanced() :
     *  decode Frame Header, or require larger `srcSize`.
     *  note : only works for formats ZSTD_f_zstd1 and ZSTD_f_zstd1_magicless
     * @return : 0, `zfhPtr` is correctly filled,
     *          >0, `srcSize` is too small, value is wanted `srcSize` amount,
     **           or an error code, which can be tested using ZSTD_isError() */
    public static nuint ZSTD_getFrameHeader_advanced(ZstdFrameHeader* zfhPtr, void* src, nuint srcSize, ZstdFormatE format)
    {
        var ip = (byte*)src;
        var minInputSize = ZSTD_startingInputLength(format);
        if (srcSize > 0)
        {
            if (src == null)
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorGeneric));
            }
        }

        if (srcSize < minInputSize)
        {
            if (srcSize > 0 && format != ZstdFormatE.ZstdFZstd1Magicless)
            {
                /* when receiving less than @minInputSize bytes,
                 * control these bytes at least correspond to a supported magic number
                 * in order to error out early if they don't.
                 **/
                var toCopy = 4 < srcSize ? 4 : srcSize;
                var hbuf = stackalloc byte[4];
                MEM_writeLE32(hbuf, 0xFD2FB528);
                assert(src != null);
                memcpy(hbuf, src, (uint)toCopy);
                if (MEM_readLE32(hbuf) != 0xFD2FB528)
                {
                    MEM_writeLE32(hbuf, 0x184D2A50);
                    memcpy(hbuf, src, (uint)toCopy);
                    if ((MEM_readLE32(hbuf) & 0xFFFFFFF0) != 0x184D2A50)
                    {
                        return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorPrefixUnknown));
                    }
                }
            }

            return minInputSize;
        }

        *zfhPtr = new ZstdFrameHeader();
        if (format != ZstdFormatE.ZstdFZstd1Magicless && MEM_readLE32(src) != 0xFD2FB528)
        {
            if ((MEM_readLE32(src) & 0xFFFFFFF0) == 0x184D2A50)
            {
                if (srcSize < 8)
                    return 8;

                *zfhPtr = new ZstdFrameHeader
                {
                    frameType = ZstdFrameTypeE.ZstdSkippableFrame,
                    dictID = MEM_readLE32(src) - 0x184D2A50,
                    headerSize = 8,
                    frameContentSize = MEM_readLE32((sbyte*)src + 4)
                };
                return 0;
            }

            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorPrefixUnknown));
        }

        {
            var fhsize = ZSTD_frameHeaderSize_internal(src, srcSize, format);
            if (srcSize < fhsize)
                return fhsize;

            zfhPtr->headerSize = (uint)fhsize;
        }

        {
            var fhdByte = ip[minInputSize - 1];
            var pos = minInputSize;
            var dictIdSizeCode = (uint)(fhdByte & 3);
            var checksumFlag = (uint)((fhdByte >> 2) & 1);
            var singleSegment = (uint)((fhdByte >> 5) & 1);
            var fcsId = (uint)(fhdByte >> 6);
            ulong windowSize = 0;
            uint dictId = 0;
            var frameContentSize = unchecked(0UL - 1);
            if ((fhdByte & 0x08) != 0)
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorFrameParameterUnsupported));
            }

            if (singleSegment == 0)
            {
                var wlByte = ip[pos++];
                var windowLog = (uint)((wlByte >> 3) + 10);
                if (windowLog > (uint)(sizeof(nuint) == 4 ? 30 : 31))
                {
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorFrameParameterWindowTooLarge));
                }

                windowSize = 1UL << (int)windowLog;
                windowSize += (windowSize >> 3) * (ulong)(wlByte & 7);
            }

            switch (dictIdSizeCode)
            {
                default:
                    assert(0 != 0);
                    goto case 0;
                case 0:
                    break;
                case 1:
                    dictId = ip[pos];
                    pos++;
                    break;
                case 2:
                    dictId = MEM_readLE16(ip + pos);
                    pos += 2;
                    break;
                case 3:
                    dictId = MEM_readLE32(ip + pos);
                    pos += 4;
                    break;
            }

            switch (fcsId)
            {
                default:
                    assert(0 != 0);
                    goto case 0;
                case 0:
                    if (singleSegment != 0)
                    {
                        frameContentSize = ip[pos];
                    }

                    break;
                case 1:
                    frameContentSize = (ulong)(MEM_readLE16(ip + pos) + 256);
                    break;
                case 2:
                    frameContentSize = MEM_readLE32(ip + pos);
                    break;
                case 3:
                    frameContentSize = MEM_readLE64(ip + pos);
                    break;
            }

            if (singleSegment != 0)
            {
                windowSize = frameContentSize;
            }

            zfhPtr->frameType = ZstdFrameTypeE.ZstdFrame;
            zfhPtr->frameContentSize = frameContentSize;
            zfhPtr->windowSize = windowSize;
            zfhPtr->blockSizeMax = (uint)(windowSize < 1 << 17 ? windowSize : 1 << 17);
            zfhPtr->dictID = dictId;
            zfhPtr->checksumFlag = checksumFlag;
        }

        return 0;
    }

    /** ZSTD_getFrameHeader() :
     *  decode Frame Header, or require larger `srcSize`.
     *  note : this function does not consume input, it only reads it.
     * @return : 0, `zfhPtr` is correctly filled,
     *          >0, `srcSize` is too small, value is wanted `srcSize` amount,
     *           or an error code, which can be tested using ZSTD_isError() */
    public static nuint ZSTD_getFrameHeader(ZstdFrameHeader* zfhPtr, void* src, nuint srcSize)
    {
        return ZSTD_getFrameHeader_advanced(zfhPtr, src, srcSize, ZstdFormatE.ZstdFZstd1);
    }

    /** ZSTD_getFrameContentSize() :
     *  compatible with legacy mode
     * @return : decompressed size of the single frame pointed to be `src` if known, otherwise
     *         - ZSTD_CONTENTSIZE_UNKNOWN if the size cannot be determined
     *         - ZSTD_CONTENTSIZE_ERROR if an error occurred (e.g. invalid magic number, srcSize too small) */
    public static ulong ZSTD_getFrameContentSize(void* src, nuint srcSize)
    {
        ZstdFrameHeader zfh;
        if (ZSTD_getFrameHeader(&zfh, src, srcSize) != 0)
            return unchecked(0UL - 2);

        if (zfh.frameType == ZstdFrameTypeE.ZstdSkippableFrame)
        {
            return 0;
        }
        else
        {
            return zfh.frameContentSize;
        }
    }

    private static nuint ReadSkippableFrameSize(void* src, nuint srcSize)
    {
        const nuint skippableHeaderSize = 8;
        if (srcSize < 8)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
        }

        var sizeU32 = MEM_readLE32((byte*)src + 4);
        if (sizeU32 + 8 < sizeU32)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorFrameParameterUnsupported));
        }

        {
            var skippableSize = skippableHeaderSize + sizeU32;
            if (skippableSize > srcSize)
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
            }

            return skippableSize;
        }
    }

    /*! ZSTD_readSkippableFrame() :
     * Retrieves content of a skippable frame, and writes it to dst buffer.
     *
     * The parameter magicVariant will receive the magicVariant that was supplied when the frame was written,
     * i.e. magicNumber - ZSTD_MAGIC_SKIPPABLE_START.  This can be NULL if the caller is not interested
     * in the magicVariant.
     *
     * Returns an error if destination buffer is not large enough, or if this is not a valid skippable frame.
     *
     * @return : number of bytes written or a ZSTD error.
     */
    public static nuint ZSTD_readSkippableFrame(void* dst, nuint dstCapacity, uint* magicVariant, void* src, nuint srcSize)
    {
        if (srcSize < 8)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
        }

        {
            var magicNumber = MEM_readLE32(src);
            var skippableFrameSize = ReadSkippableFrameSize(src, srcSize);
            var skippableContentSize = skippableFrameSize - 8;
            if (ZSTD_isSkippableFrame(src, srcSize) == 0)
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorFrameParameterUnsupported));
            }

            if (skippableFrameSize < 8 || skippableFrameSize > srcSize)
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
            }

            if (skippableContentSize > dstCapacity)
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));
            }

            if (skippableContentSize > 0 && dst != null)
                memcpy(dst, (byte*)src + 8, (uint)skippableContentSize);
            if (magicVariant != null)
            {
                *magicVariant = magicNumber - 0x184D2A50;
            }

            return skippableContentSize;
        }
    }

    /** ZSTD_findDecompressedSize() :
     *  `srcSize` must be the exact length of some number of ZSTD compressed and/or
     *      skippable frames
     *  note: compatible with legacy mode
     * @return : decompressed size of the frames contained */
    public static ulong ZSTD_findDecompressedSize(void* src, nuint srcSize)
    {
        ulong totalDstSize = 0;
        while (srcSize >= ZSTD_startingInputLength(ZstdFormatE.ZstdFZstd1))
        {
            var magicNumber = MEM_readLE32(src);
            if ((magicNumber & 0xFFFFFFF0) == 0x184D2A50)
            {
                var skippableSize = ReadSkippableFrameSize(src, srcSize);
                if (ERR_isError(skippableSize))
                    return unchecked(0UL - 2);

                assert(skippableSize <= srcSize);
                src = (byte*)src + skippableSize;
                srcSize -= skippableSize;
                continue;
            }

            {
                var fcs = ZSTD_getFrameContentSize(src, srcSize);
                if (fcs >= unchecked(0UL - 2))
                    return fcs;
                if (totalDstSize + fcs < totalDstSize)
                    return unchecked(0UL - 2);

                totalDstSize += fcs;
            }

            {
                var frameSrcSize = ZSTD_findFrameCompressedSize(src, srcSize);
                if (ERR_isError(frameSrcSize))
                    return unchecked(0UL - 2);

                assert(frameSrcSize <= srcSize);
                src = (byte*)src + frameSrcSize;
                srcSize -= frameSrcSize;
            }
        }

        if (srcSize != 0)
            return unchecked(0UL - 2);

        return totalDstSize;
    }

    /** ZSTD_getDecompressedSize() :
     *  compatible with legacy mode
     * @return : decompressed size if known, 0 otherwise
    note : 0 can mean any of the following :
    - frame content is empty
    - decompressed size field is not present in frame header
    - frame header unknown / not supported
    - frame header not complete (`srcSize` too small) */
    public static ulong ZSTD_getDecompressedSize(void* src, nuint srcSize)
    {
        var ret = ZSTD_getFrameContentSize(src, srcSize);
        return ret >= unchecked(0UL - 2) ? 0 : ret;
    }

    /** ZSTD_decodeFrameHeader() :
     * `headerSize` must be the size provided by ZSTD_frameHeaderSize().
     * If multiple DDict references are enabled, also will choose the correct DDict to use.
     * @return : 0 if success, or an error code, which can be tested using ZSTD_isError() */
    private static nuint ZSTD_decodeFrameHeader(ZstdDCtxS* dctx, void* src, nuint headerSize)
    {
        var result = ZSTD_getFrameHeader_advanced(&dctx->fParams, src, headerSize, dctx->format);
        if (ERR_isError(result))
            return result;

        if (result > 0)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
        }

        if (dctx->refMultipleDDicts == ZstdRefMultipleDDictsE.ZstdRmdRefMultipleDDicts && dctx->ddictSet != null)
        {
            ZSTD_DCtx_selectFrameDDict(dctx);
        }

        if (dctx->fParams.dictID != 0 && dctx->dictID != dctx->fParams.dictID)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDictionaryWrong));
        }

        dctx->validateChecksum = (uint)(dctx->fParams.checksumFlag != 0 && dctx->forceIgnoreChecksum == default ? 1 : 0);
        if (dctx->validateChecksum != 0)
            ZSTD_XXH64_reset(&dctx->xxhState, 0);
        dctx->processedCSize += headerSize;
        return 0;
    }

    private static ZstdFrameSizeInfo ZSTD_errorFrameSizeInfo(nuint ret)
    {
        System.Runtime.CompilerServices.Unsafe.SkipInit(out ZstdFrameSizeInfo frameSizeInfo);
        frameSizeInfo.compressedSize = ret;
        frameSizeInfo.decompressedBound = unchecked(0UL - 2);
        return frameSizeInfo;
    }

    private static ZstdFrameSizeInfo ZSTD_findFrameSizeInfo(void* src, nuint srcSize, ZstdFormatE format)
    {
        var frameSizeInfo = new ZstdFrameSizeInfo();
        if (format == ZstdFormatE.ZstdFZstd1 && srcSize >= 8 && (MEM_readLE32(src) & 0xFFFFFFF0) == 0x184D2A50)
        {
            frameSizeInfo.compressedSize = ReadSkippableFrameSize(src, srcSize);
            assert(ERR_isError(frameSizeInfo.compressedSize) || frameSizeInfo.compressedSize <= srcSize);
            return frameSizeInfo;
        }
        else
        {
            var ip = (byte*)src;
            var ipstart = ip;
            var remainingSize = srcSize;
            nuint nbBlocks = 0;
            ZstdFrameHeader zfh;
            {
                var ret = ZSTD_getFrameHeader_advanced(&zfh, src, srcSize, format);
                if (ERR_isError(ret))
                    return ZSTD_errorFrameSizeInfo(ret);
                if (ret > 0)
                    return ZSTD_errorFrameSizeInfo(unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong)));
            }

            ip += zfh.headerSize;
            remainingSize -= zfh.headerSize;
            while (true)
            {
                BlockPropertiesT blockProperties;
                var cBlockSize = ZSTD_getcBlockSize(ip, remainingSize, &blockProperties);
                if (ERR_isError(cBlockSize))
                    return ZSTD_errorFrameSizeInfo(cBlockSize);
                if (ZstdBlockHeaderSize + cBlockSize > remainingSize)
                    return ZSTD_errorFrameSizeInfo(unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong)));

                ip += ZstdBlockHeaderSize + cBlockSize;
                remainingSize -= ZstdBlockHeaderSize + cBlockSize;
                nbBlocks++;
                if (blockProperties.lastBlock != 0)
                    break;
            }

            if (zfh.checksumFlag != 0)
            {
                if (remainingSize < 4)
                    return ZSTD_errorFrameSizeInfo(unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong)));

                ip += 4;
            }

            frameSizeInfo.nbBlocks = nbBlocks;
            frameSizeInfo.compressedSize = (nuint)(ip - ipstart);
            frameSizeInfo.decompressedBound = zfh.frameContentSize != unchecked(0UL - 1) ? zfh.frameContentSize : (ulong)nbBlocks * zfh.blockSizeMax;
            return frameSizeInfo;
        }
    }

    private static nuint ZSTD_findFrameCompressedSize_advanced(void* src, nuint srcSize, ZstdFormatE format)
    {
        var frameSizeInfo = ZSTD_findFrameSizeInfo(src, srcSize, format);
        return frameSizeInfo.compressedSize;
    }

    /** ZSTD_findFrameCompressedSize() :
     * See docs in zstd.h
     * Note: compatible with legacy mode */
    public static nuint ZSTD_findFrameCompressedSize(void* src, nuint srcSize)
    {
        return ZSTD_findFrameCompressedSize_advanced(src, srcSize, ZstdFormatE.ZstdFZstd1);
    }

    /** ZSTD_decompressBound() :
     *  compatible with legacy mode
     *  `src` must point to the start of a ZSTD frame or a skippable frame
     *  `srcSize` must be at least as large as the frame contained
     *  @return : the maximum decompressed size of the compressed source
     */
    public static ulong ZSTD_decompressBound(void* src, nuint srcSize)
    {
        ulong bound = 0;
        while (srcSize > 0)
        {
            var frameSizeInfo = ZSTD_findFrameSizeInfo(src, srcSize, ZstdFormatE.ZstdFZstd1);
            var compressedSize = frameSizeInfo.compressedSize;
            var decompressedBound = frameSizeInfo.decompressedBound;
            if (ERR_isError(compressedSize) || decompressedBound == unchecked(0UL - 2))
                return unchecked(0UL - 2);

            assert(srcSize >= compressedSize);
            src = (byte*)src + compressedSize;
            srcSize -= compressedSize;
            bound += decompressedBound;
        }

        return bound;
    }

    /*! ZSTD_decompressionMargin() :
     * Zstd supports in-place decompression, where the input and output buffers overlap.
     * In this case, the output buffer must be at least (Margin + Output_Size) bytes large,
     * and the input buffer must be at the end of the output buffer.
     *
     *  _______________________ Output Buffer ________________________
     * |                                                              |
     * |                                        ____ Input Buffer ____|
     * |                                       |                      |
     * v                                       v                      v
     * |---------------------------------------|-----------|----------|
     * ^                                                   ^          ^
     * |___________________ Output_Size ___________________|_ Margin _|
     *
     * NOTE: See also ZSTD_DECOMPRESSION_MARGIN().
     * NOTE: This applies only to single-pass decompression through ZSTD_decompress() or
     * ZSTD_decompressDCtx().
     * NOTE: This function supports multi-frame input.
     *
     * @param src The compressed frame(s)
     * @param srcSize The size of the compressed frame(s)
     * @returns The decompression margin or an error that can be checked with ZSTD_isError().
     */
    public static nuint ZSTD_decompressionMargin(void* src, nuint srcSize)
    {
        nuint margin = 0;
        uint maxBlockSize = 0;
        while (srcSize > 0)
        {
            var frameSizeInfo = ZSTD_findFrameSizeInfo(src, srcSize, ZstdFormatE.ZstdFZstd1);
            var compressedSize = frameSizeInfo.compressedSize;
            var decompressedBound = frameSizeInfo.decompressedBound;
            ZstdFrameHeader zfh;
            {
                var errCode = ZSTD_getFrameHeader(&zfh, src, srcSize);
                if (ERR_isError(errCode))
                {
                    return errCode;
                }
            }

            if (ERR_isError(compressedSize) || decompressedBound == unchecked(0UL - 2))
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

            if (zfh.frameType == ZstdFrameTypeE.ZstdFrame)
            {
                margin += zfh.headerSize;
                margin += (nuint)(zfh.checksumFlag != 0 ? 4 : 0);
                margin += 3 * frameSizeInfo.nbBlocks;
                maxBlockSize = maxBlockSize > zfh.blockSizeMax ? maxBlockSize : zfh.blockSizeMax;
            }
            else
            {
                assert(zfh.frameType == ZstdFrameTypeE.ZstdSkippableFrame);
                margin += compressedSize;
            }

            assert(srcSize >= compressedSize);
            src = (byte*)src + compressedSize;
            srcSize -= compressedSize;
        }

        margin += maxBlockSize;
        return margin;
    }

    /** ZSTD_insertBlock() :
     *  insert `src` block into `dctx` history. Useful to track uncompressed blocks. */
    public static nuint ZSTD_insertBlock(ZstdDCtxS* dctx, void* blockStart, nuint blockSize)
    {
        ZSTD_checkContinuity(dctx, blockStart, blockSize);
        dctx->previousDstEnd = (sbyte*)blockStart + blockSize;
        return blockSize;
    }

    private static nuint ZSTD_copyRawBlock(void* dst, nuint dstCapacity, void* src, nuint srcSize)
    {
        if (srcSize > dstCapacity)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));
        }

        if (dst == null)
        {
            if (srcSize == 0)
                return 0;

            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstBufferNull));
        }

        memmove(dst, src, srcSize);
        return srcSize;
    }

    private static nuint ZSTD_setRleBlock(void* dst, nuint dstCapacity, byte b, nuint regenSize)
    {
        if (regenSize > dstCapacity)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));
        }

        if (dst == null)
        {
            if (regenSize == 0)
                return 0;

            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstBufferNull));
        }

        memset(dst, b, (uint)regenSize);
        return regenSize;
    }

    private static void ZSTD_DCtx_trace_end(ZstdDCtxS* dctx, ulong uncompressedSize, ulong compressedSize, int streaming)
    {
    }

    /*! ZSTD_decompressFrame() :
     * @dctx must be properly initialized
     *  will update *srcPtr and *srcSizePtr,
     *  to make *srcPtr progress by one frame. */
    private static nuint ZSTD_decompressFrame(ZstdDCtxS* dctx, void* dst, nuint dstCapacity, void** srcPtr, nuint* srcSizePtr)
    {
        var istart = (byte*)*srcPtr;
        var ip = istart;
        var ostart = (byte*)dst;
        var oend = dstCapacity != 0 ? ostart + dstCapacity : ostart;
        var op = ostart;
        var remainingSrcSize = *srcSizePtr;
        if (remainingSrcSize < (nuint)(dctx->format == ZstdFormatE.ZstdFZstd1 ? 6 : 2) + ZstdBlockHeaderSize)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
        }

        {
            var frameHeaderSize = ZSTD_frameHeaderSize_internal(ip, (nuint)(dctx->format == ZstdFormatE.ZstdFZstd1 ? 5 : 1), dctx->format);
            if (ERR_isError(frameHeaderSize))
                return frameHeaderSize;

            if (remainingSrcSize < frameHeaderSize + ZstdBlockHeaderSize)
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
            }

            {
                var errCode = ZSTD_decodeFrameHeader(dctx, ip, frameHeaderSize);
                if (ERR_isError(errCode))
                {
                    return errCode;
                }
            }

            ip += frameHeaderSize;
            remainingSrcSize -= frameHeaderSize;
        }

        if (dctx->maxBlockSizeParam != 0)
        {
            dctx->fParams.blockSizeMax = dctx->fParams.blockSizeMax < (uint)dctx->maxBlockSizeParam ? dctx->fParams.blockSizeMax : (uint)dctx->maxBlockSizeParam;
        }

        while (true)
        {
            var oBlockEnd = oend;
            nuint decodedSize;
            BlockPropertiesT blockProperties;
            var cBlockSize = ZSTD_getcBlockSize(ip, remainingSrcSize, &blockProperties);
            if (ERR_isError(cBlockSize))
                return cBlockSize;

            ip += ZstdBlockHeaderSize;
            remainingSrcSize -= ZstdBlockHeaderSize;
            if (cBlockSize > remainingSrcSize)
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
            }

            if (ip >= op && ip < oBlockEnd)
            {
                oBlockEnd = op + (ip - op);
            }

            switch (blockProperties.blockType)
            {
                case BlockTypeE.BtCompressed:
                    assert(dctx->isFrameDecompression == 1);
                    decodedSize = ZSTD_decompressBlock_internal(dctx, op, (nuint)(oBlockEnd - op), ip, cBlockSize, StreamingOperation.NotStreaming);
                    break;
                case BlockTypeE.BtRaw:
                    decodedSize = ZSTD_copyRawBlock(op, (nuint)(oend - op), ip, cBlockSize);
                    break;
                case BlockTypeE.BtRle:
                    decodedSize = ZSTD_setRleBlock(op, (nuint)(oBlockEnd - op), *ip, blockProperties.origSize);
                    break;
                case BlockTypeE.BtReserved:
                default:
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
            }

            {
                var errCode = decodedSize;
                if (ERR_isError(errCode))
                {
                    return errCode;
                }
            }

            if (dctx->validateChecksum != 0)
            {
                ZSTD_XXH64_update(&dctx->xxhState, op, decodedSize);
            }

            if (decodedSize != 0)
            {
                op += decodedSize;
            }

            assert(ip != null);
            ip += cBlockSize;
            remainingSrcSize -= cBlockSize;
            if (blockProperties.lastBlock != 0)
                break;
        }

        if (dctx->fParams.frameContentSize != unchecked(0UL - 1))
        {
            if ((ulong)(op - ostart) != dctx->fParams.frameContentSize)
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
            }
        }

        if (dctx->fParams.checksumFlag != 0)
        {
            if (remainingSrcSize < 4)
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorChecksumWrong));
            }

            if (dctx->forceIgnoreChecksum == default)
            {
                var checkCalc = (uint)ZSTD_XXH64_digest(&dctx->xxhState);
                var checkRead = MEM_readLE32(ip);
                if (checkRead != checkCalc)
                {
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorChecksumWrong));
                }
            }

            ip += 4;
            remainingSrcSize -= 4;
        }

        ZSTD_DCtx_trace_end(dctx, (ulong)(op - ostart), (ulong)(ip - istart), 0);
        *srcPtr = ip;
        *srcSizePtr = remainingSrcSize;
        return (nuint)(op - ostart);
    }

    private static nuint ZSTD_decompressMultiFrame(ZstdDCtxS* dctx, void* dst, nuint dstCapacity, void* src, nuint srcSize, void* dict, nuint dictSize, ZstdDDictS* ddict)
    {
        var dststart = dst;
        var moreThan1Frame = 0;
        assert(dict == null || ddict == null);
        if (ddict != null)
        {
            dict = ZSTD_DDict_dictContent(ddict);
            dictSize = ZSTD_DDict_dictSize(ddict);
        }

        while (srcSize >= ZSTD_startingInputLength(dctx->format))
        {
            if (dctx->format == ZstdFormatE.ZstdFZstd1 && srcSize >= 4)
            {
                var magicNumber = MEM_readLE32(src);
                if ((magicNumber & 0xFFFFFFF0) == 0x184D2A50)
                {
                    /* skippable frame detected : skip it */
                    var skippableSize = ReadSkippableFrameSize(src, srcSize);
                    {
                        var errCode = skippableSize;
                        if (ERR_isError(errCode))
                        {
                            return errCode;
                        }
                    }

                    assert(skippableSize <= srcSize);
                    src = (byte*)src + skippableSize;
                    srcSize -= skippableSize;
                    continue;
                }
            }

            if (ddict != null)
            {
                /* we were called from ZSTD_decompress_usingDDict */
                var errCode = ZSTD_decompressBegin_usingDDict(dctx, ddict);
                if (ERR_isError(errCode))
                {
                    return errCode;
                }
            }
            else
            {
                /* this will initialize correctly with no dict if dict == NULL, so
                 * use this in all cases but ddict */
                var errCode = ZSTD_decompressBegin_usingDict(dctx, dict, dictSize);
                if (ERR_isError(errCode))
                {
                    return errCode;
                }
            }

            ZSTD_checkContinuity(dctx, dst, dstCapacity);
            {
                var res = ZSTD_decompressFrame(dctx, dst, dstCapacity, &src, &srcSize);
                if (ZSTD_getErrorCode(res) == ZstdErrorCode.ZstdErrorPrefixUnknown && moreThan1Frame == 1)
                {
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
                }

                if (ERR_isError(res))
                    return res;

                assert(res <= dstCapacity);
                if (res != 0)
                {
                    dst = (byte*)dst + res;
                }

                dstCapacity -= res;
            }

            moreThan1Frame = 1;
        }

        if (srcSize != 0)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
        }

        return (nuint)((byte*)dst - (byte*)dststart);
    }

    /*! ZSTD_decompress_usingDict() :
     *  Decompression using a known Dictionary.
     *  Dictionary must be identical to the one used during compression.
     *  Note : This function loads the dictionary, resulting in significant startup delay.
     *         It's intended for a dictionary used only once.
     *  Note : When `dict == NULL || dictSize < 8` no dictionary is used. */
    public static nuint ZSTD_decompress_usingDict(ZstdDCtxS* dctx, void* dst, nuint dstCapacity, void* src, nuint srcSize, void* dict, nuint dictSize)
    {
        return ZSTD_decompressMultiFrame(dctx, dst, dstCapacity, src, srcSize, dict, dictSize, null);
    }

    private static ZstdDDictS* ZSTD_getDDict(ZstdDCtxS* dctx)
    {
        switch (dctx->dictUses)
        {
            default:
                assert(0 != 0);
                goto case ZstdDictUsesE.ZstdDontUse;
            case ZstdDictUsesE.ZstdDontUse:
                ZSTD_clearDict(dctx);
                return null;
            case ZstdDictUsesE.ZstdUseIndefinitely:
                return dctx->ddict;
            case ZstdDictUsesE.ZstdUseOnce:
                dctx->dictUses = ZstdDictUsesE.ZstdDontUse;
                return dctx->ddict;
        }
    }

    /*! ZSTD_decompressDCtx() :
     *  Same as ZSTD_decompress(),
     *  requires an allocated ZSTD_DCtx.
     *  Compatible with sticky parameters (see below).
     */
    public static nuint ZSTD_decompressDCtx(ZstdDCtxS* dctx, void* dst, nuint dstCapacity, void* src, nuint srcSize)
    {
        return ZSTD_decompress_usingDDict(dctx, dst, dstCapacity, src, srcSize, ZSTD_getDDict(dctx));
    }

    /*! ZSTD_decompress() :
     * `compressedSize` : must be the _exact_ size of some number of compressed and/or skippable frames.
     *  Multiple compressed frames can be decompressed at once with this method.
     *  The result will be the concatenation of all decompressed frames, back to back.
     * `dstCapacity` is an upper bound of originalSize to regenerate.
     *  First frame's decompressed size can be extracted using ZSTD_getFrameContentSize().
     *  If maximum upper bound isn't known, prefer using streaming mode to decompress data.
     * @return : the number of bytes decompressed into `dst` (<= `dstCapacity`),
     *           or an errorCode if it fails (which can be tested using ZSTD_isError()). */
    public static nuint ZSTD_decompress(void* dst, nuint dstCapacity, void* src, nuint srcSize)
    {
        var dctx = ZSTD_createDCtx_internal(ZstdDefaultCMem);
        if (dctx == null)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMemoryAllocation));
        }

        var regenSize = ZSTD_decompressDCtx(dctx, dst, dstCapacity, src, srcSize);
        ZSTD_freeDCtx(dctx);
        return regenSize;
    }

    /*-**************************************
     *   Advanced Streaming Decompression API
     *   Bufferless and synchronous
     ****************************************/
    public static nuint ZSTD_nextSrcSizeToDecompress(ZstdDCtxS* dctx)
    {
        return dctx->expected;
    }

    /**
     * Similar to ZSTD_nextSrcSizeToDecompress(), but when a block input can be streamed, we
     * allow taking a partial block as the input. Currently only raw uncompressed blocks can
     * be streamed.
     *
     * For blocks that can be streamed, this allows us to reduce the latency until we produce
     * output, and avoid copying the input.
     *
     * @param inputSize - The total amount of input that the caller currently has.
     */
    private static nuint ZSTD_nextSrcSizeToDecompressWithInputSize(ZstdDCtxS* dctx, nuint inputSize)
    {
        if (!(dctx->stage == ZstdDStage.ZstDdsDecompressBlock || dctx->stage == ZstdDStage.ZstDdsDecompressLastBlock))
            return dctx->expected;
        if (dctx->bType != BlockTypeE.BtRaw)
            return dctx->expected;

        return inputSize <= 1 ? 1 : inputSize <= dctx->expected ? inputSize : dctx->expected;
    }

    public static ZstdNextInputTypeE ZSTD_nextInputType(ZstdDCtxS* dctx)
    {
        switch (dctx->stage)
        {
            default:
                assert(0 != 0);
                goto case ZstdDStage.ZstDdsGetFrameHeaderSize;
            case ZstdDStage.ZstDdsGetFrameHeaderSize:
            case ZstdDStage.ZstDdsDecodeFrameHeader:
                return ZstdNextInputTypeE.ZstDnitFrameHeader;
            case ZstdDStage.ZstDdsDecodeBlockHeader:
                return ZstdNextInputTypeE.ZstDnitBlockHeader;
            case ZstdDStage.ZstDdsDecompressBlock:
                return ZstdNextInputTypeE.ZstDnitBlock;
            case ZstdDStage.ZstDdsDecompressLastBlock:
                return ZstdNextInputTypeE.ZstDnitLastBlock;
            case ZstdDStage.ZstDdsCheckChecksum:
                return ZstdNextInputTypeE.ZstDnitChecksum;
            case ZstdDStage.ZstDdsDecodeSkippableHeader:
            case ZstdDStage.ZstDdsSkipFrame:
                return ZstdNextInputTypeE.ZstDnitSkippableFrame;
        }
    }

    private static int ZSTD_isSkipFrame(ZstdDCtxS* dctx)
    {
        return dctx->stage == ZstdDStage.ZstDdsSkipFrame ? 1 : 0;
    }

    /** ZSTD_decompressContinue() :
     *  srcSize : must be the exact nb of bytes expected (see ZSTD_nextSrcSizeToDecompress())
     *  @return : nb of bytes generated into `dst` (necessarily <= `dstCapacity)
     *            or an error code, which can be tested using ZSTD_isError() */
    public static nuint ZSTD_decompressContinue(ZstdDCtxS* dctx, void* dst, nuint dstCapacity, void* src, nuint srcSize)
    {
        if (srcSize != ZSTD_nextSrcSizeToDecompressWithInputSize(dctx, srcSize))
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
        }

        ZSTD_checkContinuity(dctx, dst, dstCapacity);
        dctx->processedCSize += srcSize;
        switch (dctx->stage)
        {
            case ZstdDStage.ZstDdsGetFrameHeaderSize:
                assert(src != null);
                if (dctx->format == ZstdFormatE.ZstdFZstd1)
                {
                    assert(srcSize >= 4);
                    if ((MEM_readLE32(src) & 0xFFFFFFF0) == 0x184D2A50)
                    {
                        memcpy(dctx->headerBuffer, src, (uint)srcSize);
                        dctx->expected = 8 - srcSize;
                        dctx->stage = ZstdDStage.ZstDdsDecodeSkippableHeader;
                        return 0;
                    }
                }

                dctx->headerSize = ZSTD_frameHeaderSize_internal(src, srcSize, dctx->format);
                if (ERR_isError(dctx->headerSize))
                    return dctx->headerSize;

                memcpy(dctx->headerBuffer, src, (uint)srcSize);
                dctx->expected = dctx->headerSize - srcSize;
                dctx->stage = ZstdDStage.ZstDdsDecodeFrameHeader;
                return 0;
            case ZstdDStage.ZstDdsDecodeFrameHeader:
                assert(src != null);
                memcpy(dctx->headerBuffer + (dctx->headerSize - srcSize), src, (uint)srcSize);
            {
                var errCode = ZSTD_decodeFrameHeader(dctx, dctx->headerBuffer, dctx->headerSize);
                if (ERR_isError(errCode))
                {
                    return errCode;
                }
            }

                dctx->expected = ZstdBlockHeaderSize;
                dctx->stage = ZstdDStage.ZstDdsDecodeBlockHeader;
                return 0;
            case ZstdDStage.ZstDdsDecodeBlockHeader:
            {
                BlockPropertiesT bp;
                var cBlockSize = ZSTD_getcBlockSize(src, ZstdBlockHeaderSize, &bp);
                if (ERR_isError(cBlockSize))
                    return cBlockSize;

                if (cBlockSize > dctx->fParams.blockSizeMax)
                {
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
                }

                dctx->expected = cBlockSize;
                dctx->bType = bp.blockType;
                dctx->rleSize = bp.origSize;
                if (cBlockSize != 0)
                {
                    dctx->stage = bp.lastBlock != 0 ? ZstdDStage.ZstDdsDecompressLastBlock : ZstdDStage.ZstDdsDecompressBlock;
                    return 0;
                }

                if (bp.lastBlock != 0)
                {
                    if (dctx->fParams.checksumFlag != 0)
                    {
                        dctx->expected = 4;
                        dctx->stage = ZstdDStage.ZstDdsCheckChecksum;
                    }
                    else
                    {
                        dctx->expected = 0;
                        dctx->stage = ZstdDStage.ZstDdsGetFrameHeaderSize;
                    }
                }
                else
                {
                    dctx->expected = ZstdBlockHeaderSize;
                    dctx->stage = ZstdDStage.ZstDdsDecodeBlockHeader;
                }

                return 0;
            }

            case ZstdDStage.ZstDdsDecompressLastBlock:
            case ZstdDStage.ZstDdsDecompressBlock:
            {
                nuint rSize;
                switch (dctx->bType)
                {
                    case BlockTypeE.BtCompressed:
                        assert(dctx->isFrameDecompression == 1);
                        rSize = ZSTD_decompressBlock_internal(dctx, dst, dstCapacity, src, srcSize, StreamingOperation.IsStreaming);
                        dctx->expected = 0;
                        break;
                    case BlockTypeE.BtRaw:
                        assert(srcSize <= dctx->expected);
                        rSize = ZSTD_copyRawBlock(dst, dstCapacity, src, srcSize);
                    {
                        var errCode = rSize;
                        if (ERR_isError(errCode))
                        {
                            return errCode;
                        }
                    }

                        assert(rSize == srcSize);
                        dctx->expected -= rSize;
                        break;
                    case BlockTypeE.BtRle:
                        rSize = ZSTD_setRleBlock(dst, dstCapacity, *(byte*)src, dctx->rleSize);
                        dctx->expected = 0;
                        break;
                    case BlockTypeE.BtReserved:
                    default:
                        return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
                }

                {
                    var errCode = rSize;
                    if (ERR_isError(errCode))
                    {
                        return errCode;
                    }
                }

                if (rSize > dctx->fParams.blockSizeMax)
                {
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
                }

                dctx->decodedSize += rSize;
                if (dctx->validateChecksum != 0)
                    ZSTD_XXH64_update(&dctx->xxhState, dst, rSize);
                dctx->previousDstEnd = (sbyte*)dst + rSize;
                if (dctx->expected > 0)
                {
                    return rSize;
                }

                if (dctx->stage == ZstdDStage.ZstDdsDecompressLastBlock)
                {
                    if (dctx->fParams.frameContentSize != unchecked(0UL - 1) && dctx->decodedSize != dctx->fParams.frameContentSize)
                    {
                        return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
                    }

                    if (dctx->fParams.checksumFlag != 0)
                    {
                        dctx->expected = 4;
                        dctx->stage = ZstdDStage.ZstDdsCheckChecksum;
                    }
                    else
                    {
                        ZSTD_DCtx_trace_end(dctx, dctx->decodedSize, dctx->processedCSize, 1);
                        dctx->expected = 0;
                        dctx->stage = ZstdDStage.ZstDdsGetFrameHeaderSize;
                    }
                }
                else
                {
                    dctx->stage = ZstdDStage.ZstDdsDecodeBlockHeader;
                    dctx->expected = ZstdBlockHeaderSize;
                }

                return rSize;
            }

            case ZstdDStage.ZstDdsCheckChecksum:
                assert(srcSize == 4);
            {
                if (dctx->validateChecksum != 0)
                {
                    var h32 = (uint)ZSTD_XXH64_digest(&dctx->xxhState);
                    var check32 = MEM_readLE32(src);
                    if (check32 != h32)
                    {
                        return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorChecksumWrong));
                    }
                }

                ZSTD_DCtx_trace_end(dctx, dctx->decodedSize, dctx->processedCSize, 1);
                dctx->expected = 0;
                dctx->stage = ZstdDStage.ZstDdsGetFrameHeaderSize;
                return 0;
            }

            case ZstdDStage.ZstDdsDecodeSkippableHeader:
                assert(src != null);
                assert(srcSize <= 8);
                assert(dctx->format != ZstdFormatE.ZstdFZstd1Magicless);
                memcpy(dctx->headerBuffer + (8 - srcSize), src, (uint)srcSize);
                dctx->expected = MEM_readLE32(dctx->headerBuffer + 4);
                dctx->stage = ZstdDStage.ZstDdsSkipFrame;
                return 0;
            case ZstdDStage.ZstDdsSkipFrame:
                dctx->expected = 0;
                dctx->stage = ZstdDStage.ZstDdsGetFrameHeaderSize;
                return 0;
            default:
                assert(0 != 0);
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorGeneric));
        }
    }

    private static nuint ZSTD_refDictContent(ZstdDCtxS* dctx, void* dict, nuint dictSize)
    {
        dctx->dictEnd = dctx->previousDstEnd;
        dctx->virtualStart = (sbyte*)dict - ((sbyte*)dctx->previousDstEnd - (sbyte*)dctx->prefixStart);
        dctx->prefixStart = dict;
        dctx->previousDstEnd = (sbyte*)dict + dictSize;
        return 0;
    }

    /*! ZSTD_loadDEntropy() :
     *  dict : must point at beginning of a valid zstd dictionary.
     * @return : size of entropy tables read */
    private static nuint ZSTD_loadDEntropy(ZstdEntropyDTablesT* entropy, void* dict, nuint dictSize)
    {
        var dictPtr = (byte*)dict;
        var dictEnd = dictPtr + dictSize;
        if (dictSize <= 8)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDictionaryCorrupted));
        }

        assert(MEM_readLE32(dict) == 0xEC30A437);
        dictPtr += 8;
        {
            /* use fse tables as temporary workspace; implies fse tables are grouped together */
            void* workspace = &entropy->LLTable;
            var workspaceSize = (nuint)(sizeof(ZstdSeqSymbol) * 513 + sizeof(ZstdSeqSymbol) * 257 + sizeof(ZstdSeqSymbol) * 513);
            var hSize = HUF_readDTableX2_wksp(entropy->hufTable, dictPtr, (nuint)(dictEnd - dictPtr), workspace, workspaceSize, 0);
            if (ERR_isError(hSize))
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDictionaryCorrupted));
            }

            dictPtr += hSize;
        }

        {
            var offcodeNCount = stackalloc short[32];
            uint offcodeMaxValue = 31, offcodeLog;
            var offcodeHeaderSize = FSE_readNCount(offcodeNCount, &offcodeMaxValue, &offcodeLog, dictPtr, (nuint)(dictEnd - dictPtr));
            if (ERR_isError(offcodeHeaderSize))
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDictionaryCorrupted));
            }

            if (offcodeMaxValue > 31)
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDictionaryCorrupted));
            }

            if (offcodeLog > 8)
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDictionaryCorrupted));
            }

            ZSTD_buildFSETable(&entropy->OFTable.e0, offcodeNCount, offcodeMaxValue, OfBase, OfBits, offcodeLog, entropy->workspace, sizeof(uint) * 157, 0);
            dictPtr += offcodeHeaderSize;
        }

        {
            var matchlengthNCount = stackalloc short[53];
            uint matchlengthMaxValue = 52, matchlengthLog;
            var matchlengthHeaderSize = FSE_readNCount(matchlengthNCount, &matchlengthMaxValue, &matchlengthLog, dictPtr, (nuint)(dictEnd - dictPtr));
            if (ERR_isError(matchlengthHeaderSize))
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDictionaryCorrupted));
            }

            if (matchlengthMaxValue > 52)
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDictionaryCorrupted));
            }

            if (matchlengthLog > 9)
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDictionaryCorrupted));
            }

            ZSTD_buildFSETable(&entropy->MLTable.e0, matchlengthNCount, matchlengthMaxValue, MlBase, MlBits, matchlengthLog, entropy->workspace, sizeof(uint) * 157, 0);
            dictPtr += matchlengthHeaderSize;
        }

        {
            var litlengthNCount = stackalloc short[36];
            uint litlengthMaxValue = 35, litlengthLog;
            var litlengthHeaderSize = FSE_readNCount(litlengthNCount, &litlengthMaxValue, &litlengthLog, dictPtr, (nuint)(dictEnd - dictPtr));
            if (ERR_isError(litlengthHeaderSize))
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDictionaryCorrupted));
            }

            if (litlengthMaxValue > 35)
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDictionaryCorrupted));
            }

            if (litlengthLog > 9)
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDictionaryCorrupted));
            }

            ZSTD_buildFSETable(&entropy->LLTable.e0, litlengthNCount, litlengthMaxValue, LlBase, LlBits, litlengthLog, entropy->workspace, sizeof(uint) * 157, 0);
            dictPtr += litlengthHeaderSize;
        }

        if (dictPtr + 12 > dictEnd)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDictionaryCorrupted));
        }

        {
            int i;
            var dictContentSize = (nuint)(dictEnd - (dictPtr + 12));
            for (i = 0; i < 3; i++)
            {
                var rep = MEM_readLE32(dictPtr);
                dictPtr += 4;
                if (rep == 0 || rep > dictContentSize)
                {
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDictionaryCorrupted));
                }

                entropy->rep[i] = rep;
            }
        }

        return (nuint)(dictPtr - (byte*)dict);
    }

    private static nuint ZSTD_decompress_insertDictionary(ZstdDCtxS* dctx, void* dict, nuint dictSize)
    {
        if (dictSize < 8)
            return ZSTD_refDictContent(dctx, dict, dictSize);

        {
            var magic = MEM_readLE32(dict);
            if (magic != 0xEC30A437)
            {
                return ZSTD_refDictContent(dctx, dict, dictSize);
            }
        }

        dctx->dictID = MEM_readLE32((sbyte*)dict + 4);
        {
            var eSize = ZSTD_loadDEntropy(&dctx->entropy, dict, dictSize);
            if (ERR_isError(eSize))
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDictionaryCorrupted));
            }

            dict = (sbyte*)dict + eSize;
            dictSize -= eSize;
        }

        dctx->litEntropy = dctx->fseEntropy = 1;
        return ZSTD_refDictContent(dctx, dict, dictSize);
    }

    public static nuint ZSTD_decompressBegin(ZstdDCtxS* dctx)
    {
        assert(dctx != null);
        dctx->expected = ZSTD_startingInputLength(dctx->format);
        dctx->stage = ZstdDStage.ZstDdsGetFrameHeaderSize;
        dctx->processedCSize = 0;
        dctx->decodedSize = 0;
        dctx->previousDstEnd = null;
        dctx->prefixStart = null;
        dctx->virtualStart = null;
        dctx->dictEnd = null;
        dctx->entropy.hufTable[0] = 12 * 0x1000001;
        dctx->litEntropy = dctx->fseEntropy = 0;
        dctx->dictID = 0;
        dctx->bType = BlockTypeE.BtReserved;
        dctx->isFrameDecompression = 1;
        memcpy(dctx->entropy.rep, RepStartValue, sizeof(uint) * 3);
        dctx->LLTptr = &dctx->entropy.LLTable.e0;
        dctx->MLTptr = &dctx->entropy.MLTable.e0;
        dctx->OFTptr = &dctx->entropy.OFTable.e0;
        dctx->HUFptr = dctx->entropy.hufTable;
        return 0;
    }

    public static nuint ZSTD_decompressBegin_usingDict(ZstdDCtxS* dctx, void* dict, nuint dictSize)
    {
        {
            var errCode = ZSTD_decompressBegin(dctx);
            if (ERR_isError(errCode))
            {
                return errCode;
            }
        }

        if (dict != null && dictSize != 0)
            if (ERR_isError(ZSTD_decompress_insertDictionary(dctx, dict, dictSize)))
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDictionaryCorrupted));
            }

        return 0;
    }

    /* ======   ZSTD_DDict   ====== */
    public static nuint ZSTD_decompressBegin_usingDDict(ZstdDCtxS* dctx, ZstdDDictS* ddict)
    {
        assert(dctx != null);
        if (ddict != null)
        {
            var dictStart = (sbyte*)ZSTD_DDict_dictContent(ddict);
            var dictSize = ZSTD_DDict_dictSize(ddict);
            void* dictEnd = dictStart + dictSize;
            dctx->ddictIsCold = dctx->dictEnd != dictEnd ? 1 : 0;
        }

        {
            var errCode = ZSTD_decompressBegin(dctx);
            if (ERR_isError(errCode))
            {
                return errCode;
            }
        }

        if (ddict != null)
        {
            ZSTD_copyDDictParameters(dctx, ddict);
        }

        return 0;
    }

    /*! ZSTD_getDictID_fromDict() :
     *  Provides the dictID stored within dictionary.
     *  if @return == 0, the dictionary is not conformant with Zstandard specification.
     *  It can still be loaded, but as a content-only dictionary. */
    public static uint ZSTD_getDictID_fromDict(void* dict, nuint dictSize)
    {
        if (dictSize < 8)
            return 0;
        if (MEM_readLE32(dict) != 0xEC30A437)
            return 0;

        return MEM_readLE32((sbyte*)dict + 4);
    }

    /*! ZSTD_getDictID_fromFrame() :
     *  Provides the dictID required to decompress frame stored within `src`.
     *  If @return == 0, the dictID could not be decoded.
     *  This could for one of the following reasons :
     *  - The frame does not require a dictionary (most common case).
     *  - The frame was built with dictID intentionally removed.
     *    Needed dictionary is a hidden piece of information.
     *    Note : this use case also happens when using a non-conformant dictionary.
     *  - `srcSize` is too small, and as a result, frame header could not be decoded.
     *    Note : possible if `srcSize < ZSTD_FRAMEHEADERSIZE_MAX`.
     *  - This is not a Zstandard frame.
     *  When identifying the exact failure cause, it's possible to use
     *  ZSTD_getFrameHeader(), which will provide a more precise error code. */
    public static uint ZSTD_getDictID_fromFrame(void* src, nuint srcSize)
    {
        var zfp = new ZstdFrameHeader
        {
            frameContentSize = 0,
            windowSize = 0,
            blockSizeMax = 0,
            frameType = ZstdFrameTypeE.ZstdFrame,
            headerSize = 0,
            dictID = 0,
            checksumFlag = 0,
            _reserved1 = 0,
            _reserved2 = 0
        };
        var hError = ZSTD_getFrameHeader(&zfp, src, srcSize);
        if (ERR_isError(hError))
            return 0;

        return zfp.dictID;
    }

    /*! ZSTD_decompress_usingDDict() :
     *   Decompression using a pre-digested Dictionary
     *   Use dictionary without significant overhead. */
    public static nuint ZSTD_decompress_usingDDict(ZstdDCtxS* dctx, void* dst, nuint dstCapacity, void* src, nuint srcSize, ZstdDDictS* ddict)
    {
        return ZSTD_decompressMultiFrame(dctx, dst, dstCapacity, src, srcSize, null, 0, ddict);
    }

    /*=====================================
     *   Streaming decompression
     *====================================*/
    public static ZstdDCtxS* ZSTD_createDStream()
    {
        return ZSTD_createDCtx_internal(ZstdDefaultCMem);
    }

    public static ZstdDCtxS* ZSTD_initStaticDStream(void* workspace, nuint workspaceSize)
    {
        return ZSTD_initStaticDCtx(workspace, workspaceSize);
    }

    public static ZstdDCtxS* ZSTD_createDStream_advanced(ZstdCustomMem customMem)
    {
        return ZSTD_createDCtx_internal(customMem);
    }

    public static nuint ZSTD_freeDStream(ZstdDCtxS* zds)
    {
        return ZSTD_freeDCtx(zds);
    }

    /* ***  Initialization  *** */
    public static nuint ZSTD_DStreamInSize()
    {
        return (nuint)(1 << 17) + ZstdBlockHeaderSize;
    }

    public static nuint ZSTD_DStreamOutSize()
    {
        return 1 << 17;
    }

    /*! ZSTD_DCtx_loadDictionary_advanced() :
     *  Same as ZSTD_DCtx_loadDictionary(),
     *  but gives direct control over
     *  how to load the dictionary (by copy ? by reference ?)
     *  and how to interpret it (automatic ? force raw mode ? full mode only ?). */
    public static nuint ZSTD_DCtx_loadDictionary_advanced(ZstdDCtxS* dctx, void* dict, nuint dictSize, ZstdDictLoadMethodE dictLoadMethod, ZstdDictContentTypeE dictContentType)
    {
        if (dctx->streamStage != ZstdDStreamStage.ZdssInit)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorStageWrong));
        }

        ZSTD_clearDict(dctx);
        if (dict != null && dictSize != 0)
        {
            dctx->ddictLocal = ZSTD_createDDict_advanced(dict, dictSize, dictLoadMethod, dictContentType, dctx->customMem);
            if (dctx->ddictLocal == null)
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMemoryAllocation));
            }

            dctx->ddict = dctx->ddictLocal;
            dctx->dictUses = ZstdDictUsesE.ZstdUseIndefinitely;
        }

        return 0;
    }

    /*! ZSTD_DCtx_loadDictionary_byReference() :
     *  Same as ZSTD_DCtx_loadDictionary(),
     *  but references `dict` content instead of copying it into `dctx`.
     *  This saves memory if `dict` remains around.,
     *  However, it's imperative that `dict` remains accessible (and unmodified) while being used, so it must outlive decompression. */
    public static nuint ZSTD_DCtx_loadDictionary_byReference(ZstdDCtxS* dctx, void* dict, nuint dictSize)
    {
        return ZSTD_DCtx_loadDictionary_advanced(dctx, dict, dictSize, ZstdDictLoadMethodE.ZstdDlmByRef, ZstdDictContentTypeE.ZstdDctAuto);
    }

    /*! ZSTD_DCtx_loadDictionary() : Requires v1.4.0+
     *  Create an internal DDict from dict buffer, to be used to decompress all future frames.
     *  The dictionary remains valid for all future frames, until explicitly invalidated, or
     *  a new dictionary is loaded.
     * @result : 0, or an error code (which can be tested with ZSTD_isError()).
     *  Special : Adding a NULL (or 0-size) dictionary invalidates any previous dictionary,
     *            meaning "return to no-dictionary mode".
     *  Note 1 : Loading a dictionary involves building tables,
     *           which has a non-negligible impact on CPU usage and latency.
     *           It's recommended to "load once, use many times", to amortize the cost
     *  Note 2 :`dict` content will be copied internally, so `dict` can be released after loading.
     *           Use ZSTD_DCtx_loadDictionary_byReference() to reference dictionary content instead.
     *  Note 3 : Use ZSTD_DCtx_loadDictionary_advanced() to take control of
     *           how dictionary content is loaded and interpreted.
     */
    public static nuint ZSTD_DCtx_loadDictionary(ZstdDCtxS* dctx, void* dict, nuint dictSize)
    {
        return ZSTD_DCtx_loadDictionary_advanced(dctx, dict, dictSize, ZstdDictLoadMethodE.ZstdDlmByCopy, ZstdDictContentTypeE.ZstdDctAuto);
    }

    /*! ZSTD_DCtx_refPrefix_advanced() :
     *  Same as ZSTD_DCtx_refPrefix(), but gives finer control over
     *  how to interpret prefix content (automatic ? force raw mode (default) ? full mode only ?) */
    public static nuint ZSTD_DCtx_refPrefix_advanced(ZstdDCtxS* dctx, void* prefix, nuint prefixSize, ZstdDictContentTypeE dictContentType)
    {
        {
            var errCode = ZSTD_DCtx_loadDictionary_advanced(dctx, prefix, prefixSize, ZstdDictLoadMethodE.ZstdDlmByRef, dictContentType);
            if (ERR_isError(errCode))
            {
                return errCode;
            }
        }

        dctx->dictUses = ZstdDictUsesE.ZstdUseOnce;
        return 0;
    }

    /*! ZSTD_DCtx_refPrefix() : Requires v1.4.0+
     *  Reference a prefix (single-usage dictionary) to decompress next frame.
     *  This is the reverse operation of ZSTD_CCtx_refPrefix(),
     *  and must use the same prefix as the one used during compression.
     *  Prefix is **only used once**. Reference is discarded at end of frame.
     *  End of frame is reached when ZSTD_decompressStream() returns 0.
     * @result : 0, or an error code (which can be tested with ZSTD_isError()).
     *  Note 1 : Adding any prefix (including NULL) invalidates any previously set prefix or dictionary
     *  Note 2 : Prefix buffer is referenced. It **must** outlive decompression.
     *           Prefix buffer must remain unmodified up to the end of frame,
     *           reached when ZSTD_decompressStream() returns 0.
     *  Note 3 : By default, the prefix is treated as raw content (ZSTD_dct_rawContent).
     *           Use ZSTD_CCtx_refPrefix_advanced() to alter dictMode (Experimental section)
     *  Note 4 : Referencing a raw content prefix has almost no cpu nor memory cost.
     *           A full dictionary is more costly, as it requires building tables.
     */
    public static nuint ZSTD_DCtx_refPrefix(ZstdDCtxS* dctx, void* prefix, nuint prefixSize)
    {
        return ZSTD_DCtx_refPrefix_advanced(dctx, prefix, prefixSize, ZstdDictContentTypeE.ZstdDctRawContent);
    }

    /* ZSTD_initDStream_usingDict() :
     * return : expected size, aka ZSTD_startingInputLength().
     * this function cannot fail */
    public static nuint ZSTD_initDStream_usingDict(ZstdDCtxS* zds, void* dict, nuint dictSize)
    {
        {
            var errCode = ZSTD_DCtx_reset(zds, ZstdResetDirective.ZstdResetSessionOnly);
            if (ERR_isError(errCode))
            {
                return errCode;
            }
        }

        {
            var errCode = ZSTD_DCtx_loadDictionary(zds, dict, dictSize);
            if (ERR_isError(errCode))
            {
                return errCode;
            }
        }

        return ZSTD_startingInputLength(zds->format);
    }

    /* note : this variant can't fail */
    public static nuint ZSTD_initDStream(ZstdDCtxS* zds)
    {
        {
            var errCode = ZSTD_DCtx_reset(zds, ZstdResetDirective.ZstdResetSessionOnly);
            if (ERR_isError(errCode))
            {
                return errCode;
            }
        }

        {
            var errCode = ZSTD_DCtx_refDDict(zds, null);
            if (ERR_isError(errCode))
            {
                return errCode;
            }
        }

        return ZSTD_startingInputLength(zds->format);
    }

    /* ZSTD_initDStream_usingDDict() :
     * ddict will just be referenced, and must outlive decompression session
     * this function cannot fail */
    public static nuint ZSTD_initDStream_usingDDict(ZstdDCtxS* dctx, ZstdDDictS* ddict)
    {
        {
            var errCode = ZSTD_DCtx_reset(dctx, ZstdResetDirective.ZstdResetSessionOnly);
            if (ERR_isError(errCode))
            {
                return errCode;
            }
        }

        {
            var errCode = ZSTD_DCtx_refDDict(dctx, ddict);
            if (ERR_isError(errCode))
            {
                return errCode;
            }
        }

        return ZSTD_startingInputLength(dctx->format);
    }

    /* ZSTD_resetDStream() :
     * return : expected size, aka ZSTD_startingInputLength().
     * this function cannot fail */
    public static nuint ZSTD_resetDStream(ZstdDCtxS* dctx)
    {
        {
            var errCode = ZSTD_DCtx_reset(dctx, ZstdResetDirective.ZstdResetSessionOnly);
            if (ERR_isError(errCode))
            {
                return errCode;
            }
        }

        return ZSTD_startingInputLength(dctx->format);
    }

    /*! ZSTD_DCtx_refDDict() : Requires v1.4.0+
     *  Reference a prepared dictionary, to be used to decompress next frames.
     *  The dictionary remains active for decompression of future frames using same DCtx.
     *
     *  If called with ZSTD_d_refMultipleDDicts enabled, repeated calls of this function
     *  will store the DDict references in a table, and the DDict used for decompression
     *  will be determined at decompression time, as per the dict ID in the frame.
     *  The memory for the table is allocated on the first call to refDDict, and can be
     *  freed with ZSTD_freeDCtx().
     *
     *  If called with ZSTD_d_refMultipleDDicts disabled (the default), only one dictionary
     *  will be managed, and referencing a dictionary effectively "discards" any previous one.
     *
     * @result : 0, or an error code (which can be tested with ZSTD_isError()).
     *  Special: referencing a NULL DDict means "return to no-dictionary mode".
     *  Note 2 : DDict is just referenced, its lifetime must outlive its usage from DCtx.
     */
    public static nuint ZSTD_DCtx_refDDict(ZstdDCtxS* dctx, ZstdDDictS* ddict)
    {
        if (dctx->streamStage != ZstdDStreamStage.ZdssInit)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorStageWrong));
        }

        ZSTD_clearDict(dctx);
        if (ddict != null)
        {
            dctx->ddict = ddict;
            dctx->dictUses = ZstdDictUsesE.ZstdUseIndefinitely;
            if (dctx->refMultipleDDicts == ZstdRefMultipleDDictsE.ZstdRmdRefMultipleDDicts)
            {
                if (dctx->ddictSet == null)
                {
                    dctx->ddictSet = ZSTD_createDDictHashSet(dctx->customMem);
                    if (dctx->ddictSet == null)
                    {
                        return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMemoryAllocation));
                    }
                }

                assert(dctx->staticSize == 0);
                {
                    var errCode = ZSTD_DDictHashSet_addDDict(dctx->ddictSet, ddict, dctx->customMem);
                    if (ERR_isError(errCode))
                    {
                        return errCode;
                    }
                }
            }
        }

        return 0;
    }

    /* ZSTD_DCtx_setMaxWindowSize() :
     * note : no direct equivalence in ZSTD_DCtx_setParameter,
     * since this version sets windowSize, and the other sets windowLog */
    public static nuint ZSTD_DCtx_setMaxWindowSize(ZstdDCtxS* dctx, nuint maxWindowSize)
    {
        var bounds = ZSTD_dParam_getBounds(ZstdDParameter.ZstdDWindowLogMax);
        var min = (nuint)1 << bounds.lowerBound;
        var max = (nuint)1 << bounds.upperBound;
        if (dctx->streamStage != ZstdDStreamStage.ZdssInit)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorStageWrong));
        }

        if (maxWindowSize < min)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorParameterOutOfBound));
        }

        if (maxWindowSize > max)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorParameterOutOfBound));
        }

        dctx->maxWindowSize = maxWindowSize;
        return 0;
    }

    /*! ZSTD_DCtx_setFormat() :
     *  This function is REDUNDANT. Prefer ZSTD_DCtx_setParameter().
     *  Instruct the decoder context about what kind of data to decode next.
     *  This instruction is mandatory to decode data without a fully-formed header,
     *  such ZSTD_f_zstd1_magicless for example.
     * @return : 0, or an error code (which can be tested using ZSTD_isError()). */
    public static nuint ZSTD_DCtx_setFormat(ZstdDCtxS* dctx, ZstdFormatE format)
    {
        return ZSTD_DCtx_setParameter(dctx, ZstdDParameter.ZstdDExperimentalParam1, (int)format);
    }

    /*! ZSTD_dParam_getBounds() :
     *  All parameters must belong to an interval with lower and upper bounds,
     *  otherwise they will either trigger an error or be automatically clamped.
     * @return : a structure, ZSTD_bounds, which contains
     *         - an error status field, which must be tested using ZSTD_isError()
     *         - both lower and upper bounds, inclusive
     */
    public static ZstdBounds ZSTD_dParam_getBounds(ZstdDParameter dParam)
    {
        var bounds = new ZstdBounds
        {
            error = 0,
            lowerBound = 0,
            upperBound = 0
        };
        switch (dParam)
        {
            case ZstdDParameter.ZstdDWindowLogMax:
                bounds.lowerBound = 10;
                bounds.upperBound = sizeof(nuint) == 4 ? 30 : 31;
                return bounds;
            case ZstdDParameter.ZstdDExperimentalParam1:
                bounds.lowerBound = (int)ZstdFormatE.ZstdFZstd1;
                bounds.upperBound = (int)ZstdFormatE.ZstdFZstd1Magicless;
                return bounds;
            case ZstdDParameter.ZstdDExperimentalParam2:
                bounds.lowerBound = (int)ZstdBufferModeE.ZstdBmBuffered;
                bounds.upperBound = (int)ZstdBufferModeE.ZstdBmStable;
                return bounds;
            case ZstdDParameter.ZstdDExperimentalParam3:
                bounds.lowerBound = (int)ZstdForceIgnoreChecksumE.ZstdDValidateChecksum;
                bounds.upperBound = (int)ZstdForceIgnoreChecksumE.ZstdDIgnoreChecksum;
                return bounds;
            case ZstdDParameter.ZstdDExperimentalParam4:
                bounds.lowerBound = (int)ZstdRefMultipleDDictsE.ZstdRmdRefSingleDDict;
                bounds.upperBound = (int)ZstdRefMultipleDDictsE.ZstdRmdRefMultipleDDicts;
                return bounds;
            case ZstdDParameter.ZstdDExperimentalParam5:
                bounds.lowerBound = 0;
                bounds.upperBound = 1;
                return bounds;
            case ZstdDParameter.ZstdDExperimentalParam6:
                bounds.lowerBound = 1 << 10;
                bounds.upperBound = 1 << 17;
                return bounds;
            default:
                break;
        }

        bounds.error = unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorParameterUnsupported));
        return bounds;
    }

    /* ZSTD_dParam_withinBounds:
     * @return 1 if value is within dParam bounds,
     * 0 otherwise */
    private static int ZSTD_dParam_withinBounds(ZstdDParameter dParam, int value)
    {
        var bounds = ZSTD_dParam_getBounds(dParam);
        if (ERR_isError(bounds.error))
            return 0;
        if (value < bounds.lowerBound)
            return 0;
        if (value > bounds.upperBound)
            return 0;

        return 1;
    }

    /*! ZSTD_DCtx_getParameter() :
     *  Get the requested decompression parameter value, selected by enum ZSTD_dParameter,
     *  and store it into int* value.
     * @return : 0, or an error code (which can be tested with ZSTD_isError()).
     */
    public static nuint ZSTD_DCtx_getParameter(ZstdDCtxS* dctx, ZstdDParameter param, int* value)
    {
        switch (param)
        {
            case ZstdDParameter.ZstdDWindowLogMax:
                *value = (int)ZSTD_highbit32((uint)dctx->maxWindowSize);
                return 0;
            case ZstdDParameter.ZstdDExperimentalParam1:
                *value = (int)dctx->format;
                return 0;
            case ZstdDParameter.ZstdDExperimentalParam2:
                *value = (int)dctx->outBufferMode;
                return 0;
            case ZstdDParameter.ZstdDExperimentalParam3:
                *value = (int)dctx->forceIgnoreChecksum;
                return 0;
            case ZstdDParameter.ZstdDExperimentalParam4:
                *value = (int)dctx->refMultipleDDicts;
                return 0;
            case ZstdDParameter.ZstdDExperimentalParam5:
                *value = dctx->disableHufAsm;
                return 0;
            case ZstdDParameter.ZstdDExperimentalParam6:
                *value = dctx->maxBlockSizeParam;
                return 0;
            default:
                break;
        }

        return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorParameterUnsupported));
    }

    /*! ZSTD_DCtx_setParameter() :
     *  Set one compression parameter, selected by enum ZSTD_dParameter.
     *  All parameters have valid bounds. Bounds can be queried using ZSTD_dParam_getBounds().
     *  Providing a value beyond bound will either clamp it, or trigger an error (depending on parameter).
     *  Setting a parameter is only possible during frame initialization (before starting decompression).
     * @return : 0, or an error code (which can be tested using ZSTD_isError()).
     */
    public static nuint ZSTD_DCtx_setParameter(ZstdDCtxS* dctx, ZstdDParameter dParam, int value)
    {
        if (dctx->streamStage != ZstdDStreamStage.ZdssInit)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorStageWrong));
        }

        switch (dParam)
        {
            case ZstdDParameter.ZstdDWindowLogMax:
                if (value == 0)
                {
                    value = 27;
                }

            {
                if (ZSTD_dParam_withinBounds(ZstdDParameter.ZstdDWindowLogMax, value) == 0)
                {
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorParameterOutOfBound));
                }
            }

                dctx->maxWindowSize = (nuint)1 << value;
                return 0;
            case ZstdDParameter.ZstdDExperimentalParam1:
            {
                if (ZSTD_dParam_withinBounds(ZstdDParameter.ZstdDExperimentalParam1, value) == 0)
                {
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorParameterOutOfBound));
                }
            }

                dctx->format = (ZstdFormatE)value;
                return 0;
            case ZstdDParameter.ZstdDExperimentalParam2:
            {
                if (ZSTD_dParam_withinBounds(ZstdDParameter.ZstdDExperimentalParam2, value) == 0)
                {
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorParameterOutOfBound));
                }
            }

                dctx->outBufferMode = (ZstdBufferModeE)value;
                return 0;
            case ZstdDParameter.ZstdDExperimentalParam3:
            {
                if (ZSTD_dParam_withinBounds(ZstdDParameter.ZstdDExperimentalParam3, value) == 0)
                {
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorParameterOutOfBound));
                }
            }

                dctx->forceIgnoreChecksum = (ZstdForceIgnoreChecksumE)value;
                return 0;
            case ZstdDParameter.ZstdDExperimentalParam4:
            {
                if (ZSTD_dParam_withinBounds(ZstdDParameter.ZstdDExperimentalParam4, value) == 0)
                {
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorParameterOutOfBound));
                }
            }

                if (dctx->staticSize != 0)
                {
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorParameterUnsupported));
                }

                dctx->refMultipleDDicts = (ZstdRefMultipleDDictsE)value;
                return 0;
            case ZstdDParameter.ZstdDExperimentalParam5:
            {
                if (ZSTD_dParam_withinBounds(ZstdDParameter.ZstdDExperimentalParam5, value) == 0)
                {
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorParameterOutOfBound));
                }
            }

                dctx->disableHufAsm = value != 0 ? 1 : 0;
                return 0;
            case ZstdDParameter.ZstdDExperimentalParam6:
                if (value != 0)
                {
                    if (ZSTD_dParam_withinBounds(ZstdDParameter.ZstdDExperimentalParam6, value) == 0)
                    {
                        return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorParameterOutOfBound));
                    }
                }

                dctx->maxBlockSizeParam = value;
                return 0;
            default:
                break;
        }

        return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorParameterUnsupported));
    }

    /*! ZSTD_DCtx_reset() :
     *  Return a DCtx to clean state.
     *  Session and parameters can be reset jointly or separately.
     *  Parameters can only be reset when no active frame is being decompressed.
     * @return : 0, or an error code, which can be tested with ZSTD_isError()
     */
    public static nuint ZSTD_DCtx_reset(ZstdDCtxS* dctx, ZstdResetDirective reset)
    {
        if (reset is ZstdResetDirective.ZstdResetSessionOnly or ZstdResetDirective.ZstdResetSessionAndParameters)
        {
            dctx->streamStage = ZstdDStreamStage.ZdssInit;
            dctx->noForwardProgress = 0;
            dctx->isFrameDecompression = 1;
        }

        if (reset is ZstdResetDirective.ZstdResetParameters or ZstdResetDirective.ZstdResetSessionAndParameters)
        {
            if (dctx->streamStage != ZstdDStreamStage.ZdssInit)
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorStageWrong));
            }

            ZSTD_clearDict(dctx);
            ZSTD_DCtx_resetParameters(dctx);
        }

        return 0;
    }

    public static nuint ZSTD_sizeof_DStream(ZstdDCtxS* dctx)
    {
        return ZSTD_sizeof_DCtx(dctx);
    }

    private static nuint ZSTD_decodingBufferSize_internal(ulong windowSize, ulong frameContentSize, nuint blockSizeMax)
    {
        var blockSize = (nuint)(windowSize < 1 << 17 ? windowSize : 1 << 17) < blockSizeMax ? (nuint)(windowSize < 1 << 17 ? windowSize : 1 << 17) : blockSizeMax;
        /* We need blockSize + WILDCOPY_OVERLENGTH worth of buffer so that if a block
         * ends at windowSize + WILDCOPY_OVERLENGTH + 1 bytes, we can start writing
         * the block at the beginning of the output buffer, and maintain a full window.
         *
         * We need another blockSize worth of buffer so that we can store split
         * literals at the end of the block without overwriting the extDict window.
         */
        var neededRbSize = windowSize + blockSize * 2 + 32 * 2;
        var neededSize = frameContentSize < neededRbSize ? frameContentSize : neededRbSize;
        var minRbSize = (nuint)neededSize;
        if (minRbSize != neededSize)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorFrameParameterWindowTooLarge));
        }

        return minRbSize;
    }

    /*=====   Buffer-less streaming decompression functions  =====*/
    public static nuint ZSTD_decodingBufferSize_min(ulong windowSize, ulong frameContentSize)
    {
        return ZSTD_decodingBufferSize_internal(windowSize, frameContentSize, 1 << 17);
    }

    public static nuint ZSTD_estimateDStreamSize(nuint windowSize)
    {
        var blockSize = windowSize < 1 << 17 ? windowSize : 1 << 17;
        /* no block can be larger */
        var inBuffSize = blockSize;
        var outBuffSize = ZSTD_decodingBufferSize_min(windowSize, unchecked(0UL - 1));
        return ZSTD_estimateDCtxSize() + inBuffSize + outBuffSize;
    }

    public static nuint ZSTD_estimateDStreamSize_fromFrame(void* src, nuint srcSize)
    {
        /* note : should be user-selectable, but requires an additional parameter (or a dctx) */
        var windowSizeMax = 1U << (sizeof(nuint) == 4 ? 30 : 31);
        ZstdFrameHeader zfh;
        var err = ZSTD_getFrameHeader(&zfh, src, srcSize);
        if (ERR_isError(err))
            return err;

        if (err > 0)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
        }

        if (zfh.windowSize > windowSizeMax)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorFrameParameterWindowTooLarge));
        }

        return ZSTD_estimateDStreamSize((nuint)zfh.windowSize);
    }

    /* *****   Decompression   ***** */
    private static int ZSTD_DCtx_isOverflow(ZstdDCtxS* zds, nuint neededInBuffSize, nuint neededOutBuffSize)
    {
        return zds->inBuffSize + zds->outBuffSize >= (neededInBuffSize + neededOutBuffSize) * 3 ? 1 : 0;
    }

    private static void ZSTD_DCtx_updateOversizedDuration(ZstdDCtxS* zds, nuint neededInBuffSize, nuint neededOutBuffSize)
    {
        if (ZSTD_DCtx_isOverflow(zds, neededInBuffSize, neededOutBuffSize) != 0)
        {
            zds->oversizedDuration++;
        }
        else
        {
            zds->oversizedDuration = 0;
        }
    }

    private static int ZSTD_DCtx_isOversizedTooLong(ZstdDCtxS* zds)
    {
        return zds->oversizedDuration >= 128 ? 1 : 0;
    }

    /* Checks that the output buffer hasn't changed if ZSTD_obm_stable is used. */
    private static nuint ZSTD_checkOutBuffer(ZstdDCtxS* zds, ZstdOutBufferS* output)
    {
        var expect = zds->expectedOutBuffer;
        if (zds->outBufferMode != ZstdBufferModeE.ZstdBmStable)
            return 0;
        if (zds->streamStage == ZstdDStreamStage.ZdssInit)
            return 0;
        if (expect.dst == output->dst && expect.pos == output->pos && expect.size == output->size)
            return 0;

        return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstBufferWrong));
    }

    /* Calls ZSTD_decompressContinue() with the right parameters for ZSTD_decompressStream()
     * and updates the stage and the output buffer state. This call is extracted so it can be
     * used both when reading directly from the ZSTD_inBuffer, and in buffered input mode.
     * NOTE: You must break after calling this function since the streamStage is modified.
     */
    private static nuint ZSTD_decompressContinueStream(ZstdDCtxS* zds, sbyte** op, sbyte* oend, void* src, nuint srcSize)
    {
        var isSkipFrame = ZSTD_isSkipFrame(zds);
        if (zds->outBufferMode == ZstdBufferModeE.ZstdBmBuffered)
        {
            var dstSize = isSkipFrame != 0 ? 0 : zds->outBuffSize - zds->outStart;
            var decodedSize = ZSTD_decompressContinue(zds, zds->outBuff + zds->outStart, dstSize, src, srcSize);
            {
                var errCode = decodedSize;
                if (ERR_isError(errCode))
                {
                    return errCode;
                }
            }

            if (decodedSize == 0 && isSkipFrame == 0)
            {
                zds->streamStage = ZstdDStreamStage.ZdssRead;
            }
            else
            {
                zds->outEnd = zds->outStart + decodedSize;
                zds->streamStage = ZstdDStreamStage.ZdssFlush;
            }
        }
        else
        {
            /* Write directly into the output buffer */
            var dstSize = isSkipFrame != 0 ? 0 : (nuint)(oend - *op);
            var decodedSize = ZSTD_decompressContinue(zds, *op, dstSize, src, srcSize);
            {
                var errCode = decodedSize;
                if (ERR_isError(errCode))
                {
                    return errCode;
                }
            }

            *op += decodedSize;
            zds->streamStage = ZstdDStreamStage.ZdssRead;
            assert(*op <= oend);
            assert(zds->outBufferMode == ZstdBufferModeE.ZstdBmStable);
        }

        return 0;
    }

    /*! ZSTD_decompressStream() :
     * Streaming decompression function.
     * Call repetitively to consume full input updating it as necessary.
     * Function will update both input and output `pos` fields exposing current state via these fields:
     * - `input.pos < input.size`, some input remaining and caller should provide remaining input
     *   on the next call.
     * - `output.pos < output.size`, decoder flushed internal output buffer.
     * - `output.pos == output.size`, unflushed data potentially present in the internal buffers,
     *   check ZSTD_decompressStream() @return value,
     *   if > 0, invoke it again to flush remaining data to output.
     * Note : with no additional input, amount of data flushed <= ZSTD_BLOCKSIZE_MAX.
     *
     * @return : 0 when a frame is completely decoded and fully flushed,
     *           or an error code, which can be tested using ZSTD_isError(),
     *           or any other value > 0, which means there is some decoding or flushing to do to complete current frame.
     *
     * Note: when an operation returns with an error code, the @zds state may be left in undefined state.
     *       It's UB to invoke `ZSTD_decompressStream()` on such a state.
     *       In order to re-use such a state, it must be first reset,
     *       which can be done explicitly (`ZSTD_DCtx_reset()`),
     *       or is implied for operations starting some new decompression job (`ZSTD_initDStream`, `ZSTD_decompressDCtx()`, `ZSTD_decompress_usingDict()`)
     */
    public static nuint ZSTD_decompressStream(ZstdDCtxS* zds, ZstdOutBufferS* output, ZstdInBufferS* input)
    {
        var src = (sbyte*)input->src;
        var istart = input->pos != 0 ? src + input->pos : src;
        var iend = input->size != 0 ? src + input->size : src;
        var ip = istart;
        var dst = (sbyte*)output->dst;
        var ostart = output->pos != 0 ? dst + output->pos : dst;
        var oend = output->size != 0 ? dst + output->size : dst;
        var op = ostart;
        uint someMoreWork = 1;
        assert(zds != null);
        if (input->pos > input->size)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
        }

        if (output->pos > output->size)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));
        }

        {
            var errCode = ZSTD_checkOutBuffer(zds, output);
            if (ERR_isError(errCode))
            {
                return errCode;
            }
        }

        while (someMoreWork != 0)
        {
            switch (zds->streamStage)
            {
                case ZstdDStreamStage.ZdssInit:
                    zds->streamStage = ZstdDStreamStage.ZdssLoadHeader;
                    zds->lhSize = zds->inPos = zds->outStart = zds->outEnd = 0;
                    zds->hostageByte = 0;
                    zds->expectedOutBuffer = *output;
                    goto case ZstdDStreamStage.ZdssLoadHeader;
                case ZstdDStreamStage.ZdssLoadHeader:
                {
                    var hSize = ZSTD_getFrameHeader_advanced(&zds->fParams, zds->headerBuffer, zds->lhSize, zds->format);
                    if (zds->refMultipleDDicts != default && zds->ddictSet != null)
                    {
                        ZSTD_DCtx_selectFrameDDict(zds);
                    }

                    if (ERR_isError(hSize))
                    {
                        return hSize;
                    }

                    if (hSize != 0)
                    {
                        /* if hSize!=0, hSize > zds->lhSize */
                        var toLoad = hSize - zds->lhSize;
                        var remainingInput = (nuint)(iend - ip);
                        assert(iend >= ip);
                        if (toLoad > remainingInput)
                        {
                            if (remainingInput > 0)
                            {
                                memcpy(zds->headerBuffer + zds->lhSize, ip, (uint)remainingInput);
                                zds->lhSize += remainingInput;
                            }

                            input->pos = input->size;
                            {
                                /* check first few bytes */
                                var errCode = ZSTD_getFrameHeader_advanced(&zds->fParams, zds->headerBuffer, zds->lhSize, zds->format);
                                if (ERR_isError(errCode))
                                {
                                    return errCode;
                                }
                            }

                            return ((nuint)(zds->format == ZstdFormatE.ZstdFZstd1 ? 6 : 2) > hSize ? (nuint)(zds->format == ZstdFormatE.ZstdFZstd1 ? 6 : 2) : hSize) - zds->lhSize + ZstdBlockHeaderSize;
                        }

                        assert(ip != null);
                        memcpy(zds->headerBuffer + zds->lhSize, ip, (uint)toLoad);
                        zds->lhSize = hSize;
                        ip += toLoad;
                        break;
                    }
                }

                    if (zds->fParams.frameContentSize != unchecked(0UL - 1) && zds->fParams.frameType != ZstdFrameTypeE.ZstdSkippableFrame && (nuint)(oend - op) >= zds->fParams.frameContentSize)
                    {
                        var cSize = ZSTD_findFrameCompressedSize_advanced(istart, (nuint)(iend - istart), zds->format);
                        if (cSize <= (nuint)(iend - istart))
                        {
                            /* shortcut : using single-pass mode */
                            var decompressedSize = ZSTD_decompress_usingDDict(zds, op, (nuint)(oend - op), istart, cSize, ZSTD_getDDict(zds));
                            if (ERR_isError(decompressedSize))
                                return decompressedSize;

                            assert(istart != null);
                            ip = istart + cSize;
                            op = op != null ? op + decompressedSize : op;
                            zds->expected = 0;
                            zds->streamStage = ZstdDStreamStage.ZdssInit;
                            someMoreWork = 0;
                            break;
                        }
                    }

                    if (zds->outBufferMode == ZstdBufferModeE.ZstdBmStable && zds->fParams.frameType != ZstdFrameTypeE.ZstdSkippableFrame && zds->fParams.frameContentSize != unchecked(0UL - 1) && (nuint)(oend - op) < zds->fParams.frameContentSize)
                    {
                        return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));
                    }

                {
                    var errCode = ZSTD_decompressBegin_usingDDict(zds, ZSTD_getDDict(zds));
                    if (ERR_isError(errCode))
                    {
                        return errCode;
                    }
                }

                    if (zds->format == ZstdFormatE.ZstdFZstd1 && (MEM_readLE32(zds->headerBuffer) & 0xFFFFFFF0) == 0x184D2A50)
                    {
                        zds->expected = MEM_readLE32(zds->headerBuffer + 4);
                        zds->stage = ZstdDStage.ZstDdsSkipFrame;
                    }
                    else
                    {
                        {
                            var errCode = ZSTD_decodeFrameHeader(zds, zds->headerBuffer, zds->lhSize);
                            if (ERR_isError(errCode))
                            {
                                return errCode;
                            }
                        }

                        zds->expected = ZstdBlockHeaderSize;
                        zds->stage = ZstdDStage.ZstDdsDecodeBlockHeader;
                    }

                    zds->fParams.windowSize = zds->fParams.windowSize > 1U << 10 ? zds->fParams.windowSize : 1U << 10;
                    if (zds->fParams.windowSize > zds->maxWindowSize)
                    {
                        return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorFrameParameterWindowTooLarge));
                    }

                    if (zds->maxBlockSizeParam != 0)
                    {
                        zds->fParams.blockSizeMax = zds->fParams.blockSizeMax < (uint)zds->maxBlockSizeParam ? zds->fParams.blockSizeMax : (uint)zds->maxBlockSizeParam;
                    }

                {
                    /* frame checksum */
                    nuint neededInBuffSize = zds->fParams.blockSizeMax > 4 ? zds->fParams.blockSizeMax : 4;
                    var neededOutBuffSize = zds->outBufferMode == ZstdBufferModeE.ZstdBmBuffered ? ZSTD_decodingBufferSize_internal(zds->fParams.windowSize, zds->fParams.frameContentSize, zds->fParams.blockSizeMax) : 0;
                    ZSTD_DCtx_updateOversizedDuration(zds, neededInBuffSize, neededOutBuffSize);
                    {
                        var tooSmall = zds->inBuffSize < neededInBuffSize || zds->outBuffSize < neededOutBuffSize ? 1 : 0;
                        var tooLarge = ZSTD_DCtx_isOversizedTooLong(zds);
                        if (tooSmall != 0 || tooLarge != 0)
                        {
                            var bufferSize = neededInBuffSize + neededOutBuffSize;
                            if (zds->staticSize != 0)
                            {
                                assert(zds->staticSize >= (nuint)sizeof(ZstdDCtxS));
                                if (bufferSize > zds->staticSize - (nuint)sizeof(ZstdDCtxS))
                                {
                                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMemoryAllocation));
                                }
                            }
                            else
                            {
                                ZSTD_customFree(zds->inBuff, zds->customMem);
                                zds->inBuffSize = 0;
                                zds->outBuffSize = 0;
                                zds->inBuff = (sbyte*)ZSTD_customMalloc(bufferSize, zds->customMem);
                                if (zds->inBuff == null)
                                {
                                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMemoryAllocation));
                                }
                            }

                            zds->inBuffSize = neededInBuffSize;
                            zds->outBuff = zds->inBuff + zds->inBuffSize;
                            zds->outBuffSize = neededOutBuffSize;
                        }
                    }
                }

                    zds->streamStage = ZstdDStreamStage.ZdssRead;
                    goto case ZstdDStreamStage.ZdssRead;
                case ZstdDStreamStage.ZdssRead:
                {
                    var neededInSize = ZSTD_nextSrcSizeToDecompressWithInputSize(zds, (nuint)(iend - ip));
                    if (neededInSize == 0)
                    {
                        zds->streamStage = ZstdDStreamStage.ZdssInit;
                        someMoreWork = 0;
                        break;
                    }

                    if ((nuint)(iend - ip) >= neededInSize)
                    {
                        {
                            var errCode = ZSTD_decompressContinueStream(zds, &op, oend, ip, neededInSize);
                            if (ERR_isError(errCode))
                            {
                                return errCode;
                            }
                        }

                        assert(ip != null);
                        ip += neededInSize;
                        break;
                    }
                }

                    if (ip == iend)
                    {
                        someMoreWork = 0;
                        break;
                    }

                    zds->streamStage = ZstdDStreamStage.ZdssLoad;
                    goto case ZstdDStreamStage.ZdssLoad;
                case ZstdDStreamStage.ZdssLoad:
                {
                    var neededInSize = ZSTD_nextSrcSizeToDecompress(zds);
                    var toLoad = neededInSize - zds->inPos;
                    var isSkipFrame = ZSTD_isSkipFrame(zds);
                    nuint loadedSize;
                    assert(neededInSize == ZSTD_nextSrcSizeToDecompressWithInputSize(zds, (nuint)(iend - ip)));
                    if (isSkipFrame != 0)
                    {
                        loadedSize = toLoad < (nuint)(iend - ip) ? toLoad : (nuint)(iend - ip);
                    }
                    else
                    {
                        if (toLoad > zds->inBuffSize - zds->inPos)
                        {
                            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
                        }

                        loadedSize = ZSTD_limitCopy(zds->inBuff + zds->inPos, toLoad, ip, (nuint)(iend - ip));
                    }

                    if (loadedSize != 0)
                    {
                        ip += loadedSize;
                        zds->inPos += loadedSize;
                    }

                    if (loadedSize < toLoad)
                    {
                        someMoreWork = 0;
                        break;
                    }

                    zds->inPos = 0;
                    {
                        var errCode = ZSTD_decompressContinueStream(zds, &op, oend, zds->inBuff, neededInSize);
                        if (ERR_isError(errCode))
                        {
                            return errCode;
                        }
                    }

                    break;
                }

                case ZstdDStreamStage.ZdssFlush:
                {
                    var toFlushSize = zds->outEnd - zds->outStart;
                    var flushedSize = ZSTD_limitCopy(op, (nuint)(oend - op), zds->outBuff + zds->outStart, toFlushSize);
                    op = op != null ? op + flushedSize : op;
                    zds->outStart += flushedSize;
                    if (flushedSize == toFlushSize)
                    {
                        zds->streamStage = ZstdDStreamStage.ZdssRead;
                        if (zds->outBuffSize < zds->fParams.frameContentSize && zds->outStart + zds->fParams.blockSizeMax > zds->outBuffSize)
                        {
                            zds->outStart = zds->outEnd = 0;
                        }

                        break;
                    }
                }

                    someMoreWork = 0;
                    break;
                default:
                    assert(0 != 0);
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorGeneric));
            }
        }

        input->pos = (nuint)(ip - (sbyte*)input->src);
        output->pos = (nuint)(op - (sbyte*)output->dst);
        zds->expectedOutBuffer = *output;
        if (ip == istart && op == ostart)
        {
            zds->noForwardProgress++;
            if (zds->noForwardProgress >= 16)
            {
                if (op == oend)
                {
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorNoForwardProgressDestFull));
                }

                if (ip == iend)
                {
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorNoForwardProgressInputEmpty));
                }

                assert(0 != 0);
            }
        }
        else
        {
            zds->noForwardProgress = 0;
        }

        {
            var nextSrcSizeHint = ZSTD_nextSrcSizeToDecompress(zds);
            if (nextSrcSizeHint == 0)
            {
                if (zds->outEnd == zds->outStart)
                {
                    if (zds->hostageByte != 0)
                    {
                        if (input->pos >= input->size)
                        {
                            zds->streamStage = ZstdDStreamStage.ZdssRead;
                            return 1;
                        }

                        input->pos++;
                    }

                    return 0;
                }

                if (zds->hostageByte == 0)
                {
                    input->pos--;
                    zds->hostageByte = 1;
                }

                return 1;
            }

            nextSrcSizeHint += ZstdBlockHeaderSize * (nuint)(ZSTD_nextInputType(zds) == ZstdNextInputTypeE.ZstDnitBlock ? 1 : 0);
            assert(zds->inPos <= nextSrcSizeHint);
            nextSrcSizeHint -= zds->inPos;
            return nextSrcSizeHint;
        }
    }

    /*! ZSTD_decompressStream_simpleArgs() :
     *  Same as ZSTD_decompressStream(),
     *  but using only integral types as arguments.
     *  This can be helpful for binders from dynamic languages
     *  which have troubles handling structures containing memory pointers.
     */
    public static nuint ZSTD_decompressStream_simpleArgs(ZstdDCtxS* dctx, void* dst, nuint dstCapacity, nuint* dstPos, void* src, nuint srcSize, nuint* srcPos)
    {
        ZstdOutBufferS output;
        ZstdInBufferS input;
        output.dst = dst;
        output.size = dstCapacity;
        output.pos = *dstPos;
        input.src = src;
        input.size = srcSize;
        input.pos = *srcPos;
        {
            var cErr = ZSTD_decompressStream(dctx, &output, &input);
            *dstPos = output.pos;
            *srcPos = input.pos;
            return cErr;
        }
    }
}