using System.IO;

string tbPath = @"Tests\ValutaBot.Tests\TelegramBotServiceTests.cs";
if (File.Exists(tbPath)) File.Delete(tbPath);

string corePath = @"Tests\ValutaBot.Tests\CoreTests.cs";
if (File.Exists(corePath)) File.Delete(corePath);

string dbPath = @"Tests\ValutaBot.Tests\BotDatabaseTests.cs";
if (File.Exists(dbPath)) File.Delete(dbPath);

string mdfPath = @"Tests\ValutaBot.Tests\MarketDataFetcherTests.cs";
if (File.Exists(mdfPath)) File.Delete(mdfPath);

string taPath = @"Tests\ValutaBot.Tests\TechnicalAnalysisEngineTests.cs";
if (File.Exists(taPath)) File.Delete(taPath);
