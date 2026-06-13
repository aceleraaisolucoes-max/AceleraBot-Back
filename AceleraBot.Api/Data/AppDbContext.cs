using Microsoft.EntityFrameworkCore;

namespace AceleraBot.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<KnowledgeBase> KnowledgeBase => Set<KnowledgeBase>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<GoogleCalendarConfig> GoogleCalendarConfigs => Set<GoogleCalendarConfig>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ids gerados pelo banco (gen_random_uuid)
        foreach (var clr in new[]
        {
            typeof(Client), typeof(KnowledgeBase), typeof(Conversation), typeof(Message),
            typeof(Lead), typeof(Appointment), typeof(GoogleCalendarConfig)
        })
        {
            b.Entity(clr).Property("Id").HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
        }

        b.Entity<Client>(e =>
        {
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            e.Property(x => x.BusinessHours).HasColumnType("jsonb");
        });
        b.Entity<Conversation>(e =>
        {
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            e.Property(x => x.LastMessageAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        b.Entity<Message>(e => e.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd());
        b.Entity<KnowledgeBase>(e => e.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd());
        b.Entity<Lead>(e => e.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd());
        b.Entity<Appointment>(e =>
        {
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        b.Entity<GoogleCalendarConfig>(e =>
        {
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
    }
}
