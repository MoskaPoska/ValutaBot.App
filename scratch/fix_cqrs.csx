using System.IO;
using System.Text.RegularExpressions;

string marketPath = @"MiniApp\CQRS\Handlers\MarketAnalysisContext.cs";
string marketContent = File.ReadAllText(marketPath);

marketContent = marketContent.Replace("private MiniAppController.OhlcCandle[]? _ohlcCandles;", "private MiniAppController.OhlcCandle[]? _ohlcCandles;\n    private System.Collections.Generic.List<(string signalName, int correct, int verified)> _signalVotes;");

marketContent = marketContent.Replace("await Task.WhenAll(higherTask, lowerTask);", "var votesTask = BotDatabase.GetAllSignalVotesAsync();\n        await Task.WhenAll(higherTask, lowerTask, votesTask);\n        _signalVotes = await votesTask;");

// Fix garbled strings BEFORE replacing the calls
marketContent = marketContent.Replace("??????????", "Indicators");
marketContent = marketContent.Replace("???????", "News");

// Replace GetSignalWeightAsync with CalculateSignalWeight
marketContent = Regex.Replace(marketContent, @"await SignalTracker\.GetSignalWeightAsync\((.*?), (.*?)\)", "SignalTracker.CalculateSignalWeight(_signalVotes, , )");

File.WriteAllText(marketPath, marketContent);
