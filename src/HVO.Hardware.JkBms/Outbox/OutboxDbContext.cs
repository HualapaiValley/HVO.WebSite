using HVO.Edge.Outbox;
using Microsoft.EntityFrameworkCore;

namespace HVO.Hardware.JkBms.Outbox;

/// <summary>EF Core DbContext for the local SQLite outbox database.</summary>
public sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : EdgeOutboxDbContext(options);
