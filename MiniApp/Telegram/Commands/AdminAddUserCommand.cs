using System.Threading.Tasks;
using ValutaBot.App.MiniApp.Data.Repositories;
using ValutaBot.MiniApp;

namespace ValutaBot.App.MiniApp.Telegram.Commands
{
    public class AdminAddUserCommand : ITelegramCommand
    {
        public bool CanHandle(long chatId, string command, string cleanText)
        {
            return cleanText == "👑 Добавить админа" || cleanText == "➕ Добавить админа" || 
                   (TelegramBotService.UserStates.TryGetValue(0, out _) == false && false); // just for structure, we handle state in CanHandle differently
        }

        public async Task ExecuteAsync(long chatId, string command, string cleanText, bool isAdmin, string token, string webAppUrl)
        {
            if (!isAdmin) return;
            TelegramBotService.UserStates[chatId] = TelegramBotService.UserState.AwaitingAddAdminId;
            await TelegramBotService.SendMessage(token, chatId, "👑 <b>Пожалуйста, введите Telegram Chat ID пользователя, которому нужно предоставить права администратора:</b>\n\n(Вы можете скопировать Chat ID из логов регистраций в 👥 Всего юзеров)");
        }
    }
}
