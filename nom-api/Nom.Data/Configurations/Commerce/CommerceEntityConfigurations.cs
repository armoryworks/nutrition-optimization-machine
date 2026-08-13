using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Commerce;

namespace Nom.Data.Configurations.Commerce;

public class GroceryStoreEntityConfiguration : IEntityTypeConfiguration<GroceryStoreEntity>
{
    public void Configure(EntityTypeBuilder<GroceryStoreEntity> builder)
    {
        builder.ToTable("GroceryStore", schema: "commerce");
        builder.Property(e => e.Name).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Chain).HasMaxLength(255);
        builder.Property(e => e.AddressLine).HasMaxLength(511);
        builder.Property(e => e.City).HasMaxLength(255);
        builder.Property(e => e.Region).HasMaxLength(64);
        builder.Property(e => e.PostalCode).HasMaxLength(16);
        builder.Property(e => e.Latitude).HasColumnType("decimal(9,6)");
        builder.Property(e => e.Longitude).HasColumnType("decimal(9,6)");
        builder.Property(e => e.ExternalId).HasMaxLength(255);
        builder.HasIndex(e => e.PostalCode);
        builder.HasIndex(e => e.Chain);
    }
}

public class StorePriceEntityConfiguration : IEntityTypeConfiguration<StorePriceEntity>
{
    public void Configure(EntityTypeBuilder<StorePriceEntity> builder)
    {
        builder.ToTable("StorePrice", schema: "commerce");
        builder.Property(e => e.Price).HasColumnType("decimal(10,2)");
        builder.Property(e => e.Currency).IsRequired().HasMaxLength(3).HasDefaultValue("USD");
        builder.Property(e => e.Source).IsRequired().HasMaxLength(32).HasDefaultValue("manual");
        builder.HasIndex(e => new { e.GroceryStoreId, e.RetailPackagingId, e.AsOf });

        builder.HasOne(e => e.GroceryStore)
            .WithMany()
            .HasForeignKey(e => e.GroceryStoreId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.RetailPackaging)
            .WithMany()
            .HasForeignKey(e => e.RetailPackagingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PriceObservationEntityConfiguration : IEntityTypeConfiguration<PriceObservationEntity>
{
    public void Configure(EntityTypeBuilder<PriceObservationEntity> builder)
    {
        builder.ToTable("PriceObservation", schema: "commerce");
        builder.Property(e => e.StoreNameRaw).HasMaxLength(255);
        builder.Property(e => e.PostalCode).HasMaxLength(16);
        builder.Property(e => e.ItemText).IsRequired().HasMaxLength(511);
        builder.Property(e => e.Price).HasColumnType("decimal(10,2)");
        builder.Property(e => e.Currency).IsRequired().HasMaxLength(3).HasDefaultValue("USD");
        builder.Property(e => e.Confidence).HasColumnType("decimal(4,3)").HasDefaultValue(0m);
        builder.HasIndex(e => e.IngredientId);
        builder.HasIndex(e => new { e.PostalCode, e.PurchasedOn });

        builder.HasOne(e => e.GroceryStore)
            .WithMany()
            .HasForeignKey(e => e.GroceryStoreId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class CouponEntityConfiguration : IEntityTypeConfiguration<CouponEntity>
{
    public void Configure(EntityTypeBuilder<CouponEntity> builder)
    {
        builder.ToTable("Coupon", schema: "commerce");
        builder.Property(e => e.Chain).HasMaxLength(255);
        builder.Property(e => e.Title).IsRequired().HasMaxLength(255);
        builder.Property(e => e.ItemPattern).IsRequired().HasMaxLength(255);
        builder.Property(e => e.DiscountAmount).HasColumnType("decimal(10,2)");
        builder.Property(e => e.DiscountType).IsRequired().HasMaxLength(16).HasDefaultValue("amount");
        builder.Property(e => e.Source).IsRequired().HasMaxLength(32).HasDefaultValue("manual");
        builder.HasIndex(e => new { e.Chain, e.ValidTo });

        builder.HasOne(e => e.GroceryStore)
            .WithMany()
            .HasForeignKey(e => e.GroceryStoreId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
