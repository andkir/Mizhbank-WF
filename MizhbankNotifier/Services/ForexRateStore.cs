namespace MizhbankNotifier.Services;

/// <summary>Separate DI-resolvable rate store for the Forex EUR/USD feed.</summary>
public class ForexRateStore : RateStore
{
    public ForexRateStore() : base("forex_previous.json") { }
}
