using System.IO;
var lines = File.ReadAllText("MiniApp/Services/AuthService.cs");
lines = lines.Replace("string? string? errorMessage", "string? errorMessage");
File.WriteAllText("MiniApp/Services/AuthService.cs", lines);
