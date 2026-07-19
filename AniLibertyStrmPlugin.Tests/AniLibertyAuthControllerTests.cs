using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class AniLibertyAuthControllerTests
{
    [Fact]
    public async Task SignInLoginPass_SavesTokenFromSuccessfulResponse()
    {
        using var host = PluginTestHost.Create(cfg =>
        {
            cfg.EnableDebugLogs = true;
            cfg.UiMinLogLevel = Microsoft.Extensions.Logging.LogLevel.Debug;
        });
        var handler = new QueueHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/accounts/users/auth/login", request.RequestUri?.AbsolutePath);

            var json = request.Content?.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult() ?? string.Empty;
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("user@example.test", doc.RootElement.GetProperty("login").GetString());
            Assert.Equal("password", doc.RootElement.GetProperty("password").GetString());

            return Json(HttpStatusCode.OK, """{"token":"jwt-token"}""");
        });
        var controller = NewController(handler);

        var result = await controller.SignInLoginPass(
            new LoginRequest { Mail = "user@example.test", Passwd = "password" },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("jwt-token", host.Plugin.Configuration.AniLibertyToken);
        Assert.Empty(host.Plugin.Configuration.CurrentOtpCode);
        Assert.Contains("SignInLoginPass called.", host.Plugin.Configuration.LastTaskLog);
    }

    [Fact]
    public async Task SignInLoginPass_ReturnsBadRequestForMissingCredentialsAndErrorForMissingToken()
    {
        using var host = PluginTestHost.Create();
        var controller = NewController(new QueueHandler(_ => Json(HttpStatusCode.OK, """{"ok":true}""")));

        Assert.IsType<BadRequestObjectResult>(await controller.SignInLoginPass(null, CancellationToken.None));

        var noToken = await controller.SignInLoginPass(
            new LoginRequest { Mail = "user@example.test", Passwd = "password" },
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(noToken);
        Assert.Equal(200, objectResult.StatusCode);
        Assert.Empty(host.Plugin.Configuration.AniLibertyToken);
    }

    [Fact]
    public async Task StartOtp_CreatesDeviceIdAndParsesOtpObject()
    {
        using var host = PluginTestHost.Create();
        var controller = NewController(new QueueHandler(_ => Json(HttpStatusCode.OK, """{"otp":{"code":"123456"},"remaining_time":120}""")));

        var result = await controller.StartOtp(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.False(string.IsNullOrWhiteSpace(host.Plugin.Configuration.AniDeviceId));
        Assert.Empty(host.Plugin.Configuration.CurrentOtpCode);
    }

    [Fact]
    public async Task StartOtp_ReturnsServerErrorWhenHttpThrows()
    {
        using var host = PluginTestHost.Create();
        var controller = NewController(new QueueHandler(_ => throw new InvalidOperationException("boom")));

        var result = await controller.StartOtp(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task AcceptOtp_ValidatesCodeTokenAndSendsBearerRequest()
    {
        using var host = PluginTestHost.Create();
        var controllerWithoutToken = NewController(new QueueHandler());

        Assert.IsType<BadRequestObjectResult>(await controllerWithoutToken.AcceptOtp(null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controllerWithoutToken.AcceptOtp(new OtpRequest { Code = "abc" }, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controllerWithoutToken.AcceptOtp(new OtpRequest { Code = "123456" }, CancellationToken.None));

        var cfg = host.Plugin.Configuration;
        cfg.AniLibertyToken = "jwt-token";
        host.Plugin.UpdateConfiguration(cfg);

        var handler = new QueueHandler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("jwt-token", request.Headers.Authorization?.Parameter);

            var json = request.Content?.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult() ?? string.Empty;
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(123456, doc.RootElement.GetProperty("code").GetInt32());

            return Json(HttpStatusCode.OK, """{"success":true}""");
        });
        var controller = NewController(handler);

        var result = await controller.AcceptOtp(new OtpRequest { Code = " 123456 " }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task SignInOtp_RequiresDeviceAndSavesReturnedToken()
    {
        using var host = PluginTestHost.Create();
        var noDeviceController = NewController(new QueueHandler());
        Assert.IsType<BadRequestObjectResult>(
            await noDeviceController.SignInOtp(new OtpRequest { Code = "123456" }, CancellationToken.None));

        var cfg = host.Plugin.Configuration;
        cfg.AniDeviceId = "device-id";
        host.Plugin.UpdateConfiguration(cfg);

        var handler = new QueueHandler(request =>
        {
            var json = request.Content?.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult() ?? string.Empty;
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(123456, doc.RootElement.GetProperty("code").GetInt32());
            Assert.Equal("device-id", doc.RootElement.GetProperty("device_id").GetString());

            return Json(HttpStatusCode.OK, """{"token":"otp-token"}""");
        });
        var controller = NewController(handler);

        var result = await controller.SignInOtp(new OtpRequest { Code = "123456" }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("otp-token", host.Plugin.Configuration.AniLibertyToken);
        Assert.Empty(host.Plugin.Configuration.CurrentOtpCode);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("-1", false)]
    [InlineData("abc", false)]
    [InlineData(" 7 ", true)]
    public void OtpPayloadBuilder_ValidatesNumericCodes(string? code, bool expected)
    {
        var ok = OtpPayloadBuilder.TryCreateAcceptPayload(code, out var json);

        Assert.Equal(expected, ok);
        Assert.Equal(expected, !string.IsNullOrWhiteSpace(json));
    }

    private static AniLibertyAuthController NewController(HttpMessageHandler handler)
        => new(new StubHttpClientFactory(new HttpClient(handler)));

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body) };

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class QueueHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responses.Dequeue()(request));
    }
}
