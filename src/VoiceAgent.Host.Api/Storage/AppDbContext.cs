using Microsoft.EntityFrameworkCore;

namespace VoiceAgent.Host.Api.Storage;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Call> Calls => Set<Call>();
    public DbSet<CallTurn> CallTurns => Set<CallTurn>();
    public DbSet<CallField> CallFields => Set<CallField>();
    public DbSet<CallFieldHistory> CallFieldHistories => Set<CallFieldHistory>();
    public DbSet<DoNotCall> DoNotCalls => Set<DoNotCall>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Tenant>().HasKey(x => x.Id);

        b.Entity<Agent>().HasKey(x => x.Id);
        b.Entity<Agent>().HasIndex(x => new { x.TenantId, x.DisplayName });

        b.Entity<Lead>().HasKey(x => x.Id);
        b.Entity<Lead>().HasIndex(x => new { x.TenantId, x.Phone });
        b.Entity<Lead>().HasIndex(x => new { x.TenantId, x.CampaignCode, x.Status });

        b.Entity<Call>().HasKey(x => x.Id);
        b.Entity<Call>().HasIndex(x => new { x.TenantId, x.LeadId });
        b.Entity<Call>().HasIndex(x => new { x.TenantId, x.Status });

        b.Entity<CallTurn>().HasKey(x => x.Id);
        b.Entity<CallTurn>().HasIndex(x => new { x.TenantId, x.CallId, x.At });

        b.Entity<CallField>().HasKey(x => x.Id);
        b.Entity<CallField>().HasIndex(x => new { x.TenantId, x.CallId, x.Key });

        b.Entity<CallFieldHistory>().HasKey(x => x.Id);
        b.Entity<CallFieldHistory>().HasIndex(x => new { x.TenantId, x.CallId, x.FieldName });

        b.Entity<DoNotCall>().HasKey(x => x.Id);
        b.Entity<DoNotCall>().HasIndex(x => new { x.TenantId, x.Phone }).IsUnique();
    }
}
