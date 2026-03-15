using System.Text.Json;
using System.Text.Json.Serialization;
using MizhbankNotifier.Models;

namespace MizhbankNotifier.Services;

public class BankRateHistoryService
{
    private readonly ILogger<BankRateHistoryService> _logger;
    private readonly string _dataDir;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented          = true,
        PropertyNameCaseInsensitive = true,
    };

    public BankRateHistoryService(ILogger<BankRateHistoryService> logger)
    {
        _logger  = logger;
        _dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MizhbankNotifier", "rates");
        Directory.CreateDirectory(_dataDir);
    }

    private string FilePath(DateTime date) =>
        Path.Combine(_dataDir, date.ToString("dd-MM-yyyy") + ".banks.json");

    // ── Save ──────────────────────────────────────────────────────────────────

    public void Save(DateTime date, IReadOnlyList<BankRate> usd, IReadOnlyList<BankRate> eur)
    {
        try
        {
            var dto  = new BankRateFile(date.ToString("yyyy-MM-dd"), usd, eur);
            var json = JsonSerializer.Serialize(dto, JsonOpts);
            File.WriteAllText(FilePath(date), json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BankRateHistory: failed to save {Date}", date.ToString("dd-MM-yyyy"));
        }
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    /// <summary>Load today's file. Returns (null,null) if no file exists yet.</summary>
    public (List<BankRate>? Usd, List<BankRate>? Eur) LoadToday() =>
        Load(DateTime.Today);

    public (List<BankRate>? Usd, List<BankRate>? Eur) Load(DateTime date)
    {
        var path = FilePath(date);
        if (!File.Exists(path)) return (null, null);
        try
        {
            var json = File.ReadAllText(path);
            var dto  = JsonSerializer.Deserialize<BankRateFile>(json, JsonOpts);
            if (dto is null) return (null, null);
            _logger.LogInformation("BankRateHistory: loaded {U} USD + {E} EUR from {F}",
                dto.Usd?.Count, dto.Eur?.Count, Path.GetFileName(path));
            return (dto.Usd?.ToList(), dto.Eur?.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BankRateHistory: failed to load {File}", path);
            return (null, null);
        }
    }

    // ── DTO ───────────────────────────────────────────────────────────────────

    private record BankRateFile(
        string Date,
        IReadOnlyList<BankRate> Usd,
        IReadOnlyList<BankRate> Eur);
}
