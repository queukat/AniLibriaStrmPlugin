// --- File: AniLibertyAuthController.cs (fixed) ---

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace AniLibertyStrmPlugin;

/// <summary>
///  REST-контроллер для входа (логин/пароль) и OTP по новому AniLiberty API v1.
/// </summary>
[ApiController]
[Route("AniLibertyAuth")]
public class AniLibertyAuthController : ControllerBase
{
    private const string ApiBase = "https://api.anilibria.app/api/v1";

    // ─────────────────────────── 1) Логин/пароль ───────────────────────────

    [HttpPost("SignInLoginPass")]
    public async Task<object> SignInLoginPass([FromBody] LoginRequest req)
    {
        AppendLog($"SignInLoginPass called. login={req?.mail ?? "null"}");

        if (req is null || string.IsNullOrEmpty(req.mail) || string.IsNullOrEmpty(req.passwd))
            return Fail("No login/pass");

        var body = JsonSerializer.Serialize(new { login = req.mail, password = req.passwd });
        var resp = await PostJson($"{ApiBase}/accounts/users/auth/login", body);

        if (!resp.ok)
            return Fail("Auth failed", resp.status, resp.body);

        var token = ExtractToken(resp.body);
        if (string.IsNullOrEmpty(token))
            return Fail("No token in response", resp.status, resp.body);

        var cfg = Plugin.Instance.Configuration;
        cfg.AniLibertyToken = token;
        Plugin.Instance.UpdateConfiguration(cfg);

        return new { success = true, token, serverResponse = resp.body };
    }

    // ────────────────────────────── 2) OTP ────────────────────────────────

    [HttpPost("StartOtp")]
    public async Task<object> StartOtp()
    {
        var cfg = Plugin.Instance.Configuration;
        if (string.IsNullOrEmpty(cfg.AniDeviceId))
        {
            cfg.AniDeviceId = Guid.NewGuid().ToString("N");
            Plugin.Instance.UpdateConfiguration(cfg);
        }

        var body = JsonSerializer.Serialize(new { device_id = cfg.AniDeviceId });
        var resp = await PostJson($"{ApiBase}/accounts/otp/get", body);

        if (!resp.ok)
            return Fail("OTP start failed", resp.status, resp.body);

        string? otp = null;
        try
        {
            using var doc = JsonDocument.Parse(resp.body);
            otp = doc.RootElement.GetProperty("otp").GetString();
        }
        catch { /* ignore */ }

        if (string.IsNullOrEmpty(otp))
            return Fail("No otp in response", resp.status, resp.body);

        cfg.CurrentOtpCode = otp;
        Plugin.Instance.UpdateConfiguration(cfg);

        return new { success = true, otp, serverResponse = resp.body };
    }

    [HttpPost("AcceptOtp")]
    public async Task<object> AcceptOtp([FromBody] OtpRequest req)
    {
        if (req is null || string.IsNullOrEmpty(req.code))
            return Fail("No code");

        var body = JsonSerializer.Serialize(new { code = req.code });
        var resp = await PostJson(
            $"{ApiBase}/accounts/otp/accept",
            body,
            bearer: Plugin.Instance.Configuration.AniLibertyToken);

        return resp.ok
            ? new { success = true, serverResponse = resp.body }
            : Fail("AcceptOtp failed", resp.status, resp.body);
    }

    [HttpPost("SignInOtp")]
    public async Task<object> SignInOtp([FromBody] OtpRequest req)
    {
        var cfg = Plugin.Instance.Configuration;
        if (req is null || string.IsNullOrEmpty(req.code))
            return Fail("No code");
        if (string.IsNullOrEmpty(cfg.AniDeviceId))
            return Fail("No deviceId");

        var body = JsonSerializer.Serialize(new { code = req.code, device_id = cfg.AniDeviceId });
        var resp = await PostJson($"{ApiBase}/accounts/otp/login", body);

        if (!resp.ok)
            return Fail("OTP login failed", resp.status, resp.body);

        var token = ExtractToken(resp.body);
        if (string.IsNullOrEmpty(token))
            return Fail("No token in response", resp.status, resp.body);

        cfg.AniLibertyToken = token;
        Plugin.Instance.UpdateConfiguration(cfg);

        return new { success = true, token, serverResponse = resp.body };
    }

    // ──────────────────────────── helpers ─────────────────────────────

    private static string? ExtractToken(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;
        }
        catch { return null; }
    }

    /// <summary>Единый формат ошибки, чтобы не плодить анонимные объекты c разной формой.</summary>
    private static object Fail(string error, HttpStatusCode status = 0, string? serverResponse = null)
        => new
        {
            success = false,
            error,
            status = (int)status,
            serverResponse
        };

    private static async Task<(bool ok, HttpStatusCode status, string body)> PostJson(
        string url,
        string json,
        string? bearer = null)
    {
        try
        {
            using var client = new HttpClient();
            // Нормальный UA и JSON:
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-AniLibertyStrm/2.0 (+https://github.com/queukat)");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru,en;q=0.8");

            if (!string.IsNullOrEmpty(bearer))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

            var resp = await client.PostAsync(url,
                new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

            var body = await resp.Content.ReadAsStringAsync();
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
        // в консоль — со временем, в UI-лог — без повтора времени (его добавит AppendTaskLog)
        Console.WriteLine($"[AniLibertyAuth] {DateTime.Now:HH:mm:ss} {msg}");
        Plugin.Instance.AppendTaskLog("[AniLibertyAuth] " + msg);
    }

}

public class OtpRequest
{
    public string? code { get; set; }
}

public class LoginRequest
{
    public string? mail { get; set; }
    public string? passwd { get; set; }
}
