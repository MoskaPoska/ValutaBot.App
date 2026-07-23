using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace ValutaBot.MiniApp;

public sealed class TokenBucketRateLimiterMiddleware
{
    private readonly RequestDelegate _next;

    private class TokenBucket
    {
        public double Tokens { get; set; }
        public DateTime LastRefill { get; set; }
        public readonly object Lock = new();
    }

    private static readonly ConcurrentDictionary<string, TokenBucket> _buckets = new();

    private const double Capacity = 10.0;           // Max burst of 10 requests
    private const double RefillRatePerSec = 0.5;   // Refills 1 token every 2 seconds

    public TokenBucketRateLimiterMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only rate-limit API endpoints (e.g. /api/analyze, /api/autotrade)
        string path = context.Request.Path.Value?.ToLower() ?? "";
        if (!path.StartsWith("/api") && !path.StartsWith("/analyze"))
        {
            await _next(context);
            return;
        }

        string clientFingerprint = ComputeClientFingerprint(context);
        var bucket = _buckets.GetOrAdd(clientFingerprint, _ => new TokenBucket
        {
            Tokens = Capacity,
            LastRefill = DateTime.UtcNow
        });

        bool allowed = ConsumeToken(bucket);
        if (!allowed)
        {
            BotLogger.Info($"[RateLimiter 🛡️] Blocked excessive request rate for fingerprint {clientFingerprint[..8]}...");
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync("{\"error\":\"Слишком много запросов. Подождите несколько секунд.\",\"retryAfterSeconds\":2}");
            return;
        }

        await _next(context);
    }

    private static bool ConsumeToken(TokenBucket bucket)
    {
        lock (bucket.Lock)
        {
            var now = DateTime.UtcNow;
            double elapsedSecs = (now - bucket.LastRefill).TotalSeconds;

            // Refill tokens based on elapsed time
            bucket.Tokens = Math.Min(Capacity, bucket.Tokens + elapsedSecs * RefillRatePerSec);
            bucket.LastRefill = now;

            if (bucket.Tokens >= 1.0)
            {
                bucket.Tokens -= 1.0;
                return true;
            }

            return false;
        }
    }

    private static string ComputeClientFingerprint(HttpContext context)
    {
        string ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown_ip";
        string ua = context.Request.Headers["User-Agent"].ToString();
        string lang = context.Request.Headers["Accept-Language"].ToString();
        string initData = context.Request.Headers["X-Telegram-Init-Data"].ToString();

        string raw = $"{ip}|{ua}|{lang}|{initData}";
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}
