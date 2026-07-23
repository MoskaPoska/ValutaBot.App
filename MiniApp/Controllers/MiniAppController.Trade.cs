using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace ValutaBot.MiniApp;

public static partial class MiniAppController
{
    public static async Task<IResult> HandleAutoTradeExecuteAsync(HttpContext context, AutoTradeExecutionRequest req)
    {
        if (!IsRequestAuthorized(context, out string? authError))
            return Results.Json(new { success = false, error = authError }, statusCode: 401);

        if (IsRateLimited(context, out string? limitError))
            return Results.Json(new { success = false, error = limitError }, statusCode: 429);

        if (req == null || string.IsNullOrWhiteSpace(req.Asset) || string.IsNullOrWhiteSpace(req.Direction))
        {
            return Results.Json(new { success = false, error = "Invalid trade payload" }, statusCode: 400);
        }

        var result = await AutoTradeService.Execute1ClickTradeAsync(req);
        return Results.Json(result);
    }

    public static IResult HandleSaveSsid(HttpContext context, long chatId, string ssid)
    {
        if (!IsRequestAuthorized(context, out string? authError))
            return Results.Json(new { success = false, error = authError }, statusCode: 401);

        if (chatId <= 0 || string.IsNullOrWhiteSpace(ssid))
        {
            return Results.Json(new { success = false, error = "Invalid chatId or SSID" }, statusCode: 400);
        }

        AutoTradeService.SaveUserSsid(chatId, ssid);
        return Results.Json(new { success = true, message = "Pocket Option session token saved securely." });
    }
}
