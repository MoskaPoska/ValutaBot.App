using System;
using System.IO;
using System.Text.RegularExpressions;

string consPath = @"MiniApp\Engines\ConsensusEngine.cs";
string cons = File.ReadAllText(consPath);

// Remove unused parameters
cons = cons.Replace("        double scoreSign,\r\n        string claudeDirection,\r\n        \r\n", "");
cons = cons.Replace("        string mlDirection,\r\n        double mlConfidence,\r\n", "");

// Remove ML logic
cons = cons.Replace("        double normMl   = Math.Max(0, (mlConfidence - 0.5) * 2.0);\r\n", "");
cons = cons.Replace("        double scoreMl   = mlDirection   == \"BUY\" ? normMl   : mlDirection   == \"PUT\" ? -normMl   : 0;\r\n", "");
cons = cons.Replace("        double activeWeightMl   = (mlDirection   == \"BUY\" || mlDirection   == \"PUT\") ? weightMl   * wfWeightMultiplier : 0;\r\n", "");
cons = cons.Replace(" + scoreMl * activeWeightMl", "");
cons = cons.Replace(" + activeWeightMl", "");

// Fix string formatting
cons = cons.Replace("mlDirection == \"BUY\"", "lgbmDirection == \"BUY\"");
cons = cons.Replace("mlDirection == \"PUT\"", "lgbmDirection == \"PUT\"");
cons = cons.Replace("mlConfidence", "lgbmConfidence");

File.WriteAllText(consPath, cons);
Console.WriteLine(""Consensus Cleaned"");
