using Dapper;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace ValutaBot.MiniApp;

/// <summary>
/// Embedded SQLite Database Manager using Dapper ORM.
/// Replaces legacy un-safe JSON file writes with ACID-compliant SQLite transaction safety.
/// Automatically migrates existing legacy JSON databases into SQLite.
/// </summary>
public static class BotDatabase
{
    private static readonly string DbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bot_data.db");
    private static readonly string ConnectionString = $"Data Source={DbPath}";

    public static SqliteConnection GetConnection() => new SqliteConnection(ConnectionString);

    public static void Initialize()
    {
        using var conn = GetConnection();
        conn.Open();

        // ─── 1. Create Tables ───
        conn.Execute(@"
            CREATE TABLE IF NOT EXISTS allowed_users (
                chat_id INTEGER PRIMARY KEY,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS admins (
                chat_id INTEGER PRIMARY KEY
            );

            CREATE TABLE IF NOT EXISTS all_users (
                chat_id INTEGER PRIMARY KEY,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS registrations (
                chat_id INTEGER PRIMARY KEY,
                pocket_id TEXT NOT NULL,
                has_registered INTEGER NOT NULL DEFAULT 0,
                has_deposited INTEGER NOT NULL DEFAULT 0,
                deposit_amount REAL NOT NULL DEFAULT 0.0
            );

            CREATE TABLE IF NOT EXISTS trade_outcomes (
                id TEXT PRIMARY KEY,
                asset TEXT NOT NULL,
                timeframe TEXT NOT NULL,
                direction TEXT NOT NULL,
                entry_price REAL NOT NULL,
                exit_price REAL NOT NULL,
                pnl_bps REAL NOT NULL,
                was_win INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                verified_at TEXT NOT NULL
            );
        ");

        BotLogger.Info("[SQLite DB] Database tables initialized successfully.");

        // Initialize Trade Outcome Online Learning Engine
        TradeOutcomeTracker.Initialize();

        // ─── 2. Auto-Migrate Legacy JSON Files if Present ───
        MigrateLegacyJsonFiles(conn);
    }

    private static void MigrateLegacyJsonFiles(SqliteConnection conn)
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string allowedJson = Path.Combine(baseDir, "allowed_users.json");
            string adminsJson = Path.Combine(baseDir, "admins.json");
            string allUsersJson = Path.Combine(baseDir, "all_users.json");
            string regsJson = Path.Combine(baseDir, "registrations.json");

            // Migrate Allowed Users
            if (File.Exists(allowedJson))
            {
                var text = File.ReadAllText(allowedJson);
                var list = JsonSerializer.Deserialize<List<long>>(text);
                if (list != null)
                {
                    foreach (var id in list)
                        conn.Execute("INSERT OR IGNORE INTO allowed_users (chat_id, created_at) VALUES (@id, @now)", new { id, now = DateTime.UtcNow.ToString("o") });
                }
            }

            // Migrate Admins
            if (File.Exists(adminsJson))
            {
                var text = File.ReadAllText(adminsJson);
                var list = JsonSerializer.Deserialize<List<long>>(text);
                if (list != null)
                {
                    foreach (var id in list)
                        conn.Execute("INSERT OR IGNORE INTO admins (chat_id) VALUES (@id)", new { id });
                }
            }

            // Migrate All Users
            if (File.Exists(allUsersJson))
            {
                var text = File.ReadAllText(allUsersJson);
                var list = JsonSerializer.Deserialize<List<long>>(text);
                if (list != null)
                {
                    foreach (var id in list)
                        conn.Execute("INSERT OR IGNORE INTO all_users (chat_id, created_at) VALUES (@id, @now)", new { id, now = DateTime.UtcNow.ToString("o") });
                }
            }
        }
        catch (Exception ex)
        {
            BotLogger.Warn($"[SQLite DB] Legacy JSON migration notice: {ex.Message}");
        }
    }

    // ─── Public CRUD API ───

    public static HashSet<long> LoadAllowedUsers()
    {
        using var conn = GetConnection();
        return new HashSet<long>(conn.Query<long>("SELECT chat_id FROM allowed_users"));
    }

    public static HashSet<long> LoadAdmins()
    {
        using var conn = GetConnection();
        return new HashSet<long>(conn.Query<long>("SELECT chat_id FROM admins"));
    }

    public static HashSet<long> LoadAllUsers()
    {
        using var conn = GetConnection();
        return new HashSet<long>(conn.Query<long>("SELECT chat_id FROM all_users"));
    }

    public static void AddAllowedUser(long chatId)
    {
        using var conn = GetConnection();
        conn.Execute("INSERT OR IGNORE INTO allowed_users (chat_id, created_at) VALUES (@chatId, @now)", new { chatId, now = DateTime.UtcNow.ToString("o") });
    }

    public static void AddAdmin(long chatId)
    {
        using var conn = GetConnection();
        conn.Execute("INSERT OR IGNORE INTO admins (chat_id) VALUES (@chatId)", new { chatId });
        conn.Execute("INSERT OR IGNORE INTO allowed_users (chat_id, created_at) VALUES (@chatId, @now)", new { chatId, now = DateTime.UtcNow.ToString("o") });
    }

    public static void RemoveAdmin(long chatId)
    {
        using var conn = GetConnection();
        conn.Execute("DELETE FROM admins WHERE chat_id = @chatId", new { chatId });
    }

    public static void SaveTradeOutcome(TradeOutcomeRecord outcome)
    {
        try
        {
            using var conn = GetConnection();
            conn.Execute(@"
                INSERT OR REPLACE INTO trade_outcomes 
                (id, asset, timeframe, direction, entry_price, exit_price, pnl_bps, was_win, created_at, verified_at)
                VALUES (@Id, @Asset, @Timeframe, @Direction, @EntryPrice, @ExitPrice, @PnlBps, @WasWinInt, @CreatedAt, @VerifiedAt)
            ", new
            {
                outcome.Id,
                outcome.Asset,
                outcome.Timeframe,
                outcome.Direction,
                outcome.EntryPrice,
                outcome.ExitPrice,
                outcome.PnlBps,
                WasWinInt = outcome.WasWin ? 1 : 0,
                outcome.CreatedAt,
                outcome.VerifiedAt
            });
        }
        catch (Exception ex)
        {
            BotLogger.Error("[SQLite DB] Failed to save trade outcome record", ex);
        }
    }

    public static List<TradeOutcomeRecord> LoadTradeOutcomes(int limit = 1000)
    {
        try
        {
            using var conn = GetConnection();
            var rows = conn.Query(@"
                SELECT id as Id, asset as Asset, timeframe as Timeframe, direction as Direction,
                       entry_price as EntryPrice, exit_price as ExitPrice, pnl_bps as PnlBps,
                       was_win as WasWin, created_at as CreatedAt, verified_at as VerifiedAt
                FROM trade_outcomes
                ORDER BY verified_at DESC
                LIMIT @limit
            ", new { limit });

            return rows.Select(r => new TradeOutcomeRecord
            {
                Id = r.Id,
                Asset = r.Asset,
                Timeframe = r.Timeframe,
                Direction = r.Direction,
                EntryPrice = (double)r.EntryPrice,
                ExitPrice = (double)r.ExitPrice,
                PnlBps = (double)r.PnlBps,
                WasWin = Convert.ToInt64(r.WasWin) == 1,
                CreatedAt = r.CreatedAt ?? "",
                VerifiedAt = r.VerifiedAt ?? ""
            }).ToList();
        }
        catch (Exception ex)
        {
            BotLogger.Error("[SQLite DB] Failed to load trade outcomes", ex);
            return new List<TradeOutcomeRecord>();
        }
    }

    public static void RemoveAllowedUser(long chatId)
    {
        using var conn = GetConnection();
        conn.Execute("DELETE FROM allowed_users WHERE chat_id = @chatId", new { chatId });
    }

    public static void AddAllUser(long chatId)
    {
        using var conn = GetConnection();
        conn.Execute("INSERT OR IGNORE INTO all_users (chat_id, created_at) VALUES (@chatId, @now)", new { chatId, now = DateTime.UtcNow.ToString("o") });
    }
}

public class TradeOutcomeRecord
{
    public string Id { get; set; } = "";
    public string Asset { get; set; } = "";
    public string Timeframe { get; set; } = "";
    public string Direction { get; set; } = "";
    public double EntryPrice { get; set; }
    public double ExitPrice { get; set; }
    public double PnlBps { get; set; }
    public bool WasWin { get; set; }
    public string CreatedAt { get; set; } = "";
    public string VerifiedAt { get; set; } = "";
}
