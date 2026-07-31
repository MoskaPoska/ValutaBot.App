using System.IO;

string mdfPath = @"Tests\ValutaBot.Tests\MarketDataFetcherTests.cs";
string mdfContent = File.ReadAllText(mdfPath);
mdfContent = mdfContent.Replace("new MarketDataFetcher(null, null)", "MarketDataFetcher.Instance");
File.WriteAllText(mdfPath, mdfContent);
