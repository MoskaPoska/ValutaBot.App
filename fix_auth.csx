using System.IO;

var lines = File.ReadAllText("MiniApp/Services/AuthService.cs");
lines = lines.Replace("public static async Task<bool> IsRequestAuthorized(HttpContext context, out string? errorMessage)", "public static async Task<(bool isAuthorized, string? errorMessage)> IsRequestAuthorized(HttpContext context)");
lines = lines.Replace("errorMessage = null;", "string? errorMessage = null;");
lines = lines.Replace("return true;", "return (true, null);");
lines = lines.Replace("return false;", "return (false, errorMessage);");

File.WriteAllText("MiniApp/Services/AuthService.cs", lines);

var mvc = File.ReadAllText("MiniApp/Controllers/MiniAppController.cs");
mvc = mvc.Replace("if (!await AuthService.IsRequestAuthorized(context, out string? authError))", "var (isAuthorized, authError) = await AuthService.IsRequestAuthorized(context);\n            if (!isAuthorized)");

File.WriteAllText("MiniApp/Controllers/MiniAppController.cs", mvc);
