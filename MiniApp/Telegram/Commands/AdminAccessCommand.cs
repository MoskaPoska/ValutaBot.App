using System;
using System.Threading.Tasks;
using ValutaBot.App.MiniApp.Data.Repositories;
using ValutaBot.MiniApp;

namespace ValutaBot.App.MiniApp.Telegram.Commands
{
    public class AdminAccessCommand : ITelegramCommand
    {
        public bool CanHandle(long chatId, string command, string cleanText)
        {
            return command == "/reset" || command == "/resetaccess" ||
                   command == "/addadmin" || command == "/makeadmin" || command == "/grant";
        }

        public async Task ExecuteAsync(long chatId, string command, string cleanText, bool isAdmin, string token, string webAppUrl)
        {
            if (!isAdmin)
            {
                await TelegramBotService.SendMessage(token, chatId, "❌ У вас нет прав для выполнения этой команды.");
                return;
            }

            if (command == "/reset" || command == "/resetaccess")
            {
                long targetChatId = chatId;
                var parts = cleanText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1 && long.TryParse(parts[1], out long parsedId))
                {
                    targetChatId = parsedId;
                }

                await UserRepository.RemoveAllowedUserAsync(targetChatId);
                
                await TelegramBotService.SendMessage(token, chatId, $"🔄 <b>Доступ для пользователя {targetChatId} успешно сброшен!</b>");
                if (targetChatId != chatId)
                {
                    await TelegramBotService.SendMessage(token, targetChatId, "🔄 <b>Ваш доступ был сброшен администратором.</b>");
                }
                return;
            }

            if (command == "/addadmin" || command == "/makeadmin" || command == "/grant")
            {
                var parts = cleanText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1 && long.TryParse(parts[1], out long targetId))
                {
                    await UserRepository.AddAdminAsync(targetId);
                    await UserRepository.AddAllowedUserAsync(targetId);
                    await TelegramBotService.SendMessage(token, chatId, $"👑 <b>Пользователь {targetId} успешно назначен администратором!</b>");
                    await TelegramBotService.SendMessage(token, targetId, "👑 <b>Вам предоставили права администратора и полный доступ к боту!</b>");
                }
                else
                {
                    await TelegramBotService.SendMessage(token, chatId, "💡 <b>Использование:</b> <code>/addadmin TelegramID</code> (например: <code>/addadmin 901492845</code>)");
                }
            }
        }
    }
}
