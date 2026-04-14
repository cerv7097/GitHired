using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace CareerCoach.Services;

public class EmailSender
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<EmailSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task SendVerificationCodeAsync(string toEmail, string firstName, string code, DateTime expiresAtUtc)
    {
        var apiKey = _config["RESEND_API_KEY"];
        var fromEmail = _config["EMAIL_FROM_ADDRESS"];
        var fromName = _config["EMAIL_FROM_NAME"] ?? "Career Coach";

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(fromEmail))
        {
            throw new InvalidOperationException(
                "Email sending is not configured. Set RESEND_API_KEY and EMAIL_FROM_ADDRESS."
            );
        }

        var payload = new
        {
            from = $"{fromName} <{fromEmail}>",
            to = new[] { toEmail },
            subject = "Verify your Career Coach account",
            html = $"""
                <p>Hi {EscapeHtml(firstName)},</p>
                <p>Your verification code is:</p>
                <p style="font-size:24px;font-weight:bold;letter-spacing:4px;">{EscapeHtml(code)}</p>
                <p>This code expires at {expiresAtUtc:yyyy-MM-dd HH:mm} UTC.</p>
                <p>If you did not create this account, you can ignore this email.</p>
                """
        };

        await SendEmailAsync(apiKey, payload);
        _logger.LogInformation("Sent verification email to {Email} via Resend", toEmail);
    }

    public async Task SendPasswordResetCodeAsync(string toEmail, string firstName, string code, DateTime expiresAtUtc)
    {
        var apiKey = _config["RESEND_API_KEY"];
        var fromEmail = _config["EMAIL_FROM_ADDRESS"];
        var fromName = _config["EMAIL_FROM_NAME"] ?? "Career Coach";

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(fromEmail))
            throw new InvalidOperationException("Email sending is not configured.");

        var payload = new
        {
            from = $"{fromName} <{fromEmail}>",
            to = new[] { toEmail },
            subject = "Reset your Career Coach password",
            html = $"""
                <p>Hi {EscapeHtml(firstName)},</p>
                <p>We received a request to reset your password. Your reset code is:</p>
                <p style="font-size:24px;font-weight:bold;letter-spacing:4px;">{EscapeHtml(code)}</p>
                <p>This code expires at {expiresAtUtc:yyyy-MM-dd HH:mm} UTC.</p>
                <p>If you did not request a password reset, you can ignore this email.</p>
                """
        };

        await SendEmailAsync(apiKey, payload);
        _logger.LogInformation("Sent password reset email to {Email} via Resend", toEmail);
    }

    public async Task SendEmailChangeCodeAsync(string toEmail, string firstName, string newEmail, string code, DateTime expiresAtUtc)
    {
        var apiKey = _config["RESEND_API_KEY"];
        var fromEmail = _config["EMAIL_FROM_ADDRESS"];
        var fromName = _config["EMAIL_FROM_NAME"] ?? "Career Coach";

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(fromEmail))
            throw new InvalidOperationException("Email sending is not configured.");

        var payload = new
        {
            from = $"{fromName} <{fromEmail}>",
            to = new[] { toEmail },
            subject = "Confirm your Career Coach email change",
            html = $"""
                <p>Hi {EscapeHtml(firstName)},</p>
                <p>We received a request to change your email address to <strong>{EscapeHtml(newEmail)}</strong>.</p>
                <p>Your confirmation code is:</p>
                <p style="font-size:24px;font-weight:bold;letter-spacing:4px;">{EscapeHtml(code)}</p>
                <p>This code expires at {expiresAtUtc:yyyy-MM-dd HH:mm} UTC.</p>
                <p>If you did not request this change, you can ignore this email.</p>
                """
        };

        await SendEmailAsync(apiKey, payload);
        _logger.LogInformation("Sent email change confirmation to {Email} via Resend", toEmail);
    }

    private async Task SendEmailAsync(string apiKey, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"
        );

        var client = _httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Resend email send failed with status {StatusCode}: {ResponseBody}",
                (int)response.StatusCode, responseBody);
            throw new InvalidOperationException($"Resend rejected the email send request: {responseBody}");
        }
    }

    private static string EscapeHtml(string value) =>
        System.Net.WebUtility.HtmlEncode(value);
}
