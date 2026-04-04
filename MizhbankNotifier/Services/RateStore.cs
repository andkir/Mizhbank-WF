using MizhbankNotifier.Models;

namespace MizhbankNotifier.Services;

public class RateStore
{
    private volatile ChartData _data = ChartData.Empty;
    private readonly string _cacheFile;

    public ChartData      Data           => _data;
    public InterbankRate? Latest         => _data.LatestSession.Count > 0 ? _data.LatestSession[^1] : null;
    public InterbankRate? PreviousLatest { get; private set; }

    public event Action? DataChanged;

    public RateStore(string cacheFile = "interbank_previous.json")
    {
        _cacheFile     = cacheFile;
        PreviousLatest = RatePersistence.Load<InterbankRate>(_cacheFile);
    }

    public void Update(ChartData data)
    {
        var current = Latest;
        if (current is not null)
        {
            PreviousLatest = current;
            RatePersistence.Save(_cacheFile, current);
        }
        _data = data;
        DataChanged?.Invoke();
    }
}
