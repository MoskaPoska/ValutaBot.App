using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

string token = "8876026967:AAFbQ4cjOEdPHZuZ-LeZqbqUgDAoICDRH4c";
long chatId = 1103551505;
string webAppUrl = "https://chowder-dreamland-spotlight.ngrok-free.dev";
string cacheBustedUrl = webAppUrl + "?v=" + DateTime.UtcNow.Ticks;

string text = "?? <b>Панель администратора TradeAI</b>";
var keyboard = new
{
    keyboard = new object[]
    {
        new object[]
        {
            new { text = "?? Открыть TradeAI", web_app = new { url = cacheBustedUrl } }
        },
        new object[]
        {
            new { text = "?? Всего юзеров" },
            new { text = "?? Добавить админа" },
            new { text = "?? Удалить доступ" }
        }
    },
    resize_keyboard = true
};

var payload = new { chat_id = chatId, text, parse_mode = "HTML", reply_markup = keyboard };
var json = JsonSerializer.Serialize(payload);
Console.WriteLine(json);
using var _httpClient = new HttpClient();
using var content = new StringContent(json, Encoding.UTF8, "application/json");
var res = await _httpClient.PostAsync(new Uri("https://api.telegram.org/bot" + token + "/sendMessage"), content);

Console.WriteLine("Status: " + res.StatusCode);
Console.WriteLine(await res.Content.ReadAsStringAsync());
