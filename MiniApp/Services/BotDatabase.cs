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
        ");

        BotLogger.Info("[SQLite DB] Database tables initialized successfully.");

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
