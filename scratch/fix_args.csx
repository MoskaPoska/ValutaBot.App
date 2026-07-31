using System.IO;
using System.Text.RegularExpressions;

string marketPath = @"MiniApp\CQRS\Handlers\MarketAnalysisContext.cs";
string marketContent = File.ReadAllText(marketPath);
marketContent = marketContent.Replace("_handler._fetcher.FetchBinanceWithFallback(_symbol, _mainInterval, _clean, _limit, 10)", "_handler._fetcher.FetchBinanceWithFallback(_symbol, _mainInterval, _clean, _limit)");
File.WriteAllText(marketPath, marketContent);

string trackerPath = @"MiniApp\Services\SignalTracker.cs";
string trackerContent = File.ReadAllText(trackerPath);
trackerContent = trackerContent.Replace("private static async Task VerifyPendingAsync()", "public static async Task VerifyPendingAsync()");
File.WriteAllText(trackerPath, trackerContent);
