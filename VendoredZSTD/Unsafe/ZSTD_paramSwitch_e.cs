namespace VendoredZSTD.Unsafe;

public enum ZstdParamSwitchE
{
    /* Let the library automatically determine whether the feature shall be enabled */
    ZstdPsAuto = 0,
    /* Force-enable the feature */
    ZstdPsEnable = 1,
    /* Do not use the feature */
    ZstdPsDisable = 2
}