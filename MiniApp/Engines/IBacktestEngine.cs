using System.Threading.Tasks;

namespace ValutaBot.MiniApp;

public interface IBacktestEngine
{
    Task RunBacktestAsync(string asset, string timeframe, int limit = 1000);
}
