using HVO.Edge.Outbox;
using Microsoft.EntityFrameworkCore;

namespace HVO.Hardware.DavisVantagePro2.Outbox;

/// <summary>EF Core DbContext for the shared edge outbox database.</summary>
public sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : EdgeOutboxDbContext(options);
