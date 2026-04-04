namespace MizhbankNotifier.Services;

/// <summary>Separate DI-resolvable rate store for the EUR interbank feed.</summary>
public class EurRateStore : RateStore
{
    public EurRateStore() : base("eur_interbank_previous.json") { }
}
