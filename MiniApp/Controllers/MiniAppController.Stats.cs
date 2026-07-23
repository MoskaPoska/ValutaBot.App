using System;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace ValutaBot.MiniApp;

public static partial class MiniAppController
{
    public static IResult HandleGetStats(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        var overall = SignalTracker.GetOverallStats();
        var allStats = SignalTracker.GetAllStats()
            .Where(s => s.Key != "ALL" && s.Verified > 0)
            .OrderByDescending(s => s.Verified)
            .Select(s => new
            {
                key       = s.Key,
                verified  = s.Verified,
                correct   = s.Correct,
                incorrect = s.Incorrect,
                winRate   = s.WinRate,
                pending   = s.Pending
            });

        var signalSources = SignalTracker.GetSignalStats()
            .Select(s => new
            {
                name      = s.name,
                agreeRate = s.agreeRatePct,
                weight    = s.weight,
                count     = s.count
            });

        var recent = SignalTracker.GetRecentArchive(20)
            .Select(r => new
            {
                asset     = r.Asset,
                tf        = r.Timeframe,
                direction = r.Direction,
                entry     = Math.Round(r.EntryPrice, 5),
                exit      = r.ExitPrice.HasValue ? Math.Round(r.ExitPrice.Value, 5) : (double?)null,
                pnlBps    = r.PnlBps,
                correct   = r.WasCorrect,
                at        = r.CreatedAt.ToString("HH:mm:ss")
            });

        return Results.Json(new
        {
            overall = new
            {
                winRate   = overall.HasData ? overall.WinRate : (double?)null,
                verified  = overall.Verified,
                correct   = overall.Correct,
                incorrect = overall.Incorrect,
                pending   = SignalTracker.GetPendingCount(),
                hasData   = overall.HasData
            },
            byAsset       = allStats,
            signalSources,
            recentSignals = recent
        });
    }

    public static IResult HandleGetSignalStats(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        if (!IsRequestAuthorized(context, out string? authError))
            return Results.Json(new { error = authError }, statusCode: 401);

        return Results.Json(new
        {
            accuracy = SignalTracker.GetOverallStats().WinRate,
            signals = SignalTracker.GetSignalStats()
        });
    }
}
