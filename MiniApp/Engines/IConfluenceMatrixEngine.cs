using System.Threading.Tasks;

namespace ValutaBot.MiniApp;

public interface IConfluenceMatrixEngine
{
    Task<ConfluenceMatrixResult> Evaluate4DMatrixAsync(
        string asset,
        string primaryTimeframe,
        bool isForex = false,
        string? binanceSymbol = null);
}
