using System;
using System.IO;

string path = @"MiniApp\Controllers\MiniAppController.cs";
string code = File.ReadAllText(path);

string badBlock = @"        app.MapGet(""/api/fear-greed"", async (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = ""no-cache, no-store, must-revalidate"";
            var (isAuthorized, authError) = await AuthService.IsRequestAuthorized(context);
            if (!isAuthorized)
                return Results.Json(new { error = authError }, statusCode: 401);

            var fng = await GetFearGreedIndex();
            return Results.Json(fng);
            mlServiceUrl = string.Empty;
        
        MLPythonService.Init(mlServiceUrl);";

string goodBlock = @"        app.MapGet(""/api/fear-greed"", async (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = ""no-cache, no-store, must-revalidate"";
            var (isAuthorized, authError) = await AuthService.IsRequestAuthorized(context);
            if (!isAuthorized)
                return Results.Json(new { error = authError }, statusCode: 401);

            var fng = await GetFearGreedIndex();
            return Results.Json(fng);
        });

        /* ─── Postback Endpoint ─── */
        app.MapGet(""/api/postback"", async (HttpContext context) =>
        {
            var query = context.Request.Query;
            
            // SECURITY: Verify Postback Secret
            string expectedSecret = Environment.GetEnvironmentVariable(""POSTBACK_SECRET"") ?? ""test_secret_123"";
            string providedSecret = query.TryGetValue(""secret"", out var secVal) ? secVal.ToString().Trim() : """";
            
            if (string.IsNullOrEmpty(providedSecret) || providedSecret != expectedSecret)
            {
                BotLogger.Warn($""[Security] Unauthorized postback attempt blocked (Invalid Secret). IP: {context.Connection.RemoteIpAddress}"");
                return Results.Unauthorized();
            }

            string pocketId = query.TryGetValue(""pocketId"", out var pVal) ? pVal.ToString().Trim() : """";
            string status = query.TryGetValue(""status"", out var sVal) ? sVal.ToString().Trim().ToLower() : """";
            
            double deposit = 0;
            if (query.TryGetValue(""deposit"", out var dVal))
            {
                double.TryParse(dVal.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out deposit);
            }

            long chatId = 0;
            if (query.TryGetValue(""chatId"", out var cVal))
            {
                long.TryParse(cVal.ToString(), out chatId);
            }

            if (string.IsNullOrEmpty(pocketId))
            {
                return Results.BadRequest(new { success = false, error = ""pocketId is required"" });
            }

            BotLogger.Info($""[Postback 🔒] Verified Postback: pocketId={pocketId}, chatId={chatId}, status={status}, deposit={deposit}"");

            await TelegramBotService.ProcessPostback(chatId, pocketId, status, deposit);

            return Results.Ok(new { success = true, message = ""Postback processed successfully"" });
        });

        string? mlServiceUrl = builder.Configuration[""MLService:BaseUrl""];
        if (string.IsNullOrWhiteSpace(mlServiceUrl))
            mlServiceUrl = Environment.GetEnvironmentVariable(""ML_SERVICE_URL"");
        if (string.IsNullOrWhiteSpace(mlServiceUrl))
            mlServiceUrl = string.Empty;
        
        MLPythonService.Init(mlServiceUrl);";

code = code.Replace(badBlock, goodBlock);
File.WriteAllText(path, code);
Console.WriteLine(""Done fixing MiniAppController."");