using System.IO;
using System.Text.RegularExpressions;

string consensusPath = @"MiniApp\Engines\ConsensusEngine.cs";
string consensusContent = File.ReadAllText(consensusPath);
consensusContent = consensusContent.Replace("int claudeProbability,", "");
File.WriteAllText(consensusPath, consensusContent);

string marketPath = @"MiniApp\CQRS\Handlers\MarketAnalysisContext.cs";
string marketContent = File.ReadAllText(marketPath);
marketContent = Regex.Replace(marketContent, @"ConsensusEngine\.EvaluateConsensus\(\s*totalScore,\s*scoreSign,\s*claudeDirection,\s*claudeProbability,\s*claudeReasoningText", "ConsensusEngine.EvaluateConsensus(totalScore, scoreSign, claudeDirection, claudeReasoningText");
File.WriteAllText(marketPath, marketContent);
