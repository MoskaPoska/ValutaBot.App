using System;
using System.Threading.Tasks;
using ValutaBot.MiniApp;
using ValutaBot.App.MiniApp;
using ValutaBot.MiniApp.Features.MarketAnalysis;
using ValutaBot.MiniApp.CQRS.Handlers;
using ValutaBot.MiniApp.CQRS.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace ValutaBot.App.MiniApp.Telegram.Commands
{
    /// <summary>
    /// Handles: /signal ASSET [timeframe]
    /// Example: /signal EURUSD_OTC m1
    /// Admin-only. Runs market analysis and sends result to Telegram.
    /// </summary>
    public class SignalCommand : ITelegramCommand
    {
        public bool CanHandle(long chatId, string command, string cleanText)
            => command == "/signal";

        public async Task ExecuteAsync(long chatId, string command, string cleanText,
                                       bool isAdmin, string token, string webAppUrl)
        {
            if (!isAdmin)
                return; // non-admins silently ignored

            var parts = cleanText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                await TelegramBotService.SendMessage(token, chatId,
                    "ℹ️ Использование: <code>/signal EURUSD_OTC</code> или <code>/signal EURUSD_OTC m5</code>");
                return;
            }

            string asset     = parts[1];
            string timeframe = parts.Length >= 3 ? parts[2] : "m1";

            await TelegramBotService.SendMessage(token, chatId,
                $"⏳ Анализирую <b>{asset}</b> ({timeframe})…");

            try
            {
                using var scope = MiniAppController.Services!.CreateScope();
                var handler = scope.ServiceProvider
                    .GetRequiredService<GetMarketAnalysisQueryHandler>();

                var query = new GetMarketAnalysisQuery(asset, timeframe);
                var result = await handler.Handle(query, default);

                string text = FormatResult(asset, timeframe, result);
                await TelegramBotService.SendMessage(token, chatId, text);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SignalCommand] Error for {asset}: {ex.Message}");
                // errors go to Railway logs only — no noise in Telegram
            }
        }

        private static string FormatResult(string asset, string timeframe, object result)
        {
            if (result == null)
                return $"❌ Нет данных для <b>{asset}</b>";

            string raw = result.ToString() ?? "";
            if (raw.Length > 3800)
                raw = raw[..3800] + "\n…";

            return $"📊 <b>{asset}</b> | {timeframe}\n\n{raw}";
        }
    }
}
