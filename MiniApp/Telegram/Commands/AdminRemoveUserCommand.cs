using System.Threading.Tasks;
using ValutaBot.MiniApp;

namespace ValutaBot.App.MiniApp.Telegram.Commands
{
    public class AdminRemoveUserCommand : ITelegramCommand
    {
        public bool CanHandle(long chatId, string command, string cleanText)
        {
            return cleanText == "🚫 Удалить доступ";
        }

        public async Task ExecuteAsync(long chatId, string command, string cleanText, bool isAdmin, string token, string webAppUrl)
        {
            if (!isAdmin) return;
            TelegramBotService.UserStates[chatId] = TelegramBotService.UserState.AwaitingDeleteId;
            await TelegramBotService.SendMessage(token, chatId, "✍️ <b>Пожалуйста, введите Telegram Chat ID пользователя, доступ которого нужно аннулировать:</b>\n\n(Вы можете скопировать Chat ID из логов регистраций в 👥 Всего юзеров)");
        }
    }
}
