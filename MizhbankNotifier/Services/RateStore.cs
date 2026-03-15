using MizhbankNotifier.Models;

namespace MizhbankNotifier.Services;

public class RateStore
{
    private volatile IReadOnlyList<InterbankRate> _rates = [];

    public IReadOnlyList<InterbankRate> Rates => _rates;
    public InterbankRate? Latest => _rates.Count > 0 ? _rates[^1] : null;

    public void Update(List<InterbankRate> rates) =>
        _rates = rates.AsReadOnly();
}
