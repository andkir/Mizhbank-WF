using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MizhbankNotifier.Configuration;
using MizhbankNotifier.Models;

namespace MizhbankNotifier.Services;

public class FinanceUaService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FinanceUaService> _logger;
    private readonly ApiUrls _urls;

    public FinanceUaService(HttpClient httpClient, IOptions<ApiUrls> urls, ILogger<FinanceUaService> logger)
    {
        _httpClient = httpClient;
        _urls       = urls.Value;
        _logger     = logger;
    }

    /// <summary>
    /// Fetches today's intraday interbank USD/UAH data from finance.ua.
    /// Returns a ChartData with CurrentSession only (no previous session from this source).
    /// </summary>
    public async Task<ChartData?> GetChartDataAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _httpClient.GetStringAsync(_urls.FinanceUaDaily, ct);
            return Parse(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Finance.ua interbank rates");
            return null;
        }
    }

    /// <summary>
    /// Parses the finance.ua daily JSON: array of ["MM/dd/yyyy HH:mm", "buy", "sell"].
    /// </summary>
    private ChartData? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var rates = new List<InterbankRate>();

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                var timeStr = entry[0].GetString();
                var buyStr  = entry[1].GetString();
                var sellStr = entry[2].GetString();

                if (timeStr is null || buyStr is null || sellStr is null)
                    continue;

                if (!DateTime.TryParseExact(timeStr, "MM/dd/yyyy HH:mm",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                    continue;

                if (!decimal.TryParse(buyStr, CultureInfo.InvariantCulture, out var buy))
                    continue;
                if (!decimal.TryParse(sellStr, CultureInfo.InvariantCulture, out var sell))
                    continue;

                var tsMs = new DateTimeOffset(time, TimeSpan.FromHours(3)).ToUnixTimeMilliseconds();
                rates.Add(new InterbankRate(time, buy, sell, tsMs));
            }

            if (rates.Count == 0)
                return null;

            _logger.LogInformation("Finance.ua: parsed {Count} interbank points", rates.Count);
            return new ChartData(rates, []);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Finance.ua JSON");
            return null;
        }
    }
}
