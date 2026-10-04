using EventImageServer.Contexts;
using Microsoft.EntityFrameworkCore;

namespace EventImageServer.Services
{
    public sealed class MessageDispatchWorker : BackgroundService
    {
        private readonly MessageDispatchQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MessageDispatchWorker> _logger;

        public MessageDispatchWorker(
            MessageDispatchQueue queue,
            IServiceScopeFactory scopeFactory,
            ILogger<MessageDispatchWorker> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var job in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await DispatchAsync(job, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process message dispatch job {MessageLogId}.", job.MessageLogId);
                }
            }
        }

        private async Task DispatchAsync(MessageDispatchJob job, CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var messageLog = await dbContext.MessageLogs
                .SingleOrDefaultAsync(m => m.MessageLogId == job.MessageLogId, stoppingToken);

            if (messageLog == null)
            {
                _logger.LogWarning("Message log {MessageLogId} was not found for dispatch.", job.MessageLogId);
                return;
            }

            var twilio = scope.ServiceProvider.GetRequiredService<TwilioMessagingService>();
            try
            {
                var result = await twilio.SendAsync(job.Channel, job.To, job.Body);
                messageLog.TwilioSid = result.Sid;
                messageLog.Status = result.Status;
            }
            catch (Exception ex)
            {
                messageLog.Status = "failed";
                messageLog.ErrorCode = ex.Message;
                _logger.LogError(ex, "Message dispatch failed for log {MessageLogId}.", job.MessageLogId);
            }

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
    }
}