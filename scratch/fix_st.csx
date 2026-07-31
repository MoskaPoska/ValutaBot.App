using System.IO;
using System.Text.RegularExpressions;

string trackerPath = @"MiniApp\Services\SignalTracker.cs";
string trackerContent = File.ReadAllText(trackerPath);

string newMethod = @"
    public static double CalculateSignalWeight(System.Collections.Generic.IEnumerable<(string signalName, int correct, int verified)> votes, string signalName, double baseWeight = 1.0)
    {
        var v = System.Linq.Enumerable.FirstOrDefault(votes, x => x.signalName == signalName);
        if (v.verified < 5) return baseWeight;
        double agreeRate = (double)v.correct / v.verified;
        double adjustment = agreeRate / 0.5;
        return System.Math.Clamp(baseWeight * adjustment, 0.2, 2.0);
    }

    public static async Task<double> GetSignalWeightAsync(string signalName, double baseWeight = 1.0)
    {
        var votes = await BotDatabase.GetAllSignalVotesAsync();
        return CalculateSignalWeight(votes, signalName, baseWeight);
    }
";

trackerContent = Regex.Replace(trackerContent, @"public static async Task<double> GetSignalWeightAsync[\s\S]*?\}\s*\}", newMethod.Trim() + "\n\n");
File.WriteAllText(trackerPath, trackerContent);
