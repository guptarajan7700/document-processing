using DocumentProcessing.Infrastructure.Time;

namespace DocumentProcessing.Tests.Support;

public sealed class MutableClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);
}
