namespace VendoredZSTD.Unsafe;

/* Generate hash chain search fns for each combination of (dictMode, mls) */
public enum SearchMethodE
{
    SearchHashChain = 0,
    SearchBinaryTree = 1,
    SearchRowHash = 2
}