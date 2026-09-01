using static VendoredZSTD.UnsafeHelper;

namespace VendoredZSTD.Unsafe;

public static unsafe partial class Methods
{
    /*
     * ZSTD_compressSubBlock_literal() :
     * Compresses literals section for a sub-block.
     * When we have to write the Huffman table we will sometimes choose a header
     * size larger than necessary. This is because we have to pick the header size
     * before we know the table size + compressed size, so we have a bound on the
     * table size. If we guessed incorrectly, we fall back to uncompressed literals.
     *
     * We write the header when writeEntropy=1 and set entropyWritten=1 when we succeeded
     * in writing the header, otherwise it is set to 0.
     *
     * hufMetadata->hType has literals block type info.
     * If it is set_basic, all sub-blocks literals section will be Raw_Literals_Block.
     * If it is set_rle, all sub-blocks literals section will be RLE_Literals_Block.
     * If it is set_compressed, first sub-block's literals section will be Compressed_Literals_Block
     * If it is set_compressed, first sub-block's literals section will be Treeless_Literals_Block
     * and the following sub-blocks' literals sections will be Treeless_Literals_Block.
     * @return : compressed size of literals section of a sub-block
     * Or 0 if unable to compress.
     * Or error code
     */
    private static nuint ZSTD_compressSubBlock_literal(
        nuint* hufTable,
        ZstdHufCTablesMetadataT* hufMetadata,
        byte* literals,
        nuint litSize,
        void* dst,
        nuint dstSize,
        int bmi2,
        int writeEntropy,
        int* entropyWritten
    )
    {
        var header = (nuint)(writeEntropy != 0 ? 200 : 0);
        var lhSize = (nuint)(
            3
            + (litSize >= 1 * (1 << 10) - header ? 1 : 0)
            + (litSize >= 16 * (1 << 10) - header ? 1 : 0)
        );
        var ostart = (byte*)dst;
        var oend = ostart + dstSize;
        var op = ostart + lhSize;
        var singleStream = lhSize == 3 ? 1U : 0U;
        var hType = writeEntropy != 0 ? hufMetadata->hType : SymbolEncodingTypeE.SetRepeat;
        nuint cLitSize = 0;
        *entropyWritten = 0;
        if (litSize == 0 || hufMetadata->hType == SymbolEncodingTypeE.SetBasic)
            return ZSTD_noCompressLiterals(dst, dstSize, literals, litSize);

        if (hufMetadata->hType == SymbolEncodingTypeE.SetRle)
            return ZSTD_compressRleLiteralsBlock(dst, dstSize, literals, litSize);

        assert(litSize > 0);
        assert(
            hufMetadata->hType == SymbolEncodingTypeE.SetCompressed
            || hufMetadata->hType == SymbolEncodingTypeE.SetRepeat
        );
        if (writeEntropy != 0 && hufMetadata->hType == SymbolEncodingTypeE.SetCompressed)
        {
            memcpy(op, hufMetadata->hufDesBuffer, (uint)hufMetadata->hufDesSize);
            op += hufMetadata->hufDesSize;
            cLitSize += hufMetadata->hufDesSize;
        }

        {
            var flags = bmi2 != 0 ? (int)HufFlagsE.HufFlagsBmi2 : 0;
            var cSize =
                singleStream != 0
                    ? HUF_compress1X_usingCTable(
                        op,
                        (nuint)(oend - op),
                        literals,
                        litSize,
                        hufTable,
                        flags
                    )
                    : HUF_compress4X_usingCTable(
                        op,
                        (nuint)(oend - op),
                        literals,
                        litSize,
                        hufTable,
                        flags
                    );
            op += cSize;
            cLitSize += cSize;
            if (cSize == 0 || ERR_isError(cSize))
                return 0;

            if (writeEntropy == 0 && cLitSize >= litSize)
                return ZSTD_noCompressLiterals(dst, dstSize, literals, litSize);

            if (
                lhSize
                < (nuint)(
                    3 + (cLitSize >= 1 * (1 << 10) ? 1 : 0) + (cLitSize >= 16 * (1 << 10) ? 1 : 0)
                )
            )
            {
                assert(cLitSize > litSize);
                return ZSTD_noCompressLiterals(dst, dstSize, literals, litSize);
            }
        }

        switch (lhSize)
        {
            case 3:
            {
                var lhc =
                    (uint)(hType + ((singleStream == 0 ? 1 : 0) << 2))
                    + ((uint)litSize << 4)
                    + ((uint)cLitSize << 14);
                MEM_writeLE24(ostart, lhc);
                break;
            }

            case 4:
            {
                var lhc = (uint)(hType + (2 << 2)) + ((uint)litSize << 4) + ((uint)cLitSize << 18);
                MEM_writeLE32(ostart, lhc);
                break;
            }

            case 5:
            {
                var lhc = (uint)(hType + (3 << 2)) + ((uint)litSize << 4) + ((uint)cLitSize << 22);
                MEM_writeLE32(ostart, lhc);
                ostart[4] = (byte)(cLitSize >> 10);
                break;
            }

            default:
                assert(0 != 0);
                break;
        }

        *entropyWritten = 1;
        return (nuint)(op - ostart);
    }

    private static nuint ZSTD_seqDecompressedSize(
        SeqStoreT* seqStore,
        SeqDefS* sequences,
        nuint nbSeq,
        nuint litSize,
        int lastSequence
    )
    {
        var sstart = sequences;
        var send = sequences + nbSeq;
        var sp = sstart;
        nuint matchLengthSum = 0;
        nuint litLengthSum = 0;
        while (send > sp)
        {
            var seqLen = ZSTD_getSequenceLength(seqStore, sp);
            litLengthSum += seqLen.litLength;
            matchLengthSum += seqLen.matchLength;
            sp++;
        }

        assert(litLengthSum <= litSize);
#if DEBUG
        if (lastSequence == 0)
            assert(litLengthSum == litSize);
#endif

        return matchLengthSum + litSize;
    }

    /*
     * ZSTD_compressSubBlock_sequences() :
     * Compresses sequences section for a sub-block.
     * fseMetadata->llType, fseMetadata->ofType, and fseMetadata->mlType have
     * symbol compression modes for the super-block.
     * The first successfully compressed block will have these in its header.
     * We set entropyWritten=1 when we succeed in compressing the sequences.
     * The following sub-blocks will always have repeat mode.
     * @return : compressed size of sequences section of a sub-block
     * Or 0 if it is unable to compress
     * Or error code.
     */
    private static nuint ZSTD_compressSubBlock_sequences(
        ZstdFseCTablesT* fseTables,
        ZstdFseCTablesMetadataT* fseMetadata,
        SeqDefS* sequences,
        nuint nbSeq,
        byte* llCode,
        byte* mlCode,
        byte* ofCode,
        ZstdCCtxParamsS* cctxParams,
        void* dst,
        nuint dstCapacity,
        int bmi2,
        int writeEntropy,
        int* entropyWritten
    )
    {
        var longOffsets = cctxParams->cParams.windowLog > (uint)(MEM_32bits ? 25 : 57) ? 1 : 0;
        var ostart = (byte*)dst;
        var oend = ostart + dstCapacity;
        var op = ostart;
        byte* seqHead;
        *entropyWritten = 0;
        if (oend - op < 3 + 1)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));

        if (nbSeq < 0x7F)
        {
            *op++ = (byte)nbSeq;
        }
        else if (nbSeq < 0x7F00)
        {
            op[0] = (byte)((nbSeq >> 8) + 0x80);
            op[1] = (byte)nbSeq;
            op += 2;
        }
        else
        {
            op[0] = 0xFF;
            MEM_writeLE16(op + 1, (ushort)(nbSeq - 0x7F00));
            op += 3;
        }

        if (nbSeq == 0)
            return (nuint)(op - ostart);

        seqHead = op++;
        if (writeEntropy != 0)
        {
            var lLtype = (uint)fseMetadata->llType;
            var offtype = (uint)fseMetadata->ofType;
            var mLtype = (uint)fseMetadata->mlType;
            *seqHead = (byte)((lLtype << 6) + (offtype << 4) + (mLtype << 2));
            memcpy(op, fseMetadata->fseTablesBuffer, (uint)fseMetadata->fseTablesSize);
            op += fseMetadata->fseTablesSize;
        }
        else
        {
            const uint repeat = (uint)SymbolEncodingTypeE.SetRepeat;
            *seqHead = (byte)((repeat << 6) + (repeat << 4) + (repeat << 2));
        }

        {
            var bitstreamSize = ZSTD_encodeSequences(
                op,
                (nuint)(oend - op),
                fseTables->matchlengthCTable,
                mlCode,
                fseTables->offcodeCTable,
                ofCode,
                fseTables->litlengthCTable,
                llCode,
                sequences,
                nbSeq,
                longOffsets,
                bmi2
            );
            {
                var errCode = bitstreamSize;
                if (ERR_isError(errCode))
                    return errCode;
            }

            op += bitstreamSize;
            if (
                writeEntropy != 0
                && fseMetadata->lastCountSize != 0
                && fseMetadata->lastCountSize + bitstreamSize < 4
            )
            {
                assert(fseMetadata->lastCountSize + bitstreamSize == 3);
                return 0;
            }
        }

        if (op - seqHead < 4)
            return 0;

        *entropyWritten = 1;
        return (nuint)(op - ostart);
    }

    /*
     * ZSTD_compressSubBlock() :
     * Compresses a single sub-block.
     * @return : compressed size of the sub-block
     * Or 0 if it failed to compress.
     */
    private static nuint ZSTD_compressSubBlock(
        ZstdEntropyCTablesT* entropy,
        ZstdEntropyCTablesMetadataT* entropyMetadata,
        SeqDefS* sequences,
        nuint nbSeq,
        byte* literals,
        nuint litSize,
        byte* llCode,
        byte* mlCode,
        byte* ofCode,
        ZstdCCtxParamsS* cctxParams,
        void* dst,
        nuint dstCapacity,
        int bmi2,
        int writeLitEntropy,
        int writeSeqEntropy,
        int* litEntropyWritten,
        int* seqEntropyWritten,
        uint lastBlock
    )
    {
        var ostart = (byte*)dst;
        var oend = ostart + dstCapacity;
        var op = ostart + ZstdBlockHeaderSize;
        {
            var cLitSize = ZSTD_compressSubBlock_literal(
                &entropy->huf.CTable.e0,
                &entropyMetadata->hufMetadata,
                literals,
                litSize,
                op,
                (nuint)(oend - op),
                bmi2,
                writeLitEntropy,
                litEntropyWritten
            );
            {
                var errCode = cLitSize;
                if (ERR_isError(errCode))
                    return errCode;
            }

            if (cLitSize == 0)
                return 0;
            op += cLitSize;
        }

        {
            var cSeqSize = ZSTD_compressSubBlock_sequences(
                &entropy->fse,
                &entropyMetadata->fseMetadata,
                sequences,
                nbSeq,
                llCode,
                mlCode,
                ofCode,
                cctxParams,
                op,
                (nuint)(oend - op),
                bmi2,
                writeSeqEntropy,
                seqEntropyWritten
            );
            {
                var errCode = cSeqSize;
                if (ERR_isError(errCode))
                    return errCode;
            }

            if (cSeqSize == 0)
                return 0;
            op += cSeqSize;
        }

        {
            var cSize = (nuint)(op - ostart) - ZstdBlockHeaderSize;
            var cBlockHeader24 =
                lastBlock + ((uint)BlockTypeE.BtCompressed << 1) + (uint)(cSize << 3);
            MEM_writeLE24(ostart, cBlockHeader24);
        }

        return (nuint)(op - ostart);
    }

    private static nuint ZSTD_estimateSubBlockSize_literal(
        byte* literals,
        nuint litSize,
        ZstdHufCTablesT* huf,
        ZstdHufCTablesMetadataT* hufMetadata,
        void* workspace,
        nuint wkspSize,
        int writeEntropy
    )
    {
        var countWksp = (uint*)workspace;
        uint maxSymbolValue = 255;
        /* Use hard coded size of 3 bytes */
        nuint literalSectionHeaderSize = 3;
        if (hufMetadata->hType == SymbolEncodingTypeE.SetBasic)
            return litSize;
        if (hufMetadata->hType == SymbolEncodingTypeE.SetRle)
            return 1;
        if (
            hufMetadata->hType == SymbolEncodingTypeE.SetCompressed
            || hufMetadata->hType == SymbolEncodingTypeE.SetRepeat
        )
        {
            var largest = HIST_count_wksp(
                countWksp,
                &maxSymbolValue,
                literals,
                litSize,
                workspace,
                wkspSize
            );
            if (ERR_isError(largest))
                return litSize;
            {
                var cLitSizeEstimate = HUF_estimateCompressedSize(
                    &huf->CTable.e0,
                    countWksp,
                    maxSymbolValue
                );
                if (writeEntropy != 0)
                    cLitSizeEstimate += hufMetadata->hufDesSize;
                return cLitSizeEstimate + literalSectionHeaderSize;
            }
        }

        assert(0 != 0);
        return 0;
    }

    private static nuint ZSTD_estimateSubBlockSize_symbolType(
        SymbolEncodingTypeE type,
        byte* codeTable,
        uint maxCode,
        nuint nbSeq,
        uint* fseCTable,
        byte* additionalBits,
        short* defaultNorm,
        uint defaultNormLog,
        uint defaultMax,
        void* workspace,
        nuint wkspSize
    )
    {
        var countWksp = (uint*)workspace;
        var ctp = codeTable;
        var ctStart = ctp;
        var ctEnd = ctStart + nbSeq;
        nuint cSymbolTypeSizeEstimateInBits = 0;
        var max = maxCode;
        HIST_countFast_wksp(countWksp, &max, codeTable, nbSeq, workspace, wkspSize);
        if (type == SymbolEncodingTypeE.SetBasic)
        {
            assert(max <= defaultMax);
            cSymbolTypeSizeEstimateInBits =
                max <= defaultMax
                    ? ZSTD_crossEntropyCost(defaultNorm, defaultNormLog, countWksp, max)
                    : unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorGeneric));
        }
        else if (type == SymbolEncodingTypeE.SetRle)
        {
            cSymbolTypeSizeEstimateInBits = 0;
        }
        else if (
            type == SymbolEncodingTypeE.SetCompressed
            || type == SymbolEncodingTypeE.SetRepeat
        )
        {
            cSymbolTypeSizeEstimateInBits = ZSTD_fseBitCost(fseCTable, countWksp, max);
        }

        if (ERR_isError(cSymbolTypeSizeEstimateInBits))
            return nbSeq * 10;
        while (ctp < ctEnd)
        {
            if (additionalBits != null)
                cSymbolTypeSizeEstimateInBits += additionalBits[*ctp];
            else
                cSymbolTypeSizeEstimateInBits += *ctp;
            ctp++;
        }

        return cSymbolTypeSizeEstimateInBits / 8;
    }

    private static nuint ZSTD_estimateSubBlockSize_sequences(
        byte* ofCodeTable,
        byte* llCodeTable,
        byte* mlCodeTable,
        nuint nbSeq,
        ZstdFseCTablesT* fseTables,
        ZstdFseCTablesMetadataT* fseMetadata,
        void* workspace,
        nuint wkspSize,
        int writeEntropy
    )
    {
        /* Use hard coded size of 3 bytes */
        const nuint sequencesSectionHeaderSize = 3;
        nuint cSeqSizeEstimate = 0;
        if (nbSeq == 0)
            return sequencesSectionHeaderSize;
        cSeqSizeEstimate += ZSTD_estimateSubBlockSize_symbolType(
            fseMetadata->ofType,
            ofCodeTable,
            31,
            nbSeq,
            fseTables->offcodeCTable,
            null,
            OfDefaultNorm,
            OfDefaultNormLog,
            28,
            workspace,
            wkspSize
        );
        cSeqSizeEstimate += ZSTD_estimateSubBlockSize_symbolType(
            fseMetadata->llType,
            llCodeTable,
            35,
            nbSeq,
            fseTables->litlengthCTable,
            LlBits,
            LlDefaultNorm,
            LlDefaultNormLog,
            35,
            workspace,
            wkspSize
        );
        cSeqSizeEstimate += ZSTD_estimateSubBlockSize_symbolType(
            fseMetadata->mlType,
            mlCodeTable,
            52,
            nbSeq,
            fseTables->matchlengthCTable,
            MlBits,
            MlDefaultNorm,
            MlDefaultNormLog,
            52,
            workspace,
            wkspSize
        );
        if (writeEntropy != 0)
            cSeqSizeEstimate += fseMetadata->fseTablesSize;
        return cSeqSizeEstimate + sequencesSectionHeaderSize;
    }

    private static nuint ZSTD_estimateSubBlockSize(
        byte* literals,
        nuint litSize,
        byte* ofCodeTable,
        byte* llCodeTable,
        byte* mlCodeTable,
        nuint nbSeq,
        ZstdEntropyCTablesT* entropy,
        ZstdEntropyCTablesMetadataT* entropyMetadata,
        void* workspace,
        nuint wkspSize,
        int writeLitEntropy,
        int writeSeqEntropy
    )
    {
        nuint cSizeEstimate = 0;
        cSizeEstimate += ZSTD_estimateSubBlockSize_literal(
            literals,
            litSize,
            &entropy->huf,
            &entropyMetadata->hufMetadata,
            workspace,
            wkspSize,
            writeLitEntropy
        );
        cSizeEstimate += ZSTD_estimateSubBlockSize_sequences(
            ofCodeTable,
            llCodeTable,
            mlCodeTable,
            nbSeq,
            &entropy->fse,
            &entropyMetadata->fseMetadata,
            workspace,
            wkspSize,
            writeSeqEntropy
        );
        return cSizeEstimate + ZstdBlockHeaderSize;
    }

    private static int ZSTD_needSequenceEntropyTables(ZstdFseCTablesMetadataT* fseMetadata)
    {
        if (
            fseMetadata->llType == SymbolEncodingTypeE.SetCompressed
            || fseMetadata->llType == SymbolEncodingTypeE.SetRle
        )
            return 1;

        if (
            fseMetadata->mlType == SymbolEncodingTypeE.SetCompressed
            || fseMetadata->mlType == SymbolEncodingTypeE.SetRle
        )
            return 1;

        if (
            fseMetadata->ofType == SymbolEncodingTypeE.SetCompressed
            || fseMetadata->ofType == SymbolEncodingTypeE.SetRle
        )
            return 1;

        return 0;
    }

    /*
     * ZSTD_compressSubBlock_multi() :
     * Breaks super-block into multiple sub-blocks and compresses them.
     * Entropy will be written to the first block.
     * The following blocks will use repeat mode to compress.
     * All sub-blocks are compressed blocks (no raw or rle blocks).
     * @return : compressed size of the super block (which is multiple ZSTD blocks)
     * Or 0 if it failed to compress.
     */
    private static nuint ZSTD_compressSubBlock_multi(
        SeqStoreT* seqStorePtr,
        ZstdCompressedBlockStateT* prevCBlock,
        ZstdCompressedBlockStateT* nextCBlock,
        ZstdEntropyCTablesMetadataT* entropyMetadata,
        ZstdCCtxParamsS* cctxParams,
        void* dst,
        nuint dstCapacity,
        void* src,
        nuint srcSize,
        int bmi2,
        uint lastBlock,
        void* workspace,
        nuint wkspSize
    )
    {
        var sstart = seqStorePtr->sequencesStart;
        var send = seqStorePtr->sequences;
        var sp = sstart;
        var lstart = seqStorePtr->litStart;
        var lend = seqStorePtr->lit;
        var lp = lstart;
        var ip = (byte*)src;
        var iend = ip + srcSize;
        var ostart = (byte*)dst;
        var oend = ostart + dstCapacity;
        var op = ostart;
        var llCodePtr = seqStorePtr->llCode;
        var mlCodePtr = seqStorePtr->mlCode;
        var ofCodePtr = seqStorePtr->ofCode;
        var targetCBlockSize = cctxParams->targetCBlockSize;
        nuint litSize,
            seqCount;
        var writeLitEntropy =
            entropyMetadata->hufMetadata.hType == SymbolEncodingTypeE.SetCompressed ? 1 : 0;
        var writeSeqEntropy = 1;
        var lastSequence = 0;
        litSize = 0;
        seqCount = 0;
        do
        {
            nuint cBlockSizeEstimate = 0;
            if (sstart == send)
            {
                lastSequence = 1;
            }
            else
            {
                var sequence = sp + seqCount;
                lastSequence = sequence == send - 1 ? 1 : 0;
                litSize += ZSTD_getSequenceLength(seqStorePtr, sequence).litLength;
                seqCount++;
            }

            if (lastSequence != 0)
            {
                assert(lp <= lend);
                assert(litSize <= (nuint)(lend - lp));
                litSize = (nuint)(lend - lp);
            }

            cBlockSizeEstimate = ZSTD_estimateSubBlockSize(
                lp,
                litSize,
                ofCodePtr,
                llCodePtr,
                mlCodePtr,
                seqCount,
                &nextCBlock->entropy,
                entropyMetadata,
                workspace,
                wkspSize,
                writeLitEntropy,
                writeSeqEntropy
            );
            if (cBlockSizeEstimate > targetCBlockSize || lastSequence != 0)
            {
                var litEntropyWritten = 0;
                var seqEntropyWritten = 0;
                var decompressedSize = ZSTD_seqDecompressedSize(
                    seqStorePtr,
                    sp,
                    seqCount,
                    litSize,
                    lastSequence
                );
                var cSize = ZSTD_compressSubBlock(
                    &nextCBlock->entropy,
                    entropyMetadata,
                    sp,
                    seqCount,
                    lp,
                    litSize,
                    llCodePtr,
                    mlCodePtr,
                    ofCodePtr,
                    cctxParams,
                    op,
                    (nuint)(oend - op),
                    bmi2,
                    writeLitEntropy,
                    writeSeqEntropy,
                    &litEntropyWritten,
                    &seqEntropyWritten,
                    lastBlock != 0 && lastSequence != 0 ? 1U : 0U
                );
                {
                    var errCode = cSize;
                    if (ERR_isError(errCode))
                        return errCode;
                }

                if (cSize > 0 && cSize < decompressedSize)
                {
                    assert(ip + decompressedSize <= iend);
                    ip += decompressedSize;
                    sp += seqCount;
                    lp += litSize;
                    op += cSize;
                    llCodePtr += seqCount;
                    mlCodePtr += seqCount;
                    ofCodePtr += seqCount;
                    litSize = 0;
                    seqCount = 0;
                    if (litEntropyWritten != 0)
                        writeLitEntropy = 0;

                    if (seqEntropyWritten != 0)
                        writeSeqEntropy = 0;
                }
            }
        } while (lastSequence == 0);

        if (writeLitEntropy != 0)
            memcpy(
                &nextCBlock->entropy.huf,
                &prevCBlock->entropy.huf,
                (uint)sizeof(ZstdHufCTablesT)
            );

        if (
            writeSeqEntropy != 0
            && ZSTD_needSequenceEntropyTables(&entropyMetadata->fseMetadata) != 0
        )
            return 0;

        if (ip < iend)
        {
            var cSize = ZSTD_noCompressBlock(
                op,
                (nuint)(oend - op),
                ip,
                (nuint)(iend - ip),
                lastBlock
            );
            {
                var errCode = cSize;
                if (ERR_isError(errCode))
                    return errCode;
            }

            assert(cSize != 0);
            op += cSize;
            if (sp < send)
            {
                SeqDefS* seq;
                RepcodesS rep;
                memcpy(&rep, prevCBlock->rep, (uint)sizeof(RepcodesS));
                for (seq = sstart; seq < sp; ++seq)
                    ZSTD_updateRep(
                        rep.rep,
                        seq->offBase,
                        ZSTD_getSequenceLength(seqStorePtr, seq).litLength == 0 ? 1U : 0U
                    );

                memcpy(nextCBlock->rep, &rep, (uint)sizeof(RepcodesS));
            }
        }

        return (nuint)(op - ostart);
    }

    /* ZSTD_compressSuperBlock() :
     * Used to compress a super block when targetCBlockSize is being used.
     * The given block will be compressed into multiple sub blocks that are around targetCBlockSize. */
    private static nuint ZSTD_compressSuperBlock(
        ZstdCCtxS* zc,
        void* dst,
        nuint dstCapacity,
        void* src,
        nuint srcSize,
        uint lastBlock
    )
    {
        ZstdEntropyCTablesMetadataT entropyMetadata;
        {
            var errCode = ZSTD_buildBlockEntropyStats(
                &zc->seqStore,
                &zc->blockState.prevCBlock->entropy,
                &zc->blockState.nextCBlock->entropy,
                &zc->appliedParams,
                &entropyMetadata,
                zc->entropyWorkspace,
                (8 << 10) + 512 + sizeof(uint) * ((35 > 52 ? 35 : 52) + 2)
            );
            if (ERR_isError(errCode))
                return errCode;
        }

        return ZSTD_compressSubBlock_multi(
            &zc->seqStore,
            zc->blockState.prevCBlock,
            zc->blockState.nextCBlock,
            &entropyMetadata,
            &zc->appliedParams,
            dst,
            dstCapacity,
            src,
            srcSize,
            zc->bmi2,
            lastBlock,
            zc->entropyWorkspace,
            (8 << 10) + 512 + sizeof(uint) * ((35 > 52 ? 35 : 52) + 2)
        );
    }
}