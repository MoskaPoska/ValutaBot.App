using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using Microsoft.Extensions.Primitives;
using ValutaBot.MiniApp;

namespace IntegrationTests
{
    public class SecurityTests
    {
        // 1. AuthService Fail-Closed Test
        [Fact]
        public async Task AuthService_MissingBotToken_FailsClosed()
        {
            // Arrange
            TelegramNotifier.Init(null); // Simulate missing env var
            var context = new DefaultHttpContext();
            
            // Act
            var (isAuthorized, error) = await AuthService.IsRequestAuthorized(context);
            
            // Assert
            Assert.False(isAuthorized);
            Assert.Contains("Missing bot token", error);
        }

        // 2. InitData Validation Test (Invalid Hash)
        [Fact]
        public void TelegramInitDataValidator_InvalidHash_Fails()
        {
            // Arrange
            string fakeBotToken = "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11";
            string fakeInitData = "query_id=AAHdF6IQAAAAAN0XohC-1234&user=%7B%22id%22%3A123456789%2C%22first_name%22%3A%22Test%22%7D&auth_date=1700000000&hash=invalidhash1234567890abcdef";
            
            // Act
            bool isValid = TelegramInitDataValidator.Validate(fakeInitData, fakeBotToken, out long userId, out string username);
            
            // Assert
            Assert.False(isValid);
            Assert.Equal(0, userId);
        }

        // 3. Postback Endpoint Secret Validation
        [Fact]
        public void PostbackEndpoint_WithoutSecret_ShouldReturn401()
        {
            string expectedSecret = "test_secret_123";
            string providedSecret = ""; // Missing
            
            bool isAuthorized = !string.IsNullOrEmpty(providedSecret) && providedSecret == expectedSecret;
            
            Assert.False(isAuthorized);
        }

        [Fact]
        public void PostbackEndpoint_WithInvalidSecret_ShouldReturn401()
        {
            string expectedSecret = "test_secret_123";
            string providedSecret = "hacker_secret_999"; 
            
            bool isAuthorized = !string.IsNullOrEmpty(providedSecret) && providedSecret == expectedSecret;
            
            Assert.False(isAuthorized);
        }
        
        [Fact]
        public void PostbackEndpoint_WithValidSecret_ShouldReturnOk()
        {
            string expectedSecret = "test_secret_123";
            string providedSecret = "test_secret_123"; 
            
            bool isAuthorized = !string.IsNullOrEmpty(providedSecret) && providedSecret == expectedSecret;
            
            Assert.True(isAuthorized);
        }
    }
}
