using System.Globalization;
using System.Text.Json;
using MizhbankNotifier.Models;

namespace MizhbankNotifier.Services;

/// <summary>
/// Parses the kurs.com.ua /ajax/organizations/cash response.
/// JSON: { "view": "&lt;tbody&gt;...&lt;/tbody&gt;" }
/// Row structure: td[0]=name, td[1]=icons(hidden), td[2]=time, td[3]=buy, td[4]=sell
/// Buy/Sell values are in data-sortValue attributes on td[3]/td[4].
/// </summary>
internal static class BankRateParser
{
    public static List<BankRate>? Parse(string json, ILogger logger)
    {
        try
        {
            string html = ExtractHtml(json, logger);
            return ParseHtml(html, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse bank rates");
            return null;
        }
    }

    private static string ExtractHtml(string json, ILogger logger)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("view", out var view))
                return view.GetString() ?? json;
        }
        catch
        {
            // Not JSON — treat as raw HTML
        }
        return json;
    }

    private static List<BankRate> ParseHtml(string html, ILogger logger)
    {
        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(html);

        var rates = new List<BankRate>();
        var rows = doc.DocumentNode.SelectNodes("//tr");

        if (rows is null || rows.Count == 0)
        {
            logger.LogWarning("BankRateParser: no <tr> rows found. Raw (first 400): {Html}",
                html.Length > 400 ? html[..400] : html);
            return rates;
        }

        foreach (var row in rows)
        {
            var cells = row.SelectNodes("td");
            if (cells is null || cells.Count < 5) continue;

            // td[0]: bank name — preferably from <a> tag
            var nameNode = cells[0].SelectSingleNode(".//a") ?? cells[0];
            var name = CleanText(nameNode.InnerText);
            if (string.IsNullOrWhiteSpace(name)) continue;

            // td[2]: updated time
            var updated = CleanText(cells[2].InnerText);

            // td[3]: buy — use data-sortValue for clean numeric string
            var buyStr  = cells[3].GetAttributeValue("data-sortValue", "")
                          .Trim().Replace(",", ".");
            // td[4]: sell
            var sellStr = cells[4].GetAttributeValue("data-sortValue", "")
                          .Trim().Replace(",", ".");

            // Fallback to .course div InnerText if attribute missing
            if (string.IsNullOrEmpty(buyStr))
                buyStr = ExtractCourseValue(cells[3]);
            if (string.IsNullOrEmpty(sellStr))
                sellStr = ExtractCourseValue(cells[4]);

            if (!TryParseDecimal(buyStr,  out var buy))  continue;
            if (!TryParseDecimal(sellStr, out var sell)) continue;

            bool optimal = row.GetAttributeValue("class", "")
                             .Contains("optimal", StringComparison.OrdinalIgnoreCase)
                         || row.GetAttributeValue("class", "")
                             .Contains("tr-green", StringComparison.OrdinalIgnoreCase);

            rates.Add(new BankRate(name, buy, sell, updated, optimal));
        }

        logger.LogInformation("BankRateParser: parsed {Count} bank rates", rates.Count);
        return rates;
    }

    /// <summary>Extract the first number from a .course div, ignoring the change span.</summary>
    private static string ExtractCourseValue(HtmlAgilityPack.HtmlNode cell)
    {
        var courseDiv = cell.SelectSingleNode(".//div[contains(@class,'course')]");
        if (courseDiv is null) return "";
        // Remove child nodes (the change span) and get just the first text node
        var textNode = courseDiv.ChildNodes
            .FirstOrDefault(n => n.NodeType == HtmlAgilityPack.HtmlNodeType.Text);
        return (textNode?.InnerText ?? "").Trim().Replace(",", ".");
    }

    private static string CleanText(string html)
    {
        var tmp = new HtmlAgilityPack.HtmlDocument();
        tmp.LoadHtml(html);
        return System.Net.WebUtility.HtmlDecode(tmp.DocumentNode.InnerText).Trim();
    }

    private static bool TryParseDecimal(string s, out decimal value)
    {
        s = s.Replace(" ", "").Replace(",", ".");
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value)
               && value > 0;
    }
}
