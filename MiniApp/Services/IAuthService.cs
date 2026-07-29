namespace ValutaBot.MiniApp;

public interface IAuthService
{
    bool IsRequestAuthorized(Microsoft.AspNetCore.Http.HttpContext context, out string? errorMessage);
    bool IsRateLimited(Microsoft.AspNetCore.Http.HttpContext context, out string? errorMessage);
    string GetSignedWebAppUrl(long chatId, string webAppUrl, string token);
}
