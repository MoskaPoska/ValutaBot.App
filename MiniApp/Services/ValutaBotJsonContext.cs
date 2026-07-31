using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace ValutaBot.MiniApp
{
    [JsonSourceGenerationOptions(NumberHandling = JsonNumberHandling.AllowReadingFromString)]
    [JsonSerializable(typeof(double[][]))]
    [JsonSerializable(typeof(ValutaBot.MiniApp.TwelveDataService.TwelveDataResponse))]
    [JsonSerializable(typeof(ValutaBot.MiniApp.TwelveDataService.TwelveDataPriceResponse))]
    [JsonSerializable(typeof(ValutaBot.MiniApp.MLPythonService.PredictResponseDto))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    internal partial class ValutaBotJsonContext : JsonSerializerContext
    {
    }
}
