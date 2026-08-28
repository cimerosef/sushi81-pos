using Sushi81.Pos.Application.Foundation.Ids;

namespace Sushi81.Pos.Infrastructure.Ids;

public sealed class GuidV7IdGenerator(TimeProvider timeProvider) : IIdGenerator
{
    public Guid NewId() => Guid.CreateVersion7(timeProvider.GetUtcNow());
}
