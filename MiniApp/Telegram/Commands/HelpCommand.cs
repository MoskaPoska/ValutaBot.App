using System.Threading.Tasks;
using ValutaBot.MiniApp;

namespace ValutaBot.App.MiniApp.Telegram.Commands
{
    public class HelpCommand : ITelegramCommand
    {
        public bool CanHandle(long chatId, string command, string cleanText)
        {
            return command == "/help" || cleanText == "❓ Инструкция" || cleanText == "❓ Инструкция как пользоваться ботом";
        }

        public async Task ExecuteAsync(long chatId, string command, string cleanText, bool isAdmin, string token, string webAppUrl)
        {
            string helpText = "📖 <b>Инструкция по использованию TradeAI:</b>\n\n" +
                               "1. Нажмите кнопку <b>📊 Открыть TradeAI</b> внизу экрана.\n" +
                               "2. Выберите интересующую валютную пару и таймфрейм.\n" +
                               "3. Бот проанализирует рынок по техническим индикаторам, объемам и выдаст точный прогноз (BUY/PUT) с процентом уверенности.";
            await TelegramBotService.SendMessage(token, chatId, helpText);
        }
    }
}
