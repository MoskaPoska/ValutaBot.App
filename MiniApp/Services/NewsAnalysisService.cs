namespace ValutaBot.MiniApp;

public static class NewsAnalysisService
{
    public static (double score, string sentiment, string summary, string[] headlines) Analyze(string asset)
    {
        return (0, "Нейтральный", "Анализ новостей отключен", System.Array.Empty<string>());
    }
}
