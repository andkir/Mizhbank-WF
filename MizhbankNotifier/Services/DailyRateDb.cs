using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using MizhbankNotifier.Models;

namespace MizhbankNotifier.Services;

/// <summary>
/// SQLite store for rate history.
/// DB file: Data/rates_history.db (next to the executable).
///
/// Tables:
///   daily_rates (date TEXT, type TEXT, buy REAL, sell REAL, PK (date, type))
///       — end-of-day closing snapshots (INSERT OR IGNORE)
///   day_charts  (date TEXT, type TEXT, rates_json TEXT, PK (date, type))
///       — full intraday series per day (INSERT OR REPLACE, updated every poll)
///
/// Types: interbank_usd | interbank_eur | blackmarket_usd | blackmarket_eur
/// </summary>
public sealed class DailyRateDb : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly object           _lock = new();

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };

    public DailyRateDb(IConfiguration configuration)
    {
        Directory.CreateDirectory(RatePersistence.DataDir);

        var raw = configuration.GetConnectionString("HistoryDb")
                  ?? throw new InvalidOperationException(
                      "Missing ConnectionStrings:HistoryDb in configuration.");

        // If "Data Source" is a bare filename, resolve it relative to the app's
        // data directory so the DB lands next to the JSON snapshots regardless
        // of the working directory the process was launched from.
        var builder = new SqliteConnectionStringBuilder(raw);
        if (!Path.IsPathRooted(builder.DataSource))
            builder.DataSource = Path.Combine(RatePersistence.DataDir, builder.DataSource);

        _conn = new SqliteConnection(builder.ToString());
        _conn.Open();
        lock (_lock) EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS daily_rates (
                date TEXT NOT NULL,
                type TEXT NOT NULL,
                buy  REAL NOT NULL,
                sell REAL NOT NULL,
                PRIMARY KEY (date, type)
            );
            CREATE TABLE IF NOT EXISTS day_charts (
                date        TEXT NOT NULL,
                type        TEXT NOT NULL,
                rates_json  TEXT NOT NULL,
                PRIMARY KEY (date, type)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    // ── Closing snapshots ──────────────────────────────────────────────────────

    /// <summary>Inserts a closing snapshot; no-op if a record for that date+type exists.</summary>
    public bool TryInsert(DateOnly date, string type, decimal buy, decimal sell)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                "INSERT OR IGNORE INTO daily_rates (date, type, buy, sell) " +
                "VALUES ($d, $t, $b, $s)";
            cmd.Parameters.AddWithValue("$d", date.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$t", type);
            cmd.Parameters.AddWithValue("$b", (double)buy);
            cmd.Parameters.AddWithValue("$s", (double)sell);
            return cmd.ExecuteNonQuery() == 1;
        }
    }

    // ── Full series (per-day chart) ────────────────────────────────────────────

    /// <summary>Replaces (upserts) today's intraday series for a given type.</summary>
    public void UpsertDayChart(DateOnly date, string type, IReadOnlyList<InterbankRate> rates)
    {
        var json = JsonSerializer.Serialize(rates, _json);
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                "INSERT OR REPLACE INTO day_charts (date, type, rates_json) " +
                "VALUES ($d, $t, $j)";
            cmd.Parameters.AddWithValue("$d", date.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$t", type);
            cmd.Parameters.AddWithValue("$j", json);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Loads the intraday series for a date+type. Returns null if not found.</summary>
    public List<InterbankRate>? LoadDayChart(DateOnly date, string type)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                "SELECT rates_json FROM day_charts WHERE date = $d AND type = $t";
            cmd.Parameters.AddWithValue("$d", date.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$t", type);
            var json = cmd.ExecuteScalar() as string;
            if (json is null) return null;
            return JsonSerializer.Deserialize<List<InterbankRate>>(json, _json);
        }
    }

    /// <summary>Returns all dates that have series data for the given type, ascending.</summary>
    public List<DateOnly> GetAvailableDates(string type)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                "SELECT DISTINCT date FROM day_charts WHERE type = $t ORDER BY date ASC";
            cmd.Parameters.AddWithValue("$t", type);
            using var reader = cmd.ExecuteReader();
            var result = new List<DateOnly>();
            while (reader.Read())
                result.Add(DateOnly.Parse(reader.GetString(0)));
            return result;
        }
    }

    public void Dispose() => _conn.Dispose();
}
