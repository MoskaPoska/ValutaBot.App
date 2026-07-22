using System.Text;

namespace ValutaBot.MiniApp;

public static class BotLogger
{
    private static readonly object _fileLock = new();
    private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "valuta_bot.log");

    static BotLogger()
    {
        try
        {
            string? dir = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
        catch { /* Fallback to console only if filesystem restricts directory creation */ }
    }

    public static void Info(string message)
    {
        Log("INFO", message);
    }

    public static void Warn(string message, Exception? ex = null)
    {
        Log("WARN", ex != null ? $"{message} | Exception: {ex.Message}" : message);
    }

    public static void Error(string message, Exception? ex = null)
    {
        string fullMsg = ex != null ? $"{message} | Details: {ex.Message}\n{ex.StackTrace}" : message;
        Log("ERR", fullMsg);
    }

    private static void Log(string level, string message)
    {
        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string logLine = $"[{timestamp}] [{level}] {message}";

        Console.WriteLine(logLine);

        try
        {
            lock (_fileLock)
            {
                File.AppendAllText(LogFilePath, logLine + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { /* Prevent logging failures from interrupting application execution */ }
    }
}
