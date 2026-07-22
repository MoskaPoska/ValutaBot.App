using System.Text.Json;

namespace ValutaBot.MiniApp;

public static class TelegramNotifier
{
    private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    private static string? _botToken;
    private static long _defaultChatId;

    public static void SetDefaultChatId(long chatId) => _defaultChatId = chatId;
    public static long GetDefaultChatId() => _defaultChatId;
    public static string? GetToken() => _botToken;

    public static void Init(string? token)
    {
        _botToken = token;
        if (!string.IsNullOrEmpty(token))
            Console.WriteLine("[TG] TelegramNotifier initialized");
    }

    public static async Task SendMessage(long chatId, string text)
    {
        if (string.IsNullOrEmpty(_botToken)) return;

        try
        {
            var payload = new { chat_id = chatId, text, parse_mode = "HTML" };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"https://api.telegram.org/bot{_botToken}/sendMessage", content);

            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync();
                Console.WriteLine($"[TG] sendMessage error: {err}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG] sendMessage exception: {ex.Message}");
        }
    }

    public static async Task SendAlert(long chatId, string title, string body, string color = "#b388ff")
    {
        var msg = $"<b>{title}</b>\n{body}";
        await SendMessage(chatId, msg);
    }
}
