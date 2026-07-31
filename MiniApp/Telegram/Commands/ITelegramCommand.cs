using System.Threading.Tasks;

namespace ValutaBot.App.MiniApp.Telegram.Commands
{
    public interface ITelegramCommand
    {
        bool CanHandle(long chatId, string command, string cleanText);
        Task ExecuteAsync(long chatId, string command, string cleanText, bool isAdmin, string token, string webAppUrl);
    }
}
