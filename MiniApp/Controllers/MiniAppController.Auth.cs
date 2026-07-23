using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Http;

namespace ValutaBot.MiniApp;

public static partial class MiniAppController
{
    private static readonly ConcurrentDictionary<long, DateTime> UserLastRequestTime = new();

    public static bool IsRequestAuthorized(HttpContext context, out string? errorMessage)
    {
        errorMessage = null;

        string? botToken = TelegramNotifier.GetToken();
        if (string.IsNullOrEmpty(botToken))
        {
            return true; // Bypass validation in local dev environment
        }

        if (!context.Request.Headers.TryGetValue("X-Telegram-Init-Data", out var initDataValues))
        {
            errorMessage = "Missing authorization header";
            return false;
        }

        string initData = initDataValues.ToString();
        if (string.IsNullOrEmpty(initData))
        {
            errorMessage = "Empty authorization token";
            return false;
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
                    if (!TelegramBotService.IsUserAllowed(userId))
                    {
                        errorMessage = "Access Denied: Pocket Option registration and deposit required";
                        return false;
                    }
                    context.Items["userId"] = userId;
                    return true;
                }
            }

            errorMessage = "Invalid custom authorization signature";
            return false;
        }

        // ─── Standard Telegram InitData Validation ───
        if (!TelegramInitDataValidator.Validate(initData, botToken, out long tgUserId, out _))
        {
            errorMessage = "Invalid Telegram authorization signature";
            return false;
        }

        if (!TelegramBotService.IsUserAllowed(tgUserId))
        {
            errorMessage = "Access Denied: Pocket Option registration and deposit required";
            return false;
        }

        context.Items["userId"] = tgUserId;
        return true;
    }

    public static bool IsRateLimited(HttpContext context, out string? errorMessage)
    {
        errorMessage = null;
        if (context.Items.TryGetValue("userId", out var obj) && obj is long userId && userId > 0)
        {
            DateTime now = DateTime.UtcNow;
            if (UserLastRequestTime.TryGetValue(userId, out DateTime lastTime))
            {
                double secondsSince = (now - lastTime).TotalSeconds;
                if (secondsSince < 4) // 4 seconds rate limit
                {
                    errorMessage = $"Too many requests. Please wait {Math.Ceiling(4 - secondsSince)}s.";
                    return true;
                }
            }
            UserLastRequestTime[userId] = now;
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
