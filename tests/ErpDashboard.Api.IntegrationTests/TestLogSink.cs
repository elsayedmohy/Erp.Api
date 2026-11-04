using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace ErpDashboard.Api.IntegrationTests;

public sealed class TestLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyCollection<LogEvent> Events => _events;

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
}