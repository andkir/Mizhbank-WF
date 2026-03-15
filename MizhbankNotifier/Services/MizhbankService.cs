using System.Text.Json;
using MizhbankNotifier.Models;

namespace MizhbankNotifier.Services;

public class MizhbankService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MizhbankService> _logger;

    private const string ApiUrl =
        "https://kurs.com.ua/ajax/getChart?type=interbank&currency_from=USD&currency_to=&size=big";

    public MizhbankService(HttpClient httpClient, ILogger<MizhbankService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<InterbankRate>?> GetAllRatesAsync(CancellationToken ct = default)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            request.Headers.Add("Referer", "https://kurs.com.ua/mezhbank");

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var viewJson = doc.RootElement.GetProperty("view").GetString();
            if (viewJson is null) return null;

            using var viewDoc = JsonDocument.Parse(viewJson);
            var series = viewDoc.RootElement.GetProperty("series");

            // series[2] = Продаж (sell) with actual data, series[3] = Купівля (buy)
            var sellSeries = series[2].GetProperty("data");
            var buySeries = series[3].GetProperty("data");

            var count = Math.Min(sellSeries.GetArrayLength(), buySeries.GetArrayLength());
            var rates = new List<InterbankRate>(count);

            for (int i = 0; i < count; i++)
            {
                var timestampMs = sellSeries[i][0].GetInt64();
                var time = DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).LocalDateTime;
                var sell = sellSeries[i][1].GetDecimal();
                var buy = buySeries[i][1].GetDecimal();
                rates.Add(new InterbankRate(time, buy, sell));
            }

            _logger.LogInformation("Fetched {Count} rate points", rates.Count);
            return rates;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch interbank rates");
            return null;
        }
    }
}
