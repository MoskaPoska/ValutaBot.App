using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using ValutaBot.App.MiniApp.Services; // adjust namespace if needed
using System.Linq;

namespace Benchmarks;

public class TechnicalAnalysisBenchmarks
{
    private readonly TechnicalAnalysisEngine _engine = new();
    private readonly double[] _prices = Enumerable.Range(1, 1000).Select(i => (double)i).ToArray();

    [Benchmark]
    public double ComputeRsi()
    {
        // Assuming TechnicalAnalysisEngine has a method CalculateRsi(double[] prices, int period)
        return _engine.CalculateRsi(_prices, period: 14);
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        var summary = BenchmarkRunner.Run<TechnicalAnalysisBenchmarks>();
        System.Console.WriteLine(summary);
    }
}

