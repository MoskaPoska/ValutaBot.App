using System.IO;

string path = @"MiniApp\Engines\ContinuousStateEngine.cs";
string content = File.ReadAllText(path);

content = content.Replace("public static ContinuousStateResult EvaluateContinuousState(double[] prices", "public static ContinuousStateResult EvaluateContinuousState(ReadOnlySpan<double> prices");
content = content.Replace("private static double FilterKalmanContinuous(double[] prices", "private static double FilterKalmanContinuous(ReadOnlySpan<double> prices");
content = content.Replace("foreach (double p in prices)", "for(int i = 0; i < prices.Length; i++) { double p = prices[i];");
content = content.Replace("state = (est, err);", "state = (est, err); }");

File.WriteAllText(path, content);
Console.WriteLine("Success");
