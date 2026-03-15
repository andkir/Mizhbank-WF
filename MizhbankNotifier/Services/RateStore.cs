using MizhbankNotifier.Models;

namespace MizhbankNotifier.Services;

public class RateStore
{
    private volatile ChartData _data = ChartData.Empty;

    public ChartData Data   => _data;
    public InterbankRate? Latest =>
        _data.LatestSession.Count > 0 ? _data.LatestSession[^1] : null;

    public void Update(ChartData data) => _data = data;
}
