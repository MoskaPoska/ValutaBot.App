using System.IO;

string path = @"MiniApp\Engines\MathIndicatorsLibrary.cs";
string content = File.ReadAllText(path);

// We will just use string index of "public static double[] ComputeKalmanFilter" and delete until the end of the file, because the 3 unused functions are at the bottom of the file!
int idx = content.IndexOf("public static double[] ComputeKalmanFilter");
if (idx != -1) {
    content = content.Substring(0, idx) + "\n}\n";
    File.WriteAllText(path, content);
}
