using System.IO;

string path = @"MiniApp\Engines\AutoCalibrationEngine.cs";
string content = File.ReadAllText(path);

content = content.Replace("public static MarketRegime DetectMarketRegime(double adx, double volRatio, double rsi)", "public static MarketRegime DetectMarketRegime(double adx, double volRatio, double rsi, ReadOnlySpan<double> prices = default)");
content = content.Replace("if (volRatio > 1.75 || Math.Abs(rsi - 50.0) > 28.0)", "double entropy = prices.IsEmpty ? 0.5 : MathIndicatorsLibrary.CalculateShannonEntropy(prices);\n\n        if (entropy > 0.90 || volRatio > 1.75 || Math.Abs(rsi - 50.0) > 28.0)");
content = content.Replace("if (adx >= 25.0 && volRatio <= 2.0)", "if (adx >= 25.0 && volRatio <= 2.0 && entropy < 0.75)");

content = content.Replace("double defaultBaseWeight = 1.0)", "double defaultBaseWeight = 1.0,\n        ReadOnlySpan<double> prices = default)");
content = content.Replace("var regime = DetectMarketRegime(adx, volRatio, rsi);", "var regime = DetectMarketRegime(adx, volRatio, rsi, prices);");

File.WriteAllText(path, content);

string consPath = @"MiniApp\Engines\ConsensusEngine.cs";
string consContent = File.ReadAllText(consPath);
consContent = consContent.Replace("double rsiVal)", "double rsiVal,\n        ReadOnlySpan<double> prices = default)");
consContent = consContent.Replace("1.8);", "1.8, prices);");
consContent = consContent.Replace("1.2);", "1.2, prices);");
consContent = consContent.Replace("2.2);", "2.2, prices);");
consContent = consContent.Replace("1.0);", "1.0, prices);");
File.WriteAllText(consPath, consContent);

string ctxPath = @"MiniApp\CQRS\Handlers\MarketAnalysisContext.cs";
string ctxContent = File.ReadAllText(ctxPath);
ctxContent = ctxContent.Replace("rsiVal)", "rsiVal, _prices)");
File.WriteAllText(ctxPath, ctxContent);

Console.WriteLine("Success");
