using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace CookMate.Api.Services;

public class EmailOptions
{
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string FromName { get; set; } = "CookMate";

    // User/Password đọc từ user-secrets (Gmail dùng App Password), không ghi vào appsettings
    public string User { get; set; } = "";
    public string Password { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(User) && !string.IsNullOrWhiteSpace(Password);
}

public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default);
}

public class SmtpEmailSender(EmailOptions options) : IEmailSender
{
    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(options.FromName, options.User));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        client.Timeout = 15000;
        await client.ConnectAsync(options.Host, options.Port, SecureSocketOptions.StartTls, ct);
        // App Password của Google hiển thị có dấu cách, bỏ đi khi đăng nhập
        await client.AuthenticateAsync(options.User, options.Password.Replace(" ", ""), ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}

// Dùng khi chưa cấu hình SMTP (chỉ ở Development): in nội dung email ra log để test.
public class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        logger.LogWarning("[CHƯA CẤU HÌNH SMTP] Email tới {To}: {Subject}\n{Body}", toEmail, subject, body);
        return Task.CompletedTask;
    }
}
