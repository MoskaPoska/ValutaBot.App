using System;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace ValutaBot.MiniApp;

public static class AuthService
{
    public static async Task<(bool isAuthorized, string? errorMessage)> IsRequestAuthorized(HttpContext context)
    {
        string? botToken = TelegramNotifier.GetToken();
        if (string.IsNullOrEmpty(botToken))
        {
            return (true, null); // Bypass validation in local dev environment
        }

        if (!context.Request.Headers.TryGetValue("X-Telegram-Init-Data", out var initDataValues))
        {
            return (false, "Missing authorization header");
        }

        string initData = initDataValues.ToString();
        if (string.IsNullOrEmpty(initData))
        {
            return (false, "Empty authorization token");
        }

        // ─── Custom Signed URL Validation ───
        if (initData.Contains("custom_user_id=") && initData.Contains("custom_user_sign="))
        {
            var query = HttpUtility.ParseQueryString(initData);
            string? customIdStr = query["custom_user_id"];
            string? customSign = query["custom_user_sign"];

            if (long.TryParse(customIdStr, out long userId))
            {
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(botToken));
                byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(userId.ToString()));
                string expectedSign = Convert.ToHexString(hashBytes).ToLowerInvariant();

                if (string.Equals(customSign, expectedSign, StringComparison.OrdinalIgnoreCase))
                {
                    if (!await TelegramBotService.IsUserAllowed(userId))
                    {
                        return (false, "Access Denied: Pocket Option registration and deposit required");
                    }
                    context.Items["userId"] = userId;
                    return (true, null);
                }
            }

            return (false, "Invalid custom authorization signature");
        }

        // ─── Standard Telegram InitData Validation ───
        if (!TelegramInitDataValidator.Validate(initData, botToken, out long tgUserId, out _))
        {
            return (false, "Invalid Telegram authorization signature");
        }

        if (!await TelegramBotService.IsUserAllowed(tgUserId))
        {
            return (false, "Access Denied: Pocket Option registration and deposit required");
        }

        context.Items["userId"] = tgUserId;
        return (true, null);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, DateTime> _rateLimits = new();
    
    public static bool IsRateLimited(HttpContext context, out string? errorMessage)
    {
        errorMessage = null;
        if (context.Items.TryGetValue("userId", out var userIdObj) && userIdObj is long userId)
        {
            var now = DateTime.UtcNow;
            if (_rateLimits.TryGetValue(userId, out var lastReq))
            {
                if ((now - lastReq).TotalSeconds < 2)
                {
                    errorMessage = "Rate limit exceeded. Please wait a few seconds.";
                    return true;
                }
            }
            _rateLimits[userId] = now;
        }
        return false;
    }

    public static string GetSignedWebAppUrl(long chatId, string webAppUrl, string token)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(token));
        byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(chatId.ToString()));
        string sign = Convert.ToHexString(hashBytes).ToLowerInvariant();
        string delimiter = webAppUrl.Contains('?') ? "&" : "?";
        return $"{webAppUrl}{delimiter}custom_user_id={chatId}&custom_user_sign={sign}";
    }

    public static string SanitizeAsset(string asset)
    {
        return AssetSanitizer.Sanitize(asset);
    }
}
