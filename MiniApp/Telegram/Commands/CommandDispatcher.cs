using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ValutaBot.MiniApp;

namespace ValutaBot.App.MiniApp.Telegram.Commands
{
    public static class CommandDispatcher
    {
        private static readonly List<ITelegramCommand> _commands = new()
        {
            new SignalCommand(),         // /signal EURUSD_OTC [m1|m5|m15]
            new StartCommand(),
            new HelpCommand(),
            new SettingsCommand(),
            new AdminStatsCommand(),
            new AdminAccessCommand(),
            new AdminAddUserCommand(),
            new AdminAddUserStateCommand(),
            new AdminRemoveUserCommand(),
            new AdminRemoveUserStateCommand(),
            new SubmitIdStateCommand()
        };

        public static async Task DispatchAsync(long chatId, string command, string cleanText, bool isAdmin, string token, string webAppUrl)
        {
            foreach (var cmd in _commands)
            {
                if (cmd.CanHandle(chatId, command, cleanText))
                {
                    await cmd.ExecuteAsync(chatId, command, cleanText, isAdmin, token, webAppUrl);
                    return; // Stop after first successful handling
                }
            }

            // Fallback: AwaitingId state if no command was handled and no other state is active
            if (TelegramBotService.UserStates.TryGetValue(chatId, out var state) && state == TelegramBotService.UserState.AwaitingId)
            {
                // This is already handled by SubmitIdStateCommand, but if it wasn't, we'd handle it here.
            }
            else
            {
                // Unrecognized command or state, could do default behavior here if needed
                // Currently, default welcome logic handles unrecognized if it's just plain text,
                // But SubmitIdStateCommand expects /start to be handled first.
                // Wait, if NO command matches, we should probably do the Catch-All logic from the old file.
                bool isAllowedUser = await ValutaBot.App.MiniApp.Data.Repositories.UserRepository.IsUserAllowedAsync(chatId);

                if (isAdmin)
                {
                    await TelegramBotService.SendAdminWelcome(token, chatId, webAppUrl);
                }
                else if (isAllowedUser)
                {
                    await TelegramBotService.SendUserWelcome(token, chatId, webAppUrl);
                }
                else
                {
                    await TelegramBotService.SendGatedWelcome(token, chatId);
                }
            }
        }
    }
}
