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

        // 🔒 Security Enforcement: Ensure trade is executed strictly for the authenticated User ID
        long authUserId = context.Items.TryGetValue("userId", out var u) && u is long id ? id : req.ChatId;
        var securedReq = req with { ChatId = authUserId };

        var result = await AutoTradeService.Execute1ClickTradeAsync(securedReq);
        return Results.Json(result);
    }

    public static IResult HandleSaveSsid(HttpContext context, long chatId, string ssid)
    {
        if (!IsRequestAuthorized(context, out string? authError))
            return Results.Json(new { success = false, error = authError }, statusCode: 401);

        // 🔒 Security Enforcement: Ensure SSID token is stored strictly for the authenticated User ID
        long authUserId = context.Items.TryGetValue("userId", out var u) && u is long id ? id : chatId;

        if (authUserId <= 0 || string.IsNullOrWhiteSpace(ssid))
        {
            return Results.Json(new { success = false, error = "Invalid user session or SSID" }, statusCode: 400);
        }

        AutoTradeService.SaveUserSsid(authUserId, ssid);
        return Results.Json(new { success = true, message = "Pocket Option session token saved securely." });
    }
}
