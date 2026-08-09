using HVO.Edge.Outbox;
using Microsoft.EntityFrameworkCore;

namespace HVO.Hardware.Eg4.Outbox;

public sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : EdgeOutboxDbContext(options);
