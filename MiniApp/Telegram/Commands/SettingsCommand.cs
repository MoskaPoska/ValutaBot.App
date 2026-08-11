using System.Threading.Tasks;
using ValutaBot.App.MiniApp.Data.Repositories;

namespace ValutaBot.App.MiniApp.Telegram.Commands
{
    public class SettingsCommand : ITelegramCommand
    {
        public bool CanHandle(long chatId, string command, string cleanText)
        {
            return command == "/settings" || cleanText.Contains("настройки");
        }

        public async Task ExecuteAsync(long chatId, string command, string cleanText, bool isAdmin, string token, string webAppUrl)
        {
            if (!isAdmin)
            {
                // Optionally send a message or just ignore
                return;
            }

            var settings = await UserRepository.GetSettingsAsync(chatId);
            
            var inlineKeyboard = new
            {
                inline_keyboard = new object[]
                {
                    new object[] { new { text = $"{(settings.EnableMl ? "🟢" : "🔴")} Нейросети (ИИ)", callback_data = "toggle_ml" } },
                    new object[] { new { text = $"{(settings.EnableSmc ? "🟢" : "🔴")} SMC Структура", callback_data = "toggle_smc" } },
                    new object[] { new { text = $"{(settings.EnableOf ? "🟢" : "🔴")} Order Flow", callback_data = "toggle_of" } }
                }
            };

            string text = "⚙️ <b>Настройки анализатора</b>\n\nВключайте или отключайте модули анализа. Отключенные модули не будут отображаться в интерфейсе.";

            await ValutaBot.MiniApp.TelegramBotService.SendMessageWithKeyboard(token, chatId, text, inlineKeyboard);
        }
    }
}
