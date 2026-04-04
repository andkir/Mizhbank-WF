namespace MizhbankNotifier.Services;

/// <summary>Separate DI-resolvable rate store for the black market feed.</summary>
public class BlackMarketRateStore : RateStore
{
    public BlackMarketRateStore() : base("blackmarket_previous.json") { }
}
