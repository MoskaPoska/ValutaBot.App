using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Npgsql;

namespace ValutaBot.MiniApp;

public static class BotDatabase
{
    public static string GetConnectionString()
    {
        return Environment.GetEnvironmentVariable("DATABASE_URL") ?? "";
    }

    public static NpgsqlConnection GetConnection() => new NpgsqlConnection(GetConnectionString());

    public static async Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return;

        using var conn = GetConnection();
        await conn.OpenAsync();
        
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS allowed_users (
                chat_id BIGINT PRIMARY KEY,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS admins (
                chat_id BIGINT PRIMARY KEY
            );

            CREATE TABLE IF NOT EXISTS all_users (
                chat_id BIGINT PRIMARY KEY,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS registrations (
                pocket_id TEXT PRIMARY KEY,
                chat_id BIGINT NOT NULL,
                has_registered BOOLEAN NOT NULL,
                has_deposited BOOLEAN NOT NULL,
                deposit_amount DOUBLE PRECISION NOT NULL
            );

            CREATE TABLE IF NOT EXISTS trade_outcomes (
                id TEXT PRIMARY KEY,
                asset TEXT NOT NULL,
                timeframe TEXT NOT NULL,
                direction TEXT NOT NULL,
                entry_price DOUBLE PRECISION NOT NULL,
                exit_price DOUBLE PRECISION NOT NULL,
                pnl_bps DOUBLE PRECISION NOT NULL,
                was_win BOOLEAN NOT NULL,
                created_at TEXT NOT NULL,
                verified_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS pending_trades (
                id TEXT PRIMARY KEY,
                direction TEXT NOT NULL,
                asset TEXT NOT NULL,
                timeframe TEXT NOT NULL,
                binance_symbol TEXT NOT NULL,
                entry_price DOUBLE PRECISION NOT NULL,
                created_at TEXT NOT NULL,
                verify_at TEXT NOT NULL,
                is_forex BOOLEAN NOT NULL,
                source_directions TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS signal_votes (
                id SERIAL PRIMARY KEY,
                signal_name TEXT NOT NULL,
                was_correct BOOLEAN NOT NULL,
                created_at TEXT NOT NULL
            );
        ");

        BotLogger.Info("[PostgreSQL DB] Database tables initialized successfully.");

        // Initialize Trade Outcome Online Learning Engine
        await TradeOutcomeTracker.InitializeAsync();
    }

    public static async Task<bool> IsUserAllowedAsync(long chatId)
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return false;
        using var conn = GetConnection();
        return await conn.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM allowed_users WHERE chat_id = @chatId)", new { chatId });
    }

    public static async Task<bool> IsAdminAsync(long chatId)
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return false;
        using var conn = GetConnection();
        return await conn.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM admins WHERE chat_id = @chatId)", new { chatId });
    }

    public static async Task<List<long>> GetAdminChatIdsAsync()
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return new List<long>();
        using var conn = GetConnection();
        var result = await conn.QueryAsync<long>("SELECT chat_id FROM admins");
        return result.ToList();
    }

    public static async Task<int> GetTotalUsersCountAsync()
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return 0;
        using var conn = GetConnection();
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM all_users");
    }

    public static async Task<int> GetAllowedUsersCountAsync()
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return 0;
        using var conn = GetConnection();
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM allowed_users");
    }

    public static async Task<int> GetRegistrationsCountAsync()
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return 0;
        using var conn = GetConnection();
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM registrations");
    }

    public static async Task<List<TelegramBotService.PocketRegistration>> GetLatestRegistrationsAsync(int limit = 15)
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return new List<TelegramBotService.PocketRegistration>();
        using var conn = GetConnection();
        var result = await conn.QueryAsync<TelegramBotService.PocketRegistration>(
            "SELECT chat_id as ChatId, pocket_id as PocketId, has_registered as HasRegistered, has_deposited as HasDeposited, deposit_amount as DepositAmount FROM registrations ORDER BY pocket_id DESC LIMIT @limit",
            new { limit });
        return result.ToList();
    }

    public static async Task<TelegramBotService.PocketRegistration?> GetPocketRegistrationAsync(string pocketId)
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return null;
        using var conn = GetConnection();
        return await conn.QueryFirstOrDefaultAsync<TelegramBotService.PocketRegistration>(
            "SELECT chat_id as ChatId, pocket_id as PocketId, has_registered as HasRegistered, has_deposited as HasDeposited, deposit_amount as DepositAmount FROM registrations WHERE pocket_id = @pocketId",
            new { pocketId });
    }

    public static async Task SaveRegistrationAsync(TelegramBotService.PocketRegistration reg)
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return;
        using var conn = GetConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO registrations (chat_id, pocket_id, has_registered, has_deposited, deposit_amount)
            VALUES (@ChatId, @PocketId, @HasRegistered, @HasDeposited, @DepositAmount)
            ON CONFLICT (pocket_id) DO UPDATE SET
                chat_id = EXCLUDED.chat_id,
                has_registered = EXCLUDED.has_registered,
                has_deposited = EXCLUDED.has_deposited,
                deposit_amount = EXCLUDED.deposit_amount
        ", new { reg.ChatId, reg.PocketId, reg.HasRegistered, reg.HasDeposited, reg.DepositAmount });
    }

    public static async Task AddAllowedUserAsync(long chatId)
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return;
        using var conn = GetConnection();
        await conn.ExecuteAsync("INSERT INTO allowed_users (chat_id, created_at) VALUES (@chatId, @now) ON CONFLICT (chat_id) DO NOTHING", new { chatId, now = DateTime.UtcNow.ToString("o") });
    }

    public static async Task AddAdminAsync(long chatId)
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return;
        using var conn = GetConnection();
        await conn.ExecuteAsync("INSERT INTO admins (chat_id) VALUES (@chatId) ON CONFLICT (chat_id) DO NOTHING", new { chatId });
        await conn.ExecuteAsync("INSERT INTO allowed_users (chat_id, created_at) VALUES (@chatId, @now) ON CONFLICT (chat_id) DO NOTHING", new { chatId, now = DateTime.UtcNow.ToString("o") });
    }

    public static async Task RemoveAdminAsync(long chatId)
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return;
        using var conn = GetConnection();
        await conn.ExecuteAsync("DELETE FROM admins WHERE chat_id = @chatId", new { chatId });
    }

    public static async Task SaveTradeOutcomeAsync(TradeOutcomeRecord outcome)
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return;
        try
        {
            using var conn = GetConnection();
            await conn.ExecuteAsync(@"
                INSERT INTO trade_outcomes 
                (id, asset, timeframe, direction, entry_price, exit_price, pnl_bps, was_win, created_at, verified_at)
                VALUES (@Id, @Asset, @Timeframe, @Direction, @EntryPrice, @ExitPrice, @PnlBps, @WasWin, @CreatedAt, @VerifiedAt)
                ON CONFLICT (id) DO UPDATE SET
                    asset = EXCLUDED.asset,
                    timeframe = EXCLUDED.timeframe,
                    direction = EXCLUDED.direction,
                    entry_price = EXCLUDED.entry_price,
                    exit_price = EXCLUDED.exit_price,
                    pnl_bps = EXCLUDED.pnl_bps,
                    was_win = EXCLUDED.was_win,
                    created_at = EXCLUDED.created_at,
                    verified_at = EXCLUDED.verified_at
            ", new
            {
                outcome.Id,
                outcome.Asset,
                outcome.Timeframe,
                outcome.Direction,
                outcome.EntryPrice,
                outcome.ExitPrice,
                outcome.PnlBps,
                outcome.WasWin,
                outcome.CreatedAt,
                outcome.VerifiedAt
            });
        }
        catch (Exception ex)
        {
            BotLogger.Error("[PostgreSQL DB] Failed to save trade outcome record", ex);
        }
    }

    public static async Task<List<TradeOutcomeRecord>> LoadTradeOutcomesAsync(int limit = 1000)
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return new List<TradeOutcomeRecord>();
        try
        {
            using var conn = GetConnection();
            var rows = await conn.QueryAsync(@"
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
                WasWin = Convert.ToBoolean(r.WasWin),
                CreatedAt = r.CreatedAt ?? "",
                VerifiedAt = r.VerifiedAt ?? ""
            }).ToList();
        }
        catch (Exception ex)
        {
            BotLogger.Error("[PostgreSQL DB] Failed to load trade outcomes", ex);
            return new List<TradeOutcomeRecord>();
        }
    }

    public static async Task RemoveAllowedUserAsync(long chatId)
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return;
        using var conn = GetConnection();
        await conn.ExecuteAsync("DELETE FROM allowed_users WHERE chat_id = @chatId", new { chatId });
    }

    public static async Task AddAllUserAsync(long chatId)
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return;
        using var conn = GetConnection();
        await conn.ExecuteAsync("INSERT INTO all_users (chat_id, created_at) VALUES (@chatId, @now) ON CONFLICT (chat_id) DO NOTHING", new { chatId, now = DateTime.UtcNow.ToString("o") });
    }

    // ── Signal Tracker State ──

    public static async Task SavePendingTradeAsync(SignalTracker.PredictionRecord record)
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return;
        using var conn = GetConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO pending_trades (id, direction, asset, timeframe, binance_symbol, entry_price, created_at, verify_at, is_forex, source_directions)
            VALUES (@Id, @Direction, @Asset, @Timeframe, @BinanceSymbol, @EntryPrice, @CreatedAtStr, @VerifyAtStr, @IsForex, @SourceDirectionsStr)
            ON CONFLICT (id) DO NOTHING", 
            new {
                record.Id,
                record.Direction,
                record.Asset,
                record.Timeframe,
                record.BinanceSymbol,
                record.EntryPrice,
                CreatedAtStr = record.CreatedAt.ToString("o"),
                VerifyAtStr = record.VerifyAt.ToString("o"),
                record.IsForex,
                SourceDirectionsStr = System.Text.Json.JsonSerializer.Serialize(record.SourceDirections)
            });
    }

    public static async Task<List<SignalTracker.PredictionRecord>> GetPendingTradesToVerifyAsync(DateTime upTo)
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return new List<SignalTracker.PredictionRecord>();
        using var conn = GetConnection();
        var rows = await conn.QueryAsync(@"
            SELECT id as Id, direction as Direction, asset as Asset, timeframe as Timeframe, 
                   binance_symbol as BinanceSymbol, entry_price as EntryPrice, 
                   created_at as CreatedAtStr, verify_at as VerifyAtStr, 
                   is_forex as IsForex, source_directions as SourceDirectionsStr
            FROM pending_trades 
            WHERE verify_at <= @UpToStr", 
            new { UpToStr = upTo.ToString("o") });

        return rows.Select(r => new SignalTracker.PredictionRecord
        {
            Id = r.Id,
            Direction = r.Direction,
            Asset = r.Asset,
            Timeframe = r.Timeframe,
            BinanceSymbol = r.BinanceSymbol,
            EntryPrice = (double)r.EntryPrice,
            CreatedAt = DateTime.Parse(r.CreatedAtStr).ToUniversalTime(),
            VerifyAt = DateTime.Parse(r.VerifyAtStr).ToUniversalTime(),
            IsForex = (bool)r.IsForex,
            SourceDirections = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(r.SourceDirectionsStr) ?? new Dictionary<string, string>()
        }).ToList();
    }

    public static async Task DeletePendingTradeAsync(string id)
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return;
        using var conn = GetConnection();
        await conn.ExecuteAsync("DELETE FROM pending_trades WHERE id = @id", new { id });
    }

    public static async Task RecordSignalVoteAsync(string signalName, bool wasCorrect)
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return;
        using var conn = GetConnection();
        await conn.ExecuteAsync("INSERT INTO signal_votes (signal_name, was_correct, created_at) VALUES (@signalName, @wasCorrect, @now)", 
            new { signalName, wasCorrect, now = DateTime.UtcNow.ToString("o") });
    }

    public static async Task<(int Total, int Verified, int Correct)> GetOverallStatsAsync()
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return (0, 0, 0);
        using var conn = GetConnection();
        // Total includes pending + verified
        int pending = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pending_trades");
        var res = await conn.QueryFirstOrDefaultAsync(
            "SELECT COUNT(*) as Verified, COALESCE(SUM(CASE WHEN was_win THEN 1 ELSE 0 END), 0) as Correct FROM trade_outcomes");
        
        int verified = res != null ? (int)res.Verified : 0;
        int correct = res != null ? (int)res.Correct : 0;
        return (pending + verified, verified, correct);
    }

    public static async Task<(int Total, int Verified, int Correct)> GetStatsAsync(string asset, string timeframe)
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return (0, 0, 0);
        using var conn = GetConnection();
        int pending = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pending_trades WHERE asset = @asset AND timeframe = @timeframe", new { asset, timeframe });
        var res = await conn.QueryFirstOrDefaultAsync(
            "SELECT COUNT(*) as Verified, COALESCE(SUM(CASE WHEN was_win THEN 1 ELSE 0 END), 0) as Correct FROM trade_outcomes WHERE asset = @asset AND timeframe = @timeframe",
            new { asset, timeframe });
        
        int verified = res != null ? (int)res.Verified : 0;
        int correct = res != null ? (int)res.Correct : 0;
        return (pending + verified, verified, correct);
    }

    public static async Task<List<(string asset, string timeframe, int verified, int correct)>> GetAllStatsAsync()
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return new List<(string, string, int, int)>();
        using var conn = GetConnection();
        var rows = await conn.QueryAsync(
            "SELECT asset, timeframe, COUNT(*) as Verified, COALESCE(SUM(CASE WHEN was_win THEN 1 ELSE 0 END), 0) as Correct FROM trade_outcomes GROUP BY asset, timeframe");
        
        var result = new List<(string, string, int, int)>();
        foreach (var r in rows)
        {
            result.Add((r.asset, r.timeframe, (int)r.verified, (int)r.correct));
        }
        return result;
    }

    public static async Task<List<(string signalName, int verified, int correct)>> GetAllSignalVotesAsync()
    {
        if (string.IsNullOrEmpty(GetConnectionString())) return new List<(string, int, int)>();
        using var conn = GetConnection();
        var rows = await conn.QueryAsync(
            "SELECT signal_name, COUNT(*) as Verified, COALESCE(SUM(CASE WHEN was_correct THEN 1 ELSE 0 END), 0) as Correct FROM signal_votes GROUP BY signal_name");
        
        var result = new List<(string, int, int)>();
        foreach (var r in rows)
        {
            result.Add((r.signal_name, (int)r.verified, (int)r.correct));
        }
        return result;
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
