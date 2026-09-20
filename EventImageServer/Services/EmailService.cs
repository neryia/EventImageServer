using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace EventImageServer.Services
{
    public class SmtpOptions
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public string User { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
        public string FromName { get; set; } = "EventImage";
        public bool UseSsl { get; set; } = false; // true = implicit TLS (port 465), false = STARTTLS (port 587)
        public string PublicBaseUrl { get; set; } = string.Empty; // frontend base URL used to build links in emails
    }

    // Thin wrapper around MailKit for sending transactional emails (e.g.
    // collaborator invites) via any standard SMTP provider (Gmail app
    // password, SendGrid/Mailgun SMTP relay, etc). Configured under the
    // "Smtp" section in appsettings.json / appsettings.Development.json.
    public class EmailService
    {
        private readonly SmtpOptions _options;
        private readonly ILogger<EmailService> _logger;

        public EmailService(SmtpOptions options, ILogger<EmailService> logger)
        {
            _options = options;
            _logger = logger;
        }

        public string PublicBaseUrl => _options.PublicBaseUrl;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_options.Host) &&
            !string.IsNullOrWhiteSpace(_options.FromEmail);

        public async Task<bool> SendAsync(string toEmail, string subject, string bodyText)
        {
            if (!IsConfigured)
            {
                _logger.LogWarning("Email not sent to {ToEmail} ('{Subject}') — SMTP is not configured.", toEmail, subject);
                return false;
            }

            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(_options.FromName, _options.FromEmail));
                message.To.Add(MailboxAddress.Parse(toEmail));
                message.Subject = subject;
                message.Body = new TextPart("plain") { Text = bodyText };

                using var client = new SmtpClient();
                // Some dev/sandboxed networks can't reach Google's OCSP/CRL
                // endpoints to complete the revocation check, which .NET
                // otherwise treats as a hard TLS failure even though the
                // certificate itself is valid. Skip that check here.
                client.CheckCertificateRevocation = false;
                var socketOptions = _options.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
                await client.ConnectAsync(_options.Host, _options.Port, socketOptions);

                if (!string.IsNullOrWhiteSpace(_options.User))
                {
                    await client.AuthenticateAsync(_options.User, _options.Password);
                }

                await client.SendAsync(message);
                await client.DisconnectAsync(true);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {ToEmail} ('{Subject}').", toEmail, subject);
                return false;
            }
        }
    }
}
