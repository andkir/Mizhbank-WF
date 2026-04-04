using MizhbankNotifier.Models;

namespace MizhbankNotifier.Services;

public class BankRateStore
{
    private volatile IReadOnlyList<BankRate> _usd     = [];
    private volatile IReadOnlyList<BankRate> _eur     = [];
    private volatile IReadOnlyList<BankRate> _prevUsd = [];
    private volatile IReadOnlyList<BankRate> _prevEur = [];

    public IReadOnlyList<BankRate> Usd         => _usd;
    public IReadOnlyList<BankRate> Eur         => _eur;
    public IReadOnlyList<BankRate> PreviousUsd => _prevUsd;
    public IReadOnlyList<BankRate> PreviousEur => _prevEur;

    /// <summary>UTC time of the last successful USD fetch. Null until first fetch.</summary>
    public DateTime? FetchedAtUsd { get; private set; }
    /// <summary>UTC time of the last successful EUR fetch. Null until first fetch.</summary>
    public DateTime? FetchedAtEur { get; private set; }

    public event Action? DataChanged;

    public BankRateStore()
    {
        _prevUsd = RatePersistence.Load<List<BankRate>>("bank_usd_previous.json")
                       ?.AsReadOnly() ?? (IReadOnlyList<BankRate>)[];
        _prevEur = RatePersistence.Load<List<BankRate>>("bank_eur_previous.json")
                       ?.AsReadOnly() ?? (IReadOnlyList<BankRate>)[];
    }

    /// <summary>
    /// Seeds previous-rate snapshots from yesterday's history so trend arrows
    /// appear on the first fetch without needing two poll cycles.
    /// Only fills gaps — does not overwrite data already loaded from disk.
    /// </summary>
    public void SeedPreviousFromHistory(List<BankRate>? usd, List<BankRate>? eur)
    {
        if (_prevUsd.Count == 0 && usd?.Count > 0)
            _prevUsd = usd.AsReadOnly();
        if (_prevEur.Count == 0 && eur?.Count > 0)
            _prevEur = eur.AsReadOnly();
    }

    public void UpdateUsd(List<BankRate> rates)
    {
        if (_usd.Count > 0)
        {
            _prevUsd = _usd;
            RatePersistence.Save("bank_usd_previous.json", _usd);
        }
        _usd         = rates.AsReadOnly();
        FetchedAtUsd = DateTime.UtcNow;
        DataChanged?.Invoke();
    }

    public void UpdateEur(List<BankRate> rates)
    {
        if (_eur.Count > 0)
        {
            _prevEur = _eur;
            RatePersistence.Save("bank_eur_previous.json", _eur);
        }
        _eur         = rates.AsReadOnly();
        FetchedAtEur = DateTime.UtcNow;
        DataChanged?.Invoke();
    }
}
