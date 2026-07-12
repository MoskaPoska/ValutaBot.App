using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace ValutaBot.MiniApp;

public static class TelegramInitDataValidator
{
    public static bool Validate(string initData, string botToken, out long userId, out string? username)
    {
        userId = 0;
        username = null;

        if (string.IsNullOrEmpty(initData)) return false;

        try
        {
            var parsed = HttpUtility.ParseQueryString(initData);
            string? hash = parsed["hash"];
            if (string.IsNullOrEmpty(hash)) return false;

            // Sort all query parameters alphabetically except hash
            var sortedParams = parsed.AllKeys
                .Where(k => k != "hash" && k != null)
                .OrderBy(k => k)
                .Select(k => $"{k}={parsed[k]}")
                .ToList();

            string dataCheckString = string.Join("\n", sortedParams);

            // Compute secret key: HMAC_SHA256("WebAppData", botToken)
            byte[] secretKey = HMAC_SHA256(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(botToken));

            // Compute expected hash: HMAC_SHA256(dataCheckString, secretKey)
            byte[] expectedHashBytes = HMAC_SHA256(Encoding.UTF8.GetBytes(dataCheckString), secretKey);
            string expectedHash = Convert.ToHexString(expectedHashBytes).ToLowerInvariant();

            if (!string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Extract user information
            string? userJson = parsed["user"];
            if (!string.IsNullOrEmpty(userJson))
            {
                using var doc = JsonDocument.Parse(userJson);
                if (doc.RootElement.TryGetProperty("id", out var idProp))
                {
                    userId = idProp.GetInt64();
                }
                if (doc.RootElement.TryGetProperty("username", out var uProp))
                {
                    username = uProp.GetString();
                }
            }

            // Check auth_date expiration (e.g. 24 hours)
            string? authDateStr = parsed["auth_date"];
            if (long.TryParse(authDateStr, out long authDate))
            {
                var authTime = DateTimeOffset.FromUnixTimeSeconds(authDate).UtcDateTime;
                if ((DateTime.UtcNow - authTime).TotalHours > 24)
                {
                    Console.WriteLine($"[InitData] Session expired: authTime={authTime}, current={DateTime.UtcNow}");
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[InitData] Exception during validation: {ex.Message}");
            return false;
        }
    }

    private static byte[] HMAC_SHA256(byte[] data, byte[] key)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(data);
    }
}
