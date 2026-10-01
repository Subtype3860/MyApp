using Microsoft.EntityFrameworkCore;
using MyApp.Domain.Entities;

namespace MyApp.Infrastructure.Db {
    public class AppDbContext : DbContext {
        public DbSet<User> Users => Set<User>();
        public DbSet<Profession> Professions => Set<Profession>();
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {}
        protected override void OnModelCreating(ModelBuilder modelBuilder) {
            modelBuilder.Entity<User>(entity => {
                entity.ToTable("app_users");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id).HasColumnName("id");
                entity.Property(x => x.FirstName).HasColumnName("first_name").IsRequired().HasMaxLength(100);
                entity.Property(x => x.MiddleName).HasColumnName("middle_name").IsRequired().HasMaxLength(100);
                entity.Property(x => x.LastName).HasColumnName("last_name").IsRequired().HasMaxLength(100);
                entity.Property(x => x.UserName).HasColumnName("user_name").IsRequired().HasMaxLength(50);
                entity.Property(x => x.Email).HasColumnName("email").IsRequired().HasMaxLength(100);
                entity.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
                entity.Property(x => x.PositionId).HasColumnName("position").IsRequired();
                entity.Property(x => x.Role).HasColumnName("role").IsRequired().HasMaxLength(30);
                entity.Property(x => x.Permissions).HasColumnName("permissions").IsRequired();
                entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
                entity.Property(x => x.AvatarContent).HasColumnName("avatar_content");
                entity.Property(x => x.AvatarContentType)
                    .HasColumnName("avatar_content_type")
                    .HasMaxLength(100);
                entity.HasIndex(x => x.Email).IsUnique();
                entity.HasIndex(x => x.UserName).IsUnique();
                entity.HasOne(x => x.Profession)
                    .WithMany(x => x.Users)
                    .HasForeignKey(x => x.PositionId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Profession>(entity => {
                entity.ToTable("professions");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id)
                    .HasColumnName("id")
                    .HasDefaultValueSql("gen_random_uuid()");
                entity.Property(x => x.Name)
                    .HasColumnName("profession")
                    .IsRequired()
                    .HasMaxLength(40);
            });
        }
    }
}
