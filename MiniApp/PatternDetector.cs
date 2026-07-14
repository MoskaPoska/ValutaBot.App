namespace ValutaBot.MiniApp;

public static class PatternDetector
{
    public static List<string> DetectPatterns(MiniAppController.OhlcCandle[] candles)
    {
        var patterns = new List<string>();
        if (candles == null || candles.Length < 1) return patterns;

        int last = candles.Length - 1;

        // Pin bar / Hammer / Shooting star
        if (IsPinBar(candles[last]))
            patterns.Add(CandleName(candles[last], "PIN_BAR"));

        // Doji
        if (IsDoji(candles[last]))
            patterns.Add("DOJI");

        // Bullish / Bearish Engulfing
        if (last >= 1 && IsEngulfing(candles[last - 1], candles[last], out bool bullish))
            patterns.Add(bullish ? "ENGULFING_bullish" : "ENGULFING_bearish");

        // Inside bar
        if (last >= 1 && IsInsideBar(candles[last - 1], candles[last]))
            patterns.Add("INSIDE_BAR");

        // Morning / Evening Star (3 candles)
        if (last >= 2)
        {
            if (IsMorningStar(candles[last - 2], candles[last - 1], candles[last]))
                patterns.Add("MORNING_STAR_bullish");
            if (IsEveningStar(candles[last - 2], candles[last - 1], candles[last]))
                patterns.Add("EVENING_STAR_bearish");
        }

        // Three Soldiers / Three Crows (last 3 candles in a row)
        if (last >= 2 && ThreeSoldiers(candles[last - 2], candles[last - 1], candles[last]))
            patterns.Add("THREE_SOLDIERS_bullish");
        if (last >= 2 && ThreeCrows(candles[last - 2], candles[last - 1], candles[last]))
            patterns.Add("THREE_CROWS_bearish");

        // Marubozu (no or very small wicks)
        if (IsMarubozu(candles[last]))
            patterns.Add(CandleName(candles[last], "MARUBOZU"));

        // Hammer / Hanging Man (small body, long lower wick)
        if (IsHammer(candles[last], out bool hammerBullish))
            patterns.Add(hammerBullish ? "HAMMER" : "HANGING_MAN");

        return patterns;
    }

    public static (double[] supports, double[] resistances) CalculateLevels(double[] prices, int swingLookback = 10)
    {
        var supports = new List<double>();
        var resistances = new List<double>();
        if (prices == null || prices.Length < swingLookback * 2) return ([], []);

        double currentPrice = prices[^1];
        if (currentPrice <= 0) return ([], []);

        // Swing highs / lows
        for (int i = swingLookback; i < prices.Length - swingLookback; i++)
        {
            bool isSwingHigh = true;
            bool isSwingLow = true;
            for (int j = i - swingLookback; j <= i + swingLookback; j++)
            {
                if (j < 0 || j >= prices.Length) continue;
                if (prices[j] > prices[i]) isSwingHigh = false;
                if (prices[j] < prices[i]) isSwingLow = false;
            }
            if (isSwingHigh && prices[i] < currentPrice * 1.1)
                resistances.Add(prices[i]);
            if (isSwingLow && prices[i] > currentPrice * 0.9)
                supports.Add(prices[i]);
        }

        // Psychological levels (round numbers)
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(currentPrice)) - 1);
        double roundedBase = Math.Floor(currentPrice / magnitude) * magnitude;
        for (double level = roundedBase - magnitude * 2; level <= roundedBase + magnitude * 2; level += magnitude)
        {
            if (level < currentPrice) supports.Add(level);
            else if (level > currentPrice) resistances.Add(level);
        }

        // Nearest big round (e.g. 1.1000, 64000, etc.)
        double bigMag = Math.Pow(10, Math.Floor(Math.Log10(currentPrice)));
        double bigLevel = Math.Round(currentPrice / bigMag) * bigMag;
        if (bigLevel < currentPrice) supports.Add(bigLevel);
        else if (bigLevel > currentPrice) resistances.Add(bigLevel);

        supports = supports.Distinct().OrderByDescending(s => s).Take(4).ToList();
        resistances = resistances.Distinct().OrderBy(r => r).Take(4).ToList();

        return (supports.ToArray(), resistances.ToArray());
    }

    // ─── Individual pattern checks ───

    private static bool IsPinBar(MiniAppController.OhlcCandle c)
    {
        double body = Math.Abs(c.Close - c.Open);
        double range = c.High - c.Low;
        if (range < 1e-10) return false;
        double upperWick = c.High - Math.Max(c.Open, c.Close);
        double lowerWick = Math.Min(c.Open, c.Close) - c.Low;
        return body / range < 0.3 && (upperWick / range > 0.5 || lowerWick / range > 0.5);
    }

    private static bool IsDoji(MiniAppController.OhlcCandle c)
    {
        double body = Math.Abs(c.Close - c.Open);
        double range = c.High - c.Low;
        return range > 1e-10 && body / range < 0.1;
    }

    private static bool IsEngulfing(MiniAppController.OhlcCandle prev, MiniAppController.OhlcCandle curr, out bool bullish)
    {
        double prevBody = Math.Abs(prev.Close - prev.Open);
        double currBody = Math.Abs(curr.Close - curr.Open);
        bool prevBullish = prev.Close > prev.Open;
        bool currBullish = curr.Close > curr.Open;
        bullish = currBullish && !prevBullish && currBody > prevBody * 1.1;
        bool bearish = !currBullish && prevBullish && currBody > prevBody * 1.1;
        return bullish || bearish;
    }

    private static bool IsInsideBar(MiniAppController.OhlcCandle prev, MiniAppController.OhlcCandle curr)
    {
        return curr.High <= prev.High && curr.Low >= prev.Low;
    }

    private static bool IsMorningStar(MiniAppController.OhlcCandle c1, MiniAppController.OhlcCandle c2, MiniAppController.OhlcCandle c3)
    {
        // Bearish candle, small middle candle, bullish candle closing above halfway of c1
        bool firstBearish = c1.Close < c1.Open;
        bool smallBody = Math.Abs(c2.Close - c2.Open) < Math.Abs(c1.Close - c1.Open) * 0.5;
        bool thirdBullish = c3.Close > c3.Open;
        bool closesAboveHalf = c3.Close > c1.Open + (c1.Close - c1.Open) * 0.5;
        return firstBearish && smallBody && thirdBullish && closesAboveHalf;
    }

    private static bool IsEveningStar(MiniAppController.OhlcCandle c1, MiniAppController.OhlcCandle c2, MiniAppController.OhlcCandle c3)
    {
        bool firstBullish = c1.Close > c1.Open;
        bool smallBody = Math.Abs(c2.Close - c2.Open) < Math.Abs(c1.Close - c1.Open) * 0.5;
        bool thirdBearish = c3.Close < c3.Open;
        bool closesBelowHalf = c3.Close < c1.Open + (c1.Close - c1.Open) * 0.5;
        return firstBullish && smallBody && thirdBearish && closesBelowHalf;
    }

    private static bool ThreeSoldiers(MiniAppController.OhlcCandle c1, MiniAppController.OhlcCandle c2, MiniAppController.OhlcCandle c3)
    {
        return c1.Close > c1.Open && c2.Close > c2.Open && c3.Close > c3.Open
            && c2.Close > c1.Close && c3.Close > c2.Close
            && c2.Open > c1.Open && c3.Open > c2.Open;
    }

    private static bool ThreeCrows(MiniAppController.OhlcCandle c1, MiniAppController.OhlcCandle c2, MiniAppController.OhlcCandle c3)
    {
        return c1.Close < c1.Open && c2.Close < c2.Open && c3.Close < c3.Open
            && c2.Close < c1.Close && c3.Close < c2.Close
            && c2.Open < c1.Open && c3.Open < c2.Open;
    }

    private static bool IsMarubozu(MiniAppController.OhlcCandle c)
    {
        double body = Math.Abs(c.Close - c.Open);
        double range = c.High - c.Low;
        if (range < 1e-10) return false;
        double upperWick = c.High - Math.Max(c.Open, c.Close);
        double lowerWick = Math.Min(c.Open, c.Close) - c.Low;
        return body / range > 0.9 && upperWick / range < 0.05 && lowerWick / range < 0.05;
    }

    private static bool IsHammer(MiniAppController.OhlcCandle c, out bool bullish)
    {
        double body = Math.Abs(c.Close - c.Open);
        double range = c.High - c.Low;
        if (range < 1e-10) { bullish = false; return false; }
        double upperWick = c.High - Math.Max(c.Open, c.Close);
        double lowerWick = Math.Min(c.Open, c.Close) - c.Low;
        bullish = c.Close > c.Open;
        return body / range < 0.4 && lowerWick / range > 0.5 && upperWick / range < 0.3;
    }

    private static string CandleName(MiniAppController.OhlcCandle c, string baseName)
    {
        bool bullish = c.Close > c.Open;
        // For pin bar: bullish pin = hammer, bearish pin = shooting star
        if (baseName == "PIN_BAR")
        {
            double upperWick = c.High - Math.Max(c.Open, c.Close);
            double lowerWick = Math.Min(c.Open, c.Close) - c.Low;
            return upperWick > lowerWick ? "SHOOTING_STAR_bearish" : "PIN_BAR_bullish";
        }
        if (baseName == "MARUBOZU")
            return bullish ? "MARUBOZU_bullish" : "MARUBOZU_bearish";
        return baseName;
    }
}
