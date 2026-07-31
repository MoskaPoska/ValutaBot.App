using System.IO;

string path = @"MiniApp\Engines\OrderFlowEngine.cs";
string content = File.ReadAllText(path);

content = content.Replace("double DeltaRatio,", "double DeltaRatio,\n        double CumulativeVolumeDelta,");

string search = "bool hasInstitutionalBlockTrade = false;";
string replace = search + "\n        double cumulativeVolumeDelta = 0;";
content = content.Replace(search, replace);

string buyLogicCandles = "totalBuyVol += totalVol * buyRatio;\n                    totalSellVol += totalVol * sellRatio;";
string replaceBuyLogicCandles = buyLogicCandles + "\n                    cumulativeVolumeDelta += (totalVol * buyRatio) - (totalVol * sellRatio);";
content = content.Replace(buyLogicCandles, replaceBuyLogicCandles);

string noCandlesBuy = "if (priceDiff > 0) totalBuyVol += vol;\n                else if (priceDiff < 0) totalSellVol += vol;";
string replaceNoCandlesBuy = "if (priceDiff > 0) { totalBuyVol += vol; cumulativeVolumeDelta += vol; }\n                else if (priceDiff < 0) { totalSellVol += vol; cumulativeVolumeDelta -= vol; }";
content = content.Replace(noCandlesBuy, replaceNoCandlesBuy);

string ret1 = "return new OrderFlowResult(0, 0, 1.0, \"BALANCED\", 0, false, \"Ќедостаточно свечей дл€ анализа потока ордеров.\");";
string replaceRet1 = "return new OrderFlowResult(0, 0, 1.0, 0, \"BALANCED\", 0, false, \"Ќедостаточно свечей дл€ анализа потока ордеров.\");";
content = content.Replace(ret1, replaceRet1);

string ret2 = "return new OrderFlowResult(\n            BuyVolume: Math.Round(totalBuyVol, 2),\n            SellVolume: Math.Round(totalSellVol, 2),\n            DeltaRatio: Math.Round(deltaRatio, 2),\n            OrderFlowState: state,\n            ScoreContribution: Math.Round(scoreContribution, 2),\n            IsInstitutionalBlockTrade: hasInstitutionalBlockTrade,\n            Description: desc\n        );";
string replaceRet2 = "return new OrderFlowResult(\n            BuyVolume: Math.Round(totalBuyVol, 2),\n            SellVolume: Math.Round(totalSellVol, 2),\n            DeltaRatio: Math.Round(deltaRatio, 2),\n            CumulativeVolumeDelta: Math.Round(cumulativeVolumeDelta, 2),\n            OrderFlowState: state,\n            ScoreContribution: Math.Round(scoreContribution, 2),\n            IsInstitutionalBlockTrade: hasInstitutionalBlockTrade,\n            Description: desc\n        );";
content = content.Replace(ret2, replaceRet2);

string spoofLogic = "if (deltaRatio > 1.8 && Math.Abs(priceDelta) < 1e-7)";
string replaceSpoofLogic = "if (deltaRatio > 1.8 && cumulativeVolumeDelta > 0 && priceDelta < -1e-9)\n        {\n            state = \"BEARISH_ABSORPTION\";\n            scoreContribution = -0.30;\n            desc = \"CVD Divergence (Bearish Absorption):  рупный лимитный продавец поглощает рыночные покупки.\";\n        }\n        else if (deltaRatio < 0.55 && cumulativeVolumeDelta < 0 && priceDelta > 1e-9)\n        {\n            state = \"BULLISH_ABSORPTION\";\n            scoreContribution = 0.30;\n            desc = \"CVD Divergence (Bullish Absorption):  рупный лимитный покупатель поглощает рыночные продажи.\";\n        }\n        else if (deltaRatio > 1.8 && Math.Abs(priceDelta) < 1e-7)";

content = content.Replace(spoofLogic, replaceSpoofLogic);

File.WriteAllText(path, content);
Console.WriteLine("Success");
