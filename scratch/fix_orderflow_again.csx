using System;
using System.IO;
using System.Text.RegularExpressions;

string path = @"MiniApp\Engines\OrderFlowEngine.cs";
string content = File.ReadAllText(path);

string header = @"using System;
using System.Linq;

namespace ValutaBot.MiniApp;

/// <summary>
/// Order Flow & Volume Delta Imbalance Engine for Forex & OTC market pairs.
/// Filters out HFT micro-noise, TWAP/VWAP algorithmic noise, and Spoofing traps.
/// Focuses exclusively on Institutional Block Trades, Volume Cluster Anomalies, and Real Momentum Progress.
/// </summary>
public static class OrderFlowEngine
{
    public record OrderFlowResult(
        double BuyVolume,
        double SellVolume,
        double DeltaRatio,
        double CumulativeVolumeDelta,
        string OrderFlowState,
        double ScoreContribution,
        bool IsInstitutionalBlockTrade,
        string Description
    );

    public static OrderFlowResult AnalyzeOrderFlow(";

content = Regex.Replace(content, @"using System;.*?public static OrderFlowResult AnalyzeOrderFlow\(", header, RegexOptions.Singleline);
File.WriteAllText(path, content);
Console.WriteLine("Fixed OrderFlowEngine again");
