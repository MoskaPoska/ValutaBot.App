using System.IO;

string consPath = @"MiniApp\Engines\ConsensusEngine.cs";
string consContent = File.ReadAllText(consPath);

string targetParams = @"double wfWeightMultiplier = 1.0)";
string replaceParams = @"double wfWeightMultiplier = 1.0,
        System.ReadOnlySpan<double> prices = default)";

consContent = consContent.Replace(targetParams, replaceParams);
File.WriteAllText(consPath, consContent);
Console.WriteLine("Success");
