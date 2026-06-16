using HVO.Edge.Outbox;
using Microsoft.EntityFrameworkCore;

namespace HVO.Hardware.VictronSmartShunt.Outbox;

public sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : EdgeOutboxDbContext(options);
