using System.IO;
using System.Text.RegularExpressions;

string path = @"MiniApp\Engines\TechnicalAnalysisEngine.cs";
string content = File.ReadAllText(path);
content = content.Replace("quotes.Count()", "data.Length");
content = content.Replace("private IEnumerable<Quote> ConvertToQuotes", "private static IEnumerable<Quote> ConvertToQuotes");
File.WriteAllText(path, content);

string consensusPath = @"MiniApp\Engines\ConsensusEngine.cs";
string consensusContent = File.ReadAllText(consensusPath);
consensusContent = consensusContent.Replace("public ConfluenceMatrixResult EvaluateConsensus(ConfluenceMatrixResult coreResult, double onnxProbability, double claudeProbability)", "public ConfluenceMatrixResult EvaluateConsensus(ConfluenceMatrixResult coreResult, double onnxProbability)");
consensusContent = consensusContent.Replace("double claudeProbability)", ")");
File.WriteAllText(consensusPath, consensusContent);

string matrixPath = @"MiniApp\Engines\ConfluenceMatrixEngine.cs";
string matrixContent = File.ReadAllText(matrixPath);
matrixContent = matrixContent.Replace("private (string micro, string primary, string macro, string global) Resolve4DTimeframes", "private static (string micro, string primary, string macro, string global) Resolve4DTimeframes");
File.WriteAllText(matrixPath, matrixContent);

string autoPath = @"MiniApp\Engines\AutoCalibrationEngine.cs";
string autoContent = File.ReadAllText(autoPath);
autoContent = autoContent.Replace(".ToLower()", ".ToUpperInvariant()");
File.WriteAllText(autoPath, autoContent);
