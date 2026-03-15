using MizhbankNotifier.Models;

namespace MizhbankNotifier.Services;

public class BankRateStore
{
    private volatile IReadOnlyList<BankRate> _usd = [];
    private volatile IReadOnlyList<BankRate> _eur = [];

    public IReadOnlyList<BankRate> Usd => _usd;
    public IReadOnlyList<BankRate> Eur => _eur;

    public event Action? DataChanged;

    public void UpdateUsd(List<BankRate> rates) { _usd = rates.AsReadOnly(); DataChanged?.Invoke(); }
    public void UpdateEur(List<BankRate> rates) { _eur = rates.AsReadOnly(); DataChanged?.Invoke(); }
}
