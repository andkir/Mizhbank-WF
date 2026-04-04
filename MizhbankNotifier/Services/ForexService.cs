using Microsoft.Extensions.Options;
using MizhbankNotifier.Configuration;
using MizhbankNotifier.Models;

namespace MizhbankNotifier.Services;

public class ForexService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ForexService> _logger;
    private readonly ApiUrls _urls;

    public ForexService(HttpClient httpClient, IOptions<ApiUrls> urls, ILogger<ForexService> logger)
    {
        _httpClient = httpClient;
        _urls       = urls.Value;
        _logger     = logger;
    }

    public async Task<ChartData?> GetChartDataAsync(CancellationToken ct = default)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, _urls.KursForexEurUsd);
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            request.Headers.Add("Referer", _urls.KursChartReferer);

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            return KursChartParser.Parse(json, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch forex EUR/USD rates");
            return null;
        }
    }
}
