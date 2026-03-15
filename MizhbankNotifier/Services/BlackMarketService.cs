using System.Text.Json;
using MizhbankNotifier.Models;

namespace MizhbankNotifier.Services;

public class BlackMarketService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BlackMarketService> _logger;

    private const string ApiUrl =
        "https://kurs.com.ua/ajax/getChart?type=blackmarket&currency_from=USD&currency_to=&size=small";

    public BlackMarketService(HttpClient httpClient, ILogger<BlackMarketService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ChartData?> GetChartDataAsync(CancellationToken ct = default)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            request.Headers.Add("Referer", "https://kurs.com.ua/mezhbank");

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            return KursChartParser.Parse(json, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch black market rates");
            return null;
        }
    }
}
