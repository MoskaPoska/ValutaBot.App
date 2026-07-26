// TelegramBotIntegrationTests.cs – validates URL injection with WireMock
using System.Net;
using System.Threading.Tasks;
using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;
using ValutaBot.App.MiniApp.Telegram;

namespace ValutaBot.Tests.Integration
{
    public class TelegramBotIntegrationTests : IAsyncLifetime
    {
        private WireMockServer _server = null!;

        public Task InitializeAsync()
        {
            _server = WireMockServer.Start();
            // Mock Telegram sendMessage endpoint
            _server.Given(Request.Create()
                .WithPath("/bot*/sendMessage")
                .UsingPost())
                .RespondWith(Response.Create()
                    .WithStatusCode(HttpStatusCode.OK)
                    .WithBody("{\"ok\":true,\"result\":{}}"));
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            _server.Stop();
            _server.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public async Task SendMessage_UsesMockServer()
        {
            // Arrange – redirect service to mock server
            var original = TelegramBotService.GetBaseUrl();
            TelegramBotService.SetBaseUrl(_server.Urls[0]);

            // Act – call internal SendMessage method (exposed via internal for testing)
            await TelegramBotService.SendMessage("dummy-token", 12345, "test message");

            // Assert – request should have hit mock server
            Assert.True(_server.LogEntries.Count > 0);

            // Cleanup
            TelegramBotService.SetBaseUrl(original);
        }
    }
}
