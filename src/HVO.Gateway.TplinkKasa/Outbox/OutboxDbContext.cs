using HVO.Edge.Outbox;
using Microsoft.EntityFrameworkCore;

namespace HVO.Gateway.TplinkKasa.Outbox;

public sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : EdgeOutboxDbContext(options);
