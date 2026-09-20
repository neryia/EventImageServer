using EventImageServer.Contexts;
using EventImageServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventImageServer.Services
{
    // Hourly background service that sends automatic RSVP reminders to
    // still-pending guests, N days before Users.RsvpDeadline, for every
    // offset in Users.ReminderOffsets (e.g. 30/14/7/2 days before). Only
    // runs for owners with AutoRemindersEnabled = true.
    //
    // Idempotency: before sending, checks for an existing MessageLog
    // (GuestId, Type=Reminder) sent within the last 24 hours and skips if
    // found, so an app restart (or the hourly tick firing more than once on
    // the matching day) never double-sends the same reminder.
    public class ReminderScheduler : BackgroundService
    {
        private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ReminderScheduler> _logger;

        public ReminderScheduler(IServiceScopeFactory scopeFactory, ILogger<ReminderScheduler> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ReminderScheduler tick failed");
                }

                try
                {
                    await Task.Delay(TickInterval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    // Expected on shutdown.
                }
            }
        }

        private async Task RunOnceAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var twilio = scope.ServiceProvider.GetRequiredService<TwilioMessagingService>();
            var twilioOptions = scope.ServiceProvider.GetRequiredService<IOptions<TwilioOptions>>().Value;

            if (string.IsNullOrWhiteSpace(twilioOptions.PublicBaseUrl))
            {
                // No HttpContext is available in a background service, so an
                // RSVP link can only be built from a configured public base
                // URL. Nothing to do without one.
                return;
            }

            var today = DateTime.UtcNow.Date;
            var owners = await dbContext.Clients
                .Where(u => u.AutoRemindersEnabled && u.RsvpDeadline != null)
                .ToListAsync(stoppingToken);

            foreach (var owner in owners)
            {
                if (owner.ReminderOffsets.Count == 0)
                {
                    continue;
                }

                var daysUntilDeadline = (owner.RsvpDeadline!.Value.Date - today).Days;
                if (!owner.ReminderOffsets.Contains(daysUntilDeadline))
                {
                    continue;
                }

                await SendRemindersForOwnerAsync(dbContext, twilio, twilioOptions.PublicBaseUrl!, owner, stoppingToken);
            }
        }

        // Mirrors SeatingController.SendGuestMessages' reminder eligibility
        // filter (not opted out, has a phone number, still Pending), plus the
        // idempotency check that only applies to the automatic scheduler.
        private async Task SendRemindersForOwnerAsync(
            AppDbContext dbContext,
            TwilioMessagingService twilio,
            string publicBaseUrl,
            Users owner,
            CancellationToken stoppingToken)
        {
            var eligibleGuests = await dbContext.Guests
                .Where(g => g.OwnerId == owner.Id
                    && !g.OptedOut
                    && g.RsvpStatus == RsvpStatus.Pending
                    && g.Phone != null && g.Phone != "")
                .ToListAsync(stoppingToken);

            if (eligibleGuests.Count == 0)
            {
                return;
            }

            var cutoff = DateTime.UtcNow.AddHours(-24);
            var recentlyRemindedGuestIds = await dbContext.MessageLogs
                .Where(m => m.OwnerId == owner.Id && m.Type == MessageType.Reminder && m.SentAt > cutoff)
                .Select(m => m.GuestId)
                .ToListAsync(stoppingToken);
            var recentlyRemindedSet = recentlyRemindedGuestIds.ToHashSet();

            var baseUrl = publicBaseUrl.TrimEnd('/');

            foreach (var guest in eligibleGuests)
            {
                if (recentlyRemindedSet.Contains(guest.GuestId))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(guest.RsvpToken))
                {
                    guest.RsvpToken = GenerateSecureToken();
                    guest.RsvpTokenCreatedAt = DateTime.UtcNow;
                }

                var link = $"{baseUrl}/Rsvp/{guest.RsvpToken}";
                var body = $"Reminder: please RSVP here: {link}";

                var log = new MessageLog
                {
                    OwnerId = owner.Id,
                    GuestId = guest.GuestId,
                    Channel = MessageChannel.Sms,
                    Type = MessageType.Reminder,
                    To = guest.Phone!,
                    SentAt = DateTime.UtcNow
                };

                try
                {
                    var sendResult = await twilio.SendAsync(MessageChannel.Sms, guest.Phone!, body);
                    log.TwilioSid = sendResult.Sid;
                    log.Status = sendResult.Status;
                }
                catch (Exception ex)
                {
                    log.Status = "failed";
                    log.ErrorCode = ex.Message;
                }

                dbContext.MessageLogs.Add(log);
            }

            await dbContext.SaveChangesAsync(stoppingToken);
        }

        private static string GenerateSecureToken()
        {
            var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }
    }
}
