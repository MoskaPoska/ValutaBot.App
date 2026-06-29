using System.Collections.Concurrent;

namespace ValutaBot.MiniApp;

public static class SignalTracker
{
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<bool>> _signalHistory = new();
    private static readonly ConcurrentQueue<PredictionRecord> _pending = new();
    private static readonly ConcurrentQueue<PredictionRecord> _archive = new();
    private static int _totalPredictions, _correctPredictions;

    public static void RecordSignalVote(string signalName, bool agreedWithFinal)
    {
        var q = _signalHistory.GetOrAdd(signalName, _ => new ConcurrentQueue<bool>());
        q.Enqueue(agreedWithFinal);
        while (q.Count > 100) q.TryDequeue(out _);
    }

    public static void RecordPrediction(string direction, string asset, string timeframe, double price)
    {
        _pending.Enqueue(new PredictionRecord
        {
            Direction = direction,
            Asset = asset,
            Timeframe = timeframe,
            Price = price,
            CreatedAt = DateTime.UtcNow,
            Checked = false
        });
        Interlocked.Increment(ref _totalPredictions);
    }

    public static PredictionRecord[] GetPending() => _pending.Where(p => !p.Checked).ToArray();

    public static void MarkChecked(PredictionRecord record, bool correct)
    {
        record.Checked = true;
        record.WasCorrect = correct;
        _archive.Enqueue(record);
        if (correct) Interlocked.Increment(ref _correctPredictions);
        while (_archive.Count > 500) _archive.TryDequeue(out _);
    }

    public static double GetSignalWeight(string signalName, double baseWeight = 1.0)
    {
        if (!_signalHistory.TryGetValue(signalName, out var q) || q.Count < 10)
            return baseWeight;
        var arr = q.ToArray();
        double agreeRate = arr.Count(v => v) / (double)arr.Length;
        double adjustment = agreeRate / 0.5;
        return Math.Clamp(baseWeight * adjustment, 0.3, 2.0);
    }

    public static double GetOverallAccuracy()
    {
        return _totalPredictions > 0
            ? Math.Round((double)_correctPredictions / _totalPredictions * 100, 1)
            : 0;
    }

    public static int GetTotalPredictions() => _totalPredictions;

    public static (string name, double agreeRate, double weight, int count)[] GetSignalStats()
    {
        return _signalHistory.Select(kv =>
        {
            var arr = kv.Value.ToArray();
            double agreeRate = arr.Length > 0 ? arr.Count(v => v) / (double)arr.Length : 0.5;
            double weight = GetSignalWeight(kv.Key);
            return (name: kv.Key, agreeRate: Math.Round(agreeRate * 100, 1), weight: Math.Round(weight, 2), count: arr.Length);
        }).OrderByDescending(s => s.agreeRate).ToArray();
    }

    public class PredictionRecord
    {
        public string Direction { get; set; } = "";
        public string Asset { get; set; } = "";
        public string Timeframe { get; set; } = "";
        public double Price { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool Checked { get; set; }
        public bool WasCorrect { get; set; }
    }
}
