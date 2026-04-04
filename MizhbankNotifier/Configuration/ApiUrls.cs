namespace MizhbankNotifier.Configuration;

/// <summary>
/// Strongly-typed options bound from the "ApiUrls" section of appsettings.json.
/// Holds every external endpoint the app talks to.
/// </summary>
public class ApiUrls
{
    public const string SectionName = "ApiUrls";

    // kurs.com.ua chart endpoints
    public string KursInterbankUsd   { get; set; } = "";
    public string KursInterbankEur   { get; set; } = "";
    public string KursBlackMarketUsd { get; set; } = "";
    public string KursBlackMarketEur { get; set; } = "";
    public string KursForexEurUsd    { get; set; } = "";

    // kurs.com.ua bank-cash AJAX — template with {0} = currency id (1=USD, 2=EUR)
    public string KursBankRatesAjax       { get; set; } = "";
    public string KursBankRatesAjaxLegacy { get; set; } = "";

    // kurs.com.ua HTML pages (used for referer / session warm-up)
    public string KursHome            { get; set; } = "";
    public string KursBankRatesPage   { get; set; } = "";
    public string KursChartReferer    { get; set; } = "";

    // finance.ua daily interbank JSON
    public string FinanceUaDaily { get; set; } = "";
}
