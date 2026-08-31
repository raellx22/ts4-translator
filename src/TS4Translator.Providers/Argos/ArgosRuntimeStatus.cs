namespace TS4Translator.Providers.Argos;

public sealed record ArgosRuntimeStatus(bool RuntimeInstalled, bool EnglishToBrazilianPortugueseInstalled, string Message)
{
    public bool IsReady => RuntimeInstalled && EnglishToBrazilianPortugueseInstalled;
}
