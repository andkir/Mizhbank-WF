namespace MizhbankNotifier.Services;

/// <summary>Separate DI-resolvable rate store for the EUR black market feed.</summary>
public class EurBlackMarketRateStore : RateStore
{
    public EurBlackMarketRateStore() : base("eur_blackmarket_previous.json") { }
}
