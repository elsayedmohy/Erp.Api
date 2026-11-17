using ErpDashboard.Api.Features.Notifications;

namespace ErpDashboard.Api.IntegrationTests;

public sealed class FakeWhatsAppSender : IWhatsAppSender
{
    public enum Behavior { Accept, Reject, Unavailable }

    private readonly List<(string To, string Text)> _sent = [];
    public Behavior Next { get; set; } = Behavior.Accept;
    public IReadOnlyList<(string To, string Text)> Sent { get { lock (_sent) return [.. _sent]; } }

    public Task<WhatsAppSendResult> SendTextAsync(string recipient, string text, CancellationToken ct)
    {
        switch (Next)
        {
            case Behavior.Unavailable: throw new WhatsAppUnavailableException("Fake gateway down.");
            case Behavior.Reject: return Task.FromResult(WhatsAppSendResult.Rejected("Fake rejection."));
            default:
                lock (_sent) _sent.Add((recipient, text));
                return Task.FromResult(WhatsAppSendResult.Sent($"msg-{Guid.NewGuid():N}"));
        }
    }

    public void Reset()
    {
        Next = Behavior.Accept;
        lock (_sent) _sent.Clear();
    }
}