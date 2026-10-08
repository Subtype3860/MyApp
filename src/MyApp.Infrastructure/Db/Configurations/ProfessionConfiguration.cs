using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Domain.Entities;

namespace MyApp.Infrastructure.Db.Configurations;

public sealed class ProfessionConfiguration : IEntityTypeConfiguration<Profession>
{
    public void Configure(EntityTypeBuilder<Profession> entity)
    {
        entity.ToTable("professions");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");
        entity.Property(x => x.Name)
            .HasColumnName("profession")
            .IsRequired()
            .HasMaxLength(40);
    }
}
