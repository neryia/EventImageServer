using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;
using EventImageServer.Models;

namespace EventImageServer.Services
{
    public class TwilioOptions
    {
        public string AccountSid { get; set; } = string.Empty;
        public string AuthToken { get; set; } = string.Empty;
        public string SmsFrom { get; set; } = string.Empty;
        public string WhatsAppFrom { get; set; } = string.Empty;
        public string StatusCallbackUrl { get; set; } = string.Empty;
        public string PublicBaseUrl { get; set; } = string.Empty;
    }

    public class SendMessageResult
    {
        public string Sid { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    // Thin wrapper around the Twilio SDK for sending SMS/WhatsApp messages and
    // reading configuration (credentials, sender numbers, callback/base URLs).
    public class TwilioMessagingService
    {
        private readonly TwilioOptions _options;
        private bool _initialized;

        public TwilioMessagingService(TwilioOptions options)
        {
            _options = options;
        }

        public string PublicBaseUrl => _options.PublicBaseUrl;

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }
            TwilioClient.Init(_options.AccountSid, _options.AuthToken);
            _initialized = true;
        }

        public async Task<SendMessageResult> SendAsync(MessageChannel channel, string toPhone, string body)
        {
            EnsureInitialized();

            var from = channel == MessageChannel.WhatsApp ? _options.WhatsAppFrom : _options.SmsFrom;
            var to = channel == MessageChannel.WhatsApp ? $"whatsapp:{toPhone}" : toPhone;
            var fromNumber = channel == MessageChannel.WhatsApp ? $"whatsapp:{from}" : from;

            var createOptions = new CreateMessageOptions(new PhoneNumber(to))
            {
                From = new PhoneNumber(fromNumber),
                Body = body
            };

            if (!string.IsNullOrWhiteSpace(_options.StatusCallbackUrl))
            {
                createOptions.StatusCallback = new Uri(_options.StatusCallbackUrl);
            }

            var message = await MessageResource.CreateAsync(createOptions);

            return new SendMessageResult
            {
                Sid = message.Sid,
                Status = message.Status?.ToString() ?? "queued"
            };
        }
    }
}
