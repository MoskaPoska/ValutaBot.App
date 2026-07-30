using System;
using System.Threading.Tasks;

namespace ValutaBot.MiniApp;

public partial class TelegramBotService
{
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

        var reg = await BotDatabase.GetPocketRegistrationAsync(pocketId);
        if (reg == null)
        {
            reg = new PocketRegistration { PocketId = pocketId };
        }
        
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
        
        await BotDatabase.SaveRegistrationAsync(reg);

        if ((status == "deposit" || deposit > 0) && chatId > 0)
        {
            await BotDatabase.AddAllowedUserAsync(chatId);
            string? token = TelegramNotifier.GetToken();
            if (!string.IsNullOrEmpty(token))
            {
                await SendMessage(token, chatId, "🎉 <b>Депозит подтвержден. Доступ открыт!</b>");
                await SendUserWelcome(token, chatId, _webAppUrl);
            }
        }
    }
}
