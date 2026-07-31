using MathNet.Numerics.Distributions;
using System;
Span<double> samples = stackalloc double[100];
Normal.Samples(Random.Shared, samples, 0, 1);
Console.WriteLine("Success");
