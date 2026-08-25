namespace VendoredZSTD.Unsafe;

public static partial class Methods
{
    private static readonly ZstdCompressionParameters[][] ZstdDefaultCParameters =
        new ZstdCompressionParameters[4][]
        {
            new ZstdCompressionParameters[23]
            {
                new(19, 12, 13, 1, 6, 1, ZstdStrategy.ZstdFast),
                new(19, 13, 14, 1, 7, 0, ZstdStrategy.ZstdFast),
                new(20, 15, 16, 1, 6, 0, ZstdStrategy.ZstdFast),
                new(21, 16, 17, 1, 5, 0, ZstdStrategy.ZstdDfast),
                new(21, 18, 18, 1, 5, 0, ZstdStrategy.ZstdDfast),
                new(21, 18, 19, 3, 5, 2, ZstdStrategy.ZstdGreedy),
                new(21, 18, 19, 3, 5, 4, ZstdStrategy.ZstdLazy),
                new(21, 19, 20, 4, 5, 8, ZstdStrategy.ZstdLazy),
                new(21, 19, 20, 4, 5, 16, ZstdStrategy.ZstdLazy2),
                new(22, 20, 21, 4, 5, 16, ZstdStrategy.ZstdLazy2),
                new(22, 21, 22, 5, 5, 16, ZstdStrategy.ZstdLazy2),
                new(22, 21, 22, 6, 5, 16, ZstdStrategy.ZstdLazy2),
                new(22, 22, 23, 6, 5, 32, ZstdStrategy.ZstdLazy2),
                new(22, 22, 22, 4, 5, 32, ZstdStrategy.ZstdBtlazy2),
                new(22, 22, 23, 5, 5, 32, ZstdStrategy.ZstdBtlazy2),
                new(22, 23, 23, 6, 5, 32, ZstdStrategy.ZstdBtlazy2),
                new(22, 22, 22, 5, 5, 48, ZstdStrategy.ZstdBtopt),
                new(23, 23, 22, 5, 4, 64, ZstdStrategy.ZstdBtopt),
                new(23, 23, 22, 6, 3, 64, ZstdStrategy.ZstdBtultra),
                new(23, 24, 22, 7, 3, 256, ZstdStrategy.ZstdBtultra2),
                new(25, 25, 23, 7, 3, 256, ZstdStrategy.ZstdBtultra2),
                new(26, 26, 24, 7, 3, 512, ZstdStrategy.ZstdBtultra2),
                new(27, 27, 25, 9, 3, 999, ZstdStrategy.ZstdBtultra2)
            },
            new ZstdCompressionParameters[23]
            {
                new(18, 12, 13, 1, 5, 1, ZstdStrategy.ZstdFast),
                new(18, 13, 14, 1, 6, 0, ZstdStrategy.ZstdFast),
                new(18, 14, 14, 1, 5, 0, ZstdStrategy.ZstdDfast),
                new(18, 16, 16, 1, 4, 0, ZstdStrategy.ZstdDfast),
                new(18, 16, 17, 3, 5, 2, ZstdStrategy.ZstdGreedy),
                new(18, 17, 18, 5, 5, 2, ZstdStrategy.ZstdGreedy),
                new(18, 18, 19, 3, 5, 4, ZstdStrategy.ZstdLazy),
                new(18, 18, 19, 4, 4, 4, ZstdStrategy.ZstdLazy),
                new(18, 18, 19, 4, 4, 8, ZstdStrategy.ZstdLazy2),
                new(18, 18, 19, 5, 4, 8, ZstdStrategy.ZstdLazy2),
                new(18, 18, 19, 6, 4, 8, ZstdStrategy.ZstdLazy2),
                new(18, 18, 19, 5, 4, 12, ZstdStrategy.ZstdBtlazy2),
                new(18, 19, 19, 7, 4, 12, ZstdStrategy.ZstdBtlazy2),
                new(18, 18, 19, 4, 4, 16, ZstdStrategy.ZstdBtopt),
                new(18, 18, 19, 4, 3, 32, ZstdStrategy.ZstdBtopt),
                new(18, 18, 19, 6, 3, 128, ZstdStrategy.ZstdBtopt),
                new(18, 19, 19, 6, 3, 128, ZstdStrategy.ZstdBtultra),
                new(18, 19, 19, 8, 3, 256, ZstdStrategy.ZstdBtultra),
                new(18, 19, 19, 6, 3, 128, ZstdStrategy.ZstdBtultra2),
                new(18, 19, 19, 8, 3, 256, ZstdStrategy.ZstdBtultra2),
                new(18, 19, 19, 10, 3, 512, ZstdStrategy.ZstdBtultra2),
                new(18, 19, 19, 12, 3, 512, ZstdStrategy.ZstdBtultra2),
                new(18, 19, 19, 13, 3, 999, ZstdStrategy.ZstdBtultra2)
            },
            new ZstdCompressionParameters[23]
            {
                new(17, 12, 12, 1, 5, 1, ZstdStrategy.ZstdFast),
                new(17, 12, 13, 1, 6, 0, ZstdStrategy.ZstdFast),
                new(17, 13, 15, 1, 5, 0, ZstdStrategy.ZstdFast),
                new(17, 15, 16, 2, 5, 0, ZstdStrategy.ZstdDfast),
                new(17, 17, 17, 2, 4, 0, ZstdStrategy.ZstdDfast),
                new(17, 16, 17, 3, 4, 2, ZstdStrategy.ZstdGreedy),
                new(17, 16, 17, 3, 4, 4, ZstdStrategy.ZstdLazy),
                new(17, 16, 17, 3, 4, 8, ZstdStrategy.ZstdLazy2),
                new(17, 16, 17, 4, 4, 8, ZstdStrategy.ZstdLazy2),
                new(17, 16, 17, 5, 4, 8, ZstdStrategy.ZstdLazy2),
                new(17, 16, 17, 6, 4, 8, ZstdStrategy.ZstdLazy2),
                new(17, 17, 17, 5, 4, 8, ZstdStrategy.ZstdBtlazy2),
                new(17, 18, 17, 7, 4, 12, ZstdStrategy.ZstdBtlazy2),
                new(17, 18, 17, 3, 4, 12, ZstdStrategy.ZstdBtopt),
                new(17, 18, 17, 4, 3, 32, ZstdStrategy.ZstdBtopt),
                new(17, 18, 17, 6, 3, 256, ZstdStrategy.ZstdBtopt),
                new(17, 18, 17, 6, 3, 128, ZstdStrategy.ZstdBtultra),
                new(17, 18, 17, 8, 3, 256, ZstdStrategy.ZstdBtultra),
                new(17, 18, 17, 10, 3, 512, ZstdStrategy.ZstdBtultra),
                new(17, 18, 17, 5, 3, 256, ZstdStrategy.ZstdBtultra2),
                new(17, 18, 17, 7, 3, 512, ZstdStrategy.ZstdBtultra2),
                new(17, 18, 17, 9, 3, 512, ZstdStrategy.ZstdBtultra2),
                new(17, 18, 17, 11, 3, 999, ZstdStrategy.ZstdBtultra2)
            },
            new ZstdCompressionParameters[23]
            {
                new(14, 12, 13, 1, 5, 1, ZstdStrategy.ZstdFast),
                new(14, 14, 15, 1, 5, 0, ZstdStrategy.ZstdFast),
                new(14, 14, 15, 1, 4, 0, ZstdStrategy.ZstdFast),
                new(14, 14, 15, 2, 4, 0, ZstdStrategy.ZstdDfast),
                new(14, 14, 14, 4, 4, 2, ZstdStrategy.ZstdGreedy),
                new(14, 14, 14, 3, 4, 4, ZstdStrategy.ZstdLazy),
                new(14, 14, 14, 4, 4, 8, ZstdStrategy.ZstdLazy2),
                new(14, 14, 14, 6, 4, 8, ZstdStrategy.ZstdLazy2),
                new(14, 14, 14, 8, 4, 8, ZstdStrategy.ZstdLazy2),
                new(14, 15, 14, 5, 4, 8, ZstdStrategy.ZstdBtlazy2),
                new(14, 15, 14, 9, 4, 8, ZstdStrategy.ZstdBtlazy2),
                new(14, 15, 14, 3, 4, 12, ZstdStrategy.ZstdBtopt),
                new(14, 15, 14, 4, 3, 24, ZstdStrategy.ZstdBtopt),
                new(14, 15, 14, 5, 3, 32, ZstdStrategy.ZstdBtultra),
                new(14, 15, 15, 6, 3, 64, ZstdStrategy.ZstdBtultra),
                new(14, 15, 15, 7, 3, 256, ZstdStrategy.ZstdBtultra),
                new(14, 15, 15, 5, 3, 48, ZstdStrategy.ZstdBtultra2),
                new(14, 15, 15, 6, 3, 128, ZstdStrategy.ZstdBtultra2),
                new(14, 15, 15, 7, 3, 256, ZstdStrategy.ZstdBtultra2),
                new(14, 15, 15, 8, 3, 256, ZstdStrategy.ZstdBtultra2),
                new(14, 15, 15, 8, 3, 512, ZstdStrategy.ZstdBtultra2),
                new(14, 15, 15, 9, 3, 512, ZstdStrategy.ZstdBtultra2),
                new(14, 15, 15, 10, 3, 999, ZstdStrategy.ZstdBtultra2)
            }
        };
}