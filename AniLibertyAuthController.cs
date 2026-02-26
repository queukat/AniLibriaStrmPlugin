// --- File: AniLibertyAuthController.cs (updated) ---

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin;

/// <summary>
///     REST controller for login (username/password) and OTP using AniLiberty API v1.
/// </summary>
[ApiController]
[Route("AniLibertyAuth")]
public class AniLibertyAuthController : ControllerBase
{
    private const string ApiBase = "https://api.anilibria.app/api/v1";
    private readonly IHttpClientFactory _httpFactory;

    public AniLibertyAuthController(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    // ─────────────────────────── 1) Username/password ───────────────────────────

    [HttpPost("SignInLoginPass")]
    public async Task<IActionResult> SignInLoginPass([FromBody] LoginRequest? req, CancellationToken ct)
    {
        AppendLog($"SignInLoginPass called. login={req?.Mail ?? "null"}");

        if (req is null || string.IsNullOrWhiteSpace(req.Mail) || string.IsNullOrWhiteSpace(req.Passwd))
            return BadRequest(Fail("No login/pass"));

        var body = JsonSerializer.Serialize(new { login = req.Mail, password = req.Passwd });
        var resp = await PostJsonAsync($"{ApiBase}/accounts/users/auth/login", body, bearer: null, ct);

        if (!resp.ok)
            return StatusCode(ToStatusCode(resp.status), Fail("Auth failed", resp.status, resp.body));

        var token = ExtractToken(resp.body);
        if (string.IsNullOrWhiteSpace(token))
            return StatusCode(ToStatusCode(resp.status), Fail("No token in response", resp.status, resp.body));

        var plugin = RequirePlugin();
        var cfg = plugin.Configuration;
        cfg.AniLibertyToken = token;
        plugin.UpdateConfiguration(cfg);

        return Ok(new { success = true, token, serverResponse = resp.body });
    }

    // ────────────────────────────── 2) OTP ────────────────────────────────

    [HttpPost("StartOtp")]
    public async Task<IActionResult> StartOtp(CancellationToken ct)
    {
        var plugin = RequirePlugin();
        var cfg = plugin.Configuration;

        if (string.IsNullOrWhiteSpace(cfg.AniDeviceId))
        {
            cfg.AniDeviceId = Guid.NewGuid().ToString("N");
            plugin.UpdateConfiguration(cfg);
        }

        var body = JsonSerializer.Serialize(new { device_id = cfg.AniDeviceId });
        var resp = await PostJsonAsync($"{ApiBase}/accounts/otp/get", body, bearer: null, ct);

        if (!resp.ok)
            return StatusCode(ToStatusCode(resp.status), Fail("OTP start failed", resp.status, resp.body));

        // v1: { "otp": { "code": "058701", ... }, "remaining_time": 120 }
        string? otp = null;
        try
        {
            using var doc = JsonDocument.Parse(resp.body);

            if (doc.RootElement.TryGetProperty("otp", out var otpEl))
            {
                // For safety, also support the legacy format if the API returns a string again.
                if (otpEl.ValueKind == JsonValueKind.String)
                {
                    otp = otpEl.GetString();
                }
                else if (otpEl.ValueKind == JsonValueKind.Object &&
                         otpEl.TryGetProperty("code", out var codeEl) &&
                         codeEl.ValueKind == JsonValueKind.String)
                {
                    otp = codeEl.GetString();
                }
            }
        }
        catch
        {
            /* ignore */
        }

        if (string.IsNullOrWhiteSpace(otp))
            return StatusCode(ToStatusCode(resp.status), Fail("No otp in response", resp.status, resp.body));

        cfg.CurrentOtpCode = otp;
        plugin.UpdateConfiguration(cfg);

        return Ok(new { success = true, otp, serverResponse = resp.body });
    }

    [HttpPost("AcceptOtp")]
    public async Task<IActionResult> AcceptOtp([FromBody] OtpRequest? req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(Fail("No code"));

        var plugin = RequirePlugin();
        var token = plugin.Configuration.AniLibertyToken;
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(Fail("No auth token"));

        var body = JsonSerializer.Serialize(new { code = req.Code });
        var resp = await PostJsonAsync(
            $"{ApiBase}/accounts/otp/accept",
            body,
            bearer: token,
            ct);

        return resp.ok
            ? Ok(new { success = true, serverResponse = resp.body })
            : StatusCode(ToStatusCode(resp.status), Fail("AcceptOtp failed", resp.status, resp.body));
    }

    [HttpPost("SignInOtp")]
    public async Task<IActionResult> SignInOtp([FromBody] OtpRequest? req, CancellationToken ct)
    {
        var plugin = RequirePlugin();
        var cfg = plugin.Configuration;

        if (req is null || string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(Fail("No code"));
        if (string.IsNullOrWhiteSpace(cfg.AniDeviceId))
            return BadRequest(Fail("No deviceId"));

        var body = JsonSerializer.Serialize(new { code = req.Code, device_id = cfg.AniDeviceId });
        var resp = await PostJsonAsync($"{ApiBase}/accounts/otp/login", body, bearer: null, ct);

        if (!resp.ok)
            return StatusCode(ToStatusCode(resp.status), Fail("OTP login failed", resp.status, resp.body));

        var token = ExtractToken(resp.body);
        if (string.IsNullOrWhiteSpace(token))
            return StatusCode(ToStatusCode(resp.status), Fail("No token in response", resp.status, resp.body));

        cfg.AniLibertyToken = token;
        plugin.UpdateConfiguration(cfg);

        return Ok(new { success = true, token, serverResponse = resp.body });
    }

    // ──────────────────────────── helpers ─────────────────────────────

    private static Plugin RequirePlugin()
        => Plugin.Instance ?? throw new InvalidOperationException("Plugin instance is not initialized.");

    private static int ToStatusCode(HttpStatusCode status)
    {
        var code = (int)status;
        return code is >= 100 and <= 599 ? code : 500;
    }

    private static string? ExtractToken(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Unified error shape to avoid multiple anonymous object formats.</summary>
    private static object Fail(string error, HttpStatusCode status = 0, string? serverResponse = null)
    {
        return new
        {
            success = false,
            error,
            status = (int)status,
            serverResponse
        };
    }

    private async Task<(bool ok, HttpStatusCode status, string body)> PostJsonAsync(
        string url,
        string json,
        string? bearer,
        CancellationToken ct)
    {
        try
        {
            var client = _httpFactory.CreateClient("AniLiberty");

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            if (!string.IsNullOrWhiteSpace(bearer))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return (resp.IsSuccessStatusCode, resp.StatusCode, body);
        }
        catch (OperationCanceledException)
        {
            return (false, 0, "Canceled");
        }
        catch (Exception ex)
        {
            return (false, 0, ex.Message);
        }
    }

    private static void AppendLog(string msg)
    {
        // Write only when Debug logs are enabled (otherwise UI log grows too fast).
        var cfg = Plugin.Instance?.Configuration;
        if (cfg?.EnableDebugLogs != true)
            return;

        Console.WriteLine($"[AniLibertyAuth] {DateTime.Now:HH:mm:ss} {msg}");

        // In tests or before plugin initialization, Instance may be null.
        Plugin.Instance?.AppendTaskLog("[AniLibertyAuth] " + msg, LogLevel.Debug);
    }
}

public sealed class OtpRequest
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }
}

public sealed class LoginRequest
{
    [JsonPropertyName("mail")]
    public string? Mail { get; set; }

    [JsonPropertyName("passwd")]
    public string? Passwd { get; set; }
}
