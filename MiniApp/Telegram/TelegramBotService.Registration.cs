using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using File = System.IO.File;

namespace ValutaBot.MiniApp;

public partial class TelegramBotService
{
    private static readonly string RegistrationsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "registrations.json");
    private static readonly ConcurrentDictionary<string, PocketRegistration> PocketRegistrations = new();

    public class PocketRegistration
    {
        public long ChatId { get; set; }
        public string PocketId { get; set; } = "";
        public bool HasRegistered { get; set; }
        public bool HasDeposited { get; set; }
        public double DepositAmount { get; set; }
    }

    public static async Task ProcessPostback(long chatId, string pocketId, string status, double deposit)
    {
        if (string.IsNullOrEmpty(pocketId) || 
            pocketId.Contains("[") || pocketId.Contains("]") || 
            pocketId.Contains("{") || pocketId.Contains("}") || 
            pocketId.Equals("uid", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[Postback] Ignored test postback with macro placeholder: pocketId={pocketId}");
            return;
        }

        lock (_lock)
        {
            var reg = PocketRegistrations.GetOrAdd(pocketId, pid => new PocketRegistration { PocketId = pid });
            if (chatId > 0) reg.ChatId = chatId;
            
            if (status == "register" || status == "reg" || status == "lead" || status == "registration")
            {
                reg.HasRegistered = true;
            }
            
            if (status == "deposit" || deposit > 0)
            {
                reg.HasDeposited = true;
                reg.DepositAmount += deposit;
            }
            
            SaveRegistrations();
        }

        if ((status == "deposit" || deposit > 0) && chatId > 0)
        {
            lock (_lock)
            {
                if (!AllowedUsers.Contains(chatId))
                {
                    AllowedUsers.Add(chatId);
                    SaveAllowedUsers();
                }
            }
            string? token = TelegramNotifier.GetToken();
            if (!string.IsNullOrEmpty(token))
            {
                await SendMessage(token, chatId, "🎉 <b>Депозит подтвержден. Доступ открыт!</b>");
                await SendUserWelcome(token, chatId, _webAppUrl);
            }
        }
    }

    private static void LoadRegistrations()
    {
        if (!string.IsNullOrEmpty(ConnectionString))
        {
            try
            {
                using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
                conn.Open();
                using var cmd = new Npgsql.NpgsqlCommand("SELECT pocket_id, chat_id, has_registered, has_deposited, deposit_amount FROM registrations", conn);
                using var reader = cmd.ExecuteReader();
                lock (_lock)
                {
                    PocketRegistrations.Clear();
                    while (reader.Read())
                    {
                        string pocketId = reader.GetString(0);
                        if (string.IsNullOrEmpty(pocketId) || 
                            pocketId.Contains("[") || pocketId.Contains("]") || 
                            pocketId.Contains("{") || pocketId.Contains("}") || 
                            pocketId.Equals("uid", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var reg = new PocketRegistration
                        {
                            PocketId = pocketId,
                            ChatId = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                            HasRegistered = reader.GetBoolean(2),
                            HasDeposited = reader.GetBoolean(3),
                            DepositAmount = reader.GetDouble(4)
                        };
                        PocketRegistrations[reg.PocketId] = reg;
                    }
                }
                Console.WriteLine($"[DB] Loaded {PocketRegistrations.Count} registrations from PostgreSQL");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Error loading registrations: {ex.Message}. Falling back to file.");
            }
        }

        try
        {
            if (File.Exists(RegistrationsFile))
            {
                var json = File.ReadAllText(RegistrationsFile);
                var list = JsonSerializer.Deserialize<List<PocketRegistration>>(json);
                if (list != null)
                {
                    PocketRegistrations.Clear();
                    foreach (var reg in list)
                    {
                        if (!string.IsNullOrEmpty(reg.PocketId) && 
                            !reg.PocketId.Contains("[") && !reg.PocketId.Contains("]") && 
                            !reg.PocketId.Contains("{") && !reg.PocketId.Contains("}") && 
                            !reg.PocketId.Equals("uid", StringComparison.OrdinalIgnoreCase))
                        {
                            PocketRegistrations[reg.PocketId] = reg;
                        }
                    }
                    Console.WriteLine($"[TG Bot] Loaded {PocketRegistrations.Count} pocket registrations");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] Error loading registrations: {ex.Message}");
        }
    }

    private static void SaveRegistrations()
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
                        
                        lock (_lock)
                        {
                            foreach (var reg in PocketRegistrations.Values)
                            {
                                using var cmd = new Npgsql.NpgsqlCommand(@"
                                    INSERT INTO registrations (pocket_id, chat_id, has_registered, has_deposited, deposit_amount)
                                    VALUES (@pocketId, @chatId, @hasReg, @hasDep, @depAmt)
                                    ON CONFLICT (pocket_id) 
                                    DO UPDATE SET 
                                        chat_id = EXCLUDED.chat_id,
                                        has_registered = EXCLUDED.has_registered,
                                        has_deposited = EXCLUDED.has_deposited,
                                        deposit_amount = EXCLUDED.deposit_amount", conn, trans);

                                cmd.Parameters.AddWithValue("pocketId", reg.PocketId);
                                cmd.Parameters.AddWithValue("chatId", reg.ChatId);
                                cmd.Parameters.AddWithValue("hasReg", reg.HasRegistered);
                                cmd.Parameters.AddWithValue("hasDep", reg.HasDeposited);
                                cmd.Parameters.AddWithValue("depAmt", reg.DepositAmount);

                                cmd.ExecuteNonQuery();
                            }
                        }

                        trans.Commit();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DB] Error saving registrations to PostgreSQL: {ex.Message}");
                    }
                }

                try
                {
                    List<PocketRegistration> regsToSave;
                    lock (_lock)
                    {
                        regsToSave = PocketRegistrations.Values.ToList();
                    }
                    var json = JsonSerializer.Serialize(regsToSave);
                    File.WriteAllText(RegistrationsFile, json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TG Bot] Error saving registrations: {ex.Message}");
                }
            }
        });
    }
}
