using MizhbankNotifier.Models;

namespace MizhbankNotifier.Services;

public class BankRateService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BankRateService> _logger;
    private bool _sessionReady;

    private const string PageUrl  = "https://kurs.com.ua/money.index";
    private const string AjaxBase =
        "https://kurs.com.ua/ajax/organizations/cash" +
        "?currency_to=undefined&organizations=MAJOR&show_optimal=1&current_page=money.index&currency_from=";

    public BankRateService(HttpClient httpClient, ILogger<BankRateService> logger)
    {
        _httpClient = httpClient;
        _logger     = logger;
    }

    /// <param name="currency">1 = USD, 2 = EUR</param>
    public async Task<List<BankRate>?> GetRatesAsync(int currency, CancellationToken ct = default)
    {
        try
        {
            if (!_sessionReady)
                await WarmSessionAsync(ct);

            var response = await SendAjaxAsync(currency, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // Session may have expired — re-warm and retry once
                _sessionReady = false;
                await WarmSessionAsync(ct);
                response = await SendAjaxAsync(currency, ct);
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            return BankRateParser.Parse(json, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch bank rates (currency={C})", currency);
            return null;
        }
    }

    private Task<HttpResponseMessage> SendAjaxAsync(int currency, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, AjaxBase + currency);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*; q=0.01");
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        request.Headers.TryAddWithoutValidation("Referer", PageUrl);
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
        return _httpClient.SendAsync(request, ct);
    }

    private async Task WarmSessionAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("BankRateService: warming session via {Url}", PageUrl);
            var req = new HttpRequestMessage(HttpMethod.Get, PageUrl);
            req.Headers.TryAddWithoutValidation("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            req.Headers.TryAddWithoutValidation("Referer", "https://kurs.com.ua/");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            req.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
            req.Headers.TryAddWithoutValidation("Cache-Control", "max-age=0");
            var resp = await _httpClient.SendAsync(req, ct);
            _logger.LogInformation("BankRateService: session warm status {Status}", resp.StatusCode);
            _sessionReady = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BankRateService: session warm failed");
        }
    }
}
