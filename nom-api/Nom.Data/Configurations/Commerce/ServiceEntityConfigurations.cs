using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Commerce;

namespace Nom.Data.Configurations.Commerce;

public class ServiceOfferingEntityConfiguration : IEntityTypeConfiguration<ServiceOfferingEntity>
{
    public void Configure(EntityTypeBuilder<ServiceOfferingEntity> builder)
    {
        builder.ToTable("ServiceOffering", schema: "commerce");
        builder.Property(e => e.ServiceType).IsRequired().HasMaxLength(32);
        builder.Property(e => e.ProviderName).IsRequired().HasMaxLength(255);
        builder.Property(e => e.ProviderPayoutaccount).HasMaxLength(255);
        builder.Property(e => e.CoverageArea).HasMaxLength(128);
        builder.Property(e => e.PricingModel).IsRequired().HasMaxLength(16).HasDefaultValue("quote");
        builder.Property(e => e.BasePrice).HasColumnType("decimal(10,2)");
        builder.Property(e => e.CommissionRate).HasColumnType("decimal(5,4)").HasDefaultValue(0m);
        builder.HasIndex(e => new { e.ServiceType, e.IsActive });
    }
}

public class ServiceOrderEntityConfiguration : IEntityTypeConfiguration<ServiceOrderEntity>
{
    public void Configure(EntityTypeBuilder<ServiceOrderEntity> builder)
    {
        builder.ToTable("ServiceOrder", schema: "commerce");
        builder.Property(e => e.Status).IsRequired().HasMaxLength(16).HasDefaultValue("quoted");
        builder.Property(e => e.QuotedTotal).HasColumnType("decimal(10,2)");
        builder.Property(e => e.PlatformFee).HasColumnType("decimal(10,2)");
        builder.Property(e => e.ProviderPayout).HasColumnType("decimal(10,2)");
        builder.Property(e => e.Currency).IsRequired().HasMaxLength(3).HasDefaultValue("USD");
        builder.Property(e => e.PaymentProcessor).HasMaxLength(16);
        builder.Property(e => e.PaymentReference).HasMaxLength(255);
        builder.HasIndex(e => new { e.CustomerPersonId, e.Status });

        builder.HasOne(e => e.ServiceOffering)
            .WithMany()
            .HasForeignKey(e => e.ServiceOfferingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
