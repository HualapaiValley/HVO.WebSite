using Microsoft.EntityFrameworkCore;

namespace HVO.Edge.Outbox;

public sealed class DefaultEdgeOutboxDbContext(DbContextOptions<DefaultEdgeOutboxDbContext> options)
    : EdgeOutboxDbContext(options);
