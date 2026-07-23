using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using File = System.IO.File;

namespace ValutaBot.MiniApp;

public partial class TelegramBotService : BackgroundService
{
    private static readonly HttpClient _httpClient = new HttpClient(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        EnableMultipleHttp2Connections = true
    }) { Timeout = TimeSpan.FromSeconds(35) };

    private static readonly string AllowedUsersFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "allowed_users.json");
    private static readonly string AdminsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "admins.json");
    private static readonly string AllUsersFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "all_users.json");

    private static readonly HashSet<long> AllowedUsers = new();
    private static readonly HashSet<long> AdminChatIds = new();
    private static readonly HashSet<long> AllUsers = new();
    private static readonly ConcurrentDictionary<long, DateTime> UserLastActivity = new();
    private static readonly object _lock = new();
    private static readonly object _saveLock = new();
    private static string _webAppUrl = "https://chowder-dreamland-spotlight.ngrok-free.dev";

    public static bool IsUserAllowed(long chatId)
    {
        lock (_lock)
        {
            return AdminChatIds.Contains(chatId) || AllowedUsers.Contains(chatId);
        }
    }

    public static async Task SendMessageToAdmins(string text)
    {
        string? token = TelegramNotifier.GetToken();
        if (string.IsNullOrEmpty(token)) return;

        List<long> adminsToNotify;
        lock (_lock)
        {
            adminsToNotify = AdminChatIds.ToList();
        }

        foreach (long adminId in adminsToNotify)
        {
            await SendMessage(token, adminId, text);
        }
    }

    private enum UserState
    {
        None,
        AwaitingId,
        AwaitingDeleteId,
        AwaitingAddAdminId
    }

    private static readonly ConcurrentDictionary<long, UserState> UserStates = new();
    private static readonly ConcurrentDictionary<long, string> UserSubmittedIds = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? token = TelegramNotifier.GetToken();
        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("[TG Bot] Telegram Bot Token is not set. Bot service will not run.");
            return;
        }

        _webAppUrl = Environment.GetEnvironmentVariable("WEBAPP_URL") 
            ?? "https://chowder-dreamland-spotlight.ngrok-free.dev";

        Console.WriteLine($"[TG Bot] Service started. WebApp URL: {_webAppUrl}");

        lock (_lock)
        {
            BotDatabase.Initialize();

            // Auto-seed admin IDs 1103551505, 901492845 and any env ADMIN_CHAT_ID / ADMIN_IDS
            BotDatabase.AddAdmin(1103551505);
            BotDatabase.AddAdmin(901492845);

            string envAdmin = Environment.GetEnvironmentVariable("ADMIN_CHAT_ID") ?? Environment.GetEnvironmentVariable("ADMIN_IDS") ?? "";
            foreach (var part in envAdmin.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (long.TryParse(part, out long parsedEnvAdmin))
                {
                    BotDatabase.AddAdmin(parsedEnvAdmin);
                }
            }

            AllowedUsers.Clear();
            foreach (var id in BotDatabase.LoadAllowedUsers()) AllowedUsers.Add(id);
            AdminChatIds.Clear();
            foreach (var id in BotDatabase.LoadAdmins()) AdminChatIds.Add(id);
            AllUsers.Clear();
            foreach (var id in BotDatabase.LoadAllUsers()) AllUsers.Add(id);
        }

        long offset = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                string url = $"https://api.telegram.org/bot{token}/getUpdates?offset={offset}&timeout=30";
                var response = await _httpClient.GetAsync(url, stoppingToken);
                if (!response.IsSuccessStatusCode)
                {
                    await Task.Delay(5000, stoppingToken);
                    continue;
                }

                var jsonStr = await response.Content.ReadAsStringAsync(stoppingToken);
                using var doc = JsonDocument.Parse(jsonStr);
                if (doc.RootElement.TryGetProperty("result", out var resultArr) && resultArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var update in resultArr.EnumerateArray())
                    {
                        long updateId = update.GetProperty("update_id").GetInt64();
                        offset = updateId + 1;

                        if (update.TryGetProperty("message", out var message))
                        {
                            long chatId = message.GetProperty("chat").GetProperty("id").GetInt64();
                            string text = message.TryGetProperty("text", out var tProp) ? (tProp.GetString() ?? "") : "";
                            
                            string username = "";
                            if (message.TryGetProperty("from", out var fromUser))
                            {
                                username = fromUser.TryGetProperty("username", out var uProp) ? (uProp.GetString() ?? "") : "";
                            }

                            _ = Task.Run(async () =>
                            {
                                try { await HandleMessage(token, chatId, text, username, _webAppUrl); }
                                catch (Exception ex) { Console.WriteLine($"[TG Bot] HandleMessage error ({chatId}): {ex.Message}"); }
                            });
                        }
                        else if (update.TryGetProperty("callback_query", out var callbackQuery))
                        {
                            string queryId = callbackQuery.GetProperty("id").GetString()!;
                            long chatId = callbackQuery.GetProperty("message").GetProperty("chat").GetProperty("id").GetInt64();
                            string data = callbackQuery.GetProperty("data").GetString()!;
                            int messageId = callbackQuery.GetProperty("message").GetProperty("message_id").GetInt32();

                            string username = "";
                            if (callbackQuery.TryGetProperty("from", out var fromUser))
                            {
                                username = fromUser.TryGetProperty("username", out var uProp) ? (uProp.GetString() ?? "") : "";
                            }

                            _ = Task.Run(async () =>
                            {
                                try { await HandleCallback(token, queryId, chatId, data, messageId, username, _webAppUrl); }
                                catch (Exception ex) { Console.WriteLine($"[TG Bot] HandleCallback error ({chatId}): {ex.Message}"); }
                            });
                        }
                    }
                }
            }
            catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                BotLogger.Error($"[TG Bot] Error in polling loop: {ex.Message}", ex);
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private static async Task SendMessage(string token, long chatId, string text)
    {
        var client = TelegramNotifier.GetBotClient() ?? new TelegramBotClient(token);
        try
        {
            await client.SendTextMessageAsync(chatId, text, parseMode: ParseMode.Html);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] sendMessage SDK exception: {ex.Message}");
        }
    }

    private static async Task SendMessageWithKeyboard(string token, long chatId, string text, object keyboard)
    {
        try
        {
            var payload = new { chat_id = chatId, text, parse_mode = "HTML", reply_markup = keyboard };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _httpClient.PostAsync($"https://api.telegram.org/bot{token}/sendMessage", content);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync();
                Console.WriteLine($"[TG Bot] sendMessage error: {err}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] sendMessage exception: {ex.Message}");
        }
    }

    private static async Task EditMessageText(string token, long chatId, int messageId, string text)
    {
        var client = TelegramNotifier.GetBotClient() ?? new TelegramBotClient(token);
        try
        {
            await client.EditMessageTextAsync(chatId, messageId, text, parseMode: ParseMode.Html);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] editMessageText SDK exception: {ex.Message}");
        }
    }

    private static async Task AnswerCallbackQuery(string token, string callbackQueryId, string text)
    {
        var client = TelegramNotifier.GetBotClient() ?? new TelegramBotClient(token);
        try
        {
            await client.AnswerCallbackQueryAsync(callbackQueryId, text);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] answerCallbackQuery SDK exception: {ex.Message}");
        }
    }

    private static string ConnectionString = "";

    private static void LoadAllowedUsers()
    {
        if (!string.IsNullOrEmpty(ConnectionString))
        {
            try
            {
                using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
                conn.Open();
                using var cmd = new Npgsql.NpgsqlCommand("SELECT chat_id FROM allowed_users", conn);
                using var reader = cmd.ExecuteReader();
                lock (_lock)
                {
                    AllowedUsers.Clear();
                    while (reader.Read())
                    {
                        AllowedUsers.Add(reader.GetInt64(0));
                    }
                }
                Console.WriteLine($"[DB] Loaded {AllowedUsers.Count} allowed users from PostgreSQL");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Error loading allowed users: {ex.Message}. Falling back to file.");
            }
        }

        try
        {
            if (File.Exists(AllowedUsersFile))
            {
                var json = File.ReadAllText(AllowedUsersFile);
                var list = JsonSerializer.Deserialize<List<long>>(json);
                if (list != null)
                {
                    AllowedUsers.Clear();
                    foreach (var id in list) AllowedUsers.Add(id);
                    Console.WriteLine($"[TG Bot] Loaded {AllowedUsers.Count} allowed users");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] Error loading allowed users: {ex.Message}");
        }
    }

    private static void SaveAllowedUsers()
    {
        Task.Run(() =>
        {
            lock (_saveLock)
            {
                if (!string.IsNullOrEmpty(ConnectionString))
                {
                    try
                    {
                        using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
                        conn.Open();
                        using var trans = conn.BeginTransaction();
                        
                        using (var cmd = new Npgsql.NpgsqlCommand("DELETE FROM allowed_users", conn, trans))
                        {
                            cmd.ExecuteNonQuery();
                        }

                        lock (_lock)
                        {
                            foreach (var chatId in AllowedUsers)
                            {
                                using var cmd = new Npgsql.NpgsqlCommand("INSERT INTO allowed_users (chat_id) VALUES (@chatId) ON CONFLICT DO NOTHING", conn, trans);
                                cmd.Parameters.AddWithValue("chatId", chatId);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        trans.Commit();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DB] Error saving allowed users: {ex.Message}");
                    }
                }

                try
                {
                    List<long> usersToSave;
                    lock (_lock)
                    {
                        usersToSave = AllowedUsers.ToList();
                    }
                    var json = JsonSerializer.Serialize(usersToSave);
                    File.WriteAllText(AllowedUsersFile, json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TG Bot] Error saving allowed users: {ex.Message}");
                }
            }
        });
    }

    private static void SaveAllUsers()
    {
        Task.Run(() =>
        {
            lock (_saveLock)
            {
                try
                {
                    List<long> usersToSave;
                    lock (_lock)
                    {
                        usersToSave = AllUsers.ToList();
                    }
                    var json = JsonSerializer.Serialize(usersToSave);
                    File.WriteAllText(AllUsersFile, json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TG Bot] Error saving all users: {ex.Message}");
                }
            }
        });
    }

    private static async Task ResetChatMenuButton(string token, long chatId)
    {
        try
        {
            var payload = new
            {
                chat_id = chatId,
                menu_button = new { type = "default" }
            };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await _httpClient.PostAsync($"https://api.telegram.org/bot{token}/setChatMenuButton", content);
        }
        catch { }
    }

    private static async Task SendDatabaseFile(string token, long chatId, string filePath, string caption)
    {
        if (!File.Exists(filePath))
        {
            await SendMessage(token, chatId, $"❌ Файл {Path.GetFileName(filePath)} еще не создан.");
            return;
        }

        var client = TelegramNotifier.GetBotClient() ?? new TelegramBotClient(token);
        try
        {
            await using var stream = File.OpenRead(filePath);
            await client.SendDocumentAsync(
                chatId: chatId,
                document: InputFile.FromStream(stream, Path.GetFileName(filePath)),
                caption: caption
            );
        }
        catch (Exception ex)
        {
            await SendMessage(token, chatId, $"❌ Ошибка отправки файла: {ex.Message}");
        }
    }
}
