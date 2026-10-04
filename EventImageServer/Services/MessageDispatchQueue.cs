using System.Threading.Channels;
using EventImageServer.Models;

namespace EventImageServer.Services
{
    public sealed record MessageDispatchJob(
        int MessageLogId,
        MessageChannel Channel,
        string To,
        string Body);

    public sealed class MessageDispatchQueue
    {
        private readonly Channel<MessageDispatchJob> _channel =
            Channel.CreateUnbounded<MessageDispatchJob>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        public ValueTask EnqueueAsync(MessageDispatchJob job, CancellationToken cancellationToken = default) =>
            _channel.Writer.WriteAsync(job, cancellationToken);

        public IAsyncEnumerable<MessageDispatchJob> ReadAllAsync(CancellationToken cancellationToken) =>
            _channel.Reader.ReadAllAsync(cancellationToken);
    }
}