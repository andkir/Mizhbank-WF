using Microsoft.Extensions.Options;
using MizhbankNotifier.Configuration;
using MizhbankNotifier.Models;

namespace MizhbankNotifier.Services;

public class MizhbankService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MizhbankService> _logger;
    private readonly ApiUrls _urls;

    public MizhbankService(HttpClient httpClient, IOptions<ApiUrls> urls, ILogger<MizhbankService> logger)
    {
        _httpClient = httpClient;
        _urls       = urls.Value;
        _logger     = logger;
    }

    public async Task<ChartData?> GetChartDataAsync(CancellationToken ct = default)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, _urls.KursInterbankUsd);
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            request.Headers.Add("Referer", _urls.KursChartReferer);

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            return KursChartParser.Parse(json, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch interbank rates");
            return null;
        }
    }
}
