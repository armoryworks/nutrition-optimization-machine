using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class SourceCandidateEntityConfiguration : IEntityTypeConfiguration<SourceCandidateEntity>
{
    public void Configure(EntityTypeBuilder<SourceCandidateEntity> builder)
    {
        builder.ToTable("SourceCandidate", schema: "recipe");

        builder.Property(e => e.Domain).IsRequired().HasMaxLength(255);
        builder.HasIndex(e => e.Domain).IsUnique();
        builder.Property(e => e.EvidenceUrl).HasMaxLength(2047);
        builder.Property(e => e.FoundVia).HasMaxLength(255);
        builder.Property(e => e.Signal).HasMaxLength(32);
        builder.Property(e => e.State).IsRequired().HasMaxLength(16);
        builder.HasIndex(e => e.State);
    }
}

public class SourceCheckEntityConfiguration : IEntityTypeConfiguration<SourceCheckEntity>
{
    public void Configure(EntityTypeBuilder<SourceCheckEntity> builder)
    {
        builder.ToTable("SourceCheck", schema: "recipe");

        builder.Property(e => e.Domain).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Purpose).IsRequired().HasMaxLength(16);
        builder.Property(e => e.Mode).HasMaxLength(16);
        builder.Property(e => e.JobId).HasMaxLength(64);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(16);
        builder.Property(e => e.StopReason).HasMaxLength(16);
        builder.Property(e => e.Error).HasMaxLength(1023);
        builder.Property(e => e.ScraperVerdict).HasMaxLength(16);
        builder.Property(e => e.RuleVersion).HasMaxLength(32);
        builder.Property(e => e.Verdict).HasMaxLength(16);
        builder.Property(e => e.PolicyVersion).HasMaxLength(32);
        builder.Property(e => e.ReasonsJson).HasColumnType("jsonb");
        builder.Property(e => e.ReportJson).HasColumnType("jsonb");

        builder.HasIndex(e => new { e.Domain, e.StartedUtc });
        builder.HasIndex(e => e.StartedUtc);
    }
}

public class SourceDiscoveryRunEntityConfiguration : IEntityTypeConfiguration<SourceDiscoveryRunEntity>
{
    public void Configure(EntityTypeBuilder<SourceDiscoveryRunEntity> builder)
    {
        builder.ToTable("SourceDiscoveryRun", schema: "recipe");

        builder.Property(e => e.JobId).HasMaxLength(64);
        builder.Property(e => e.Seeds).IsRequired().HasMaxLength(2047);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(16);
        builder.Property(e => e.StopReason).HasMaxLength(16);
        builder.Property(e => e.Error).HasMaxLength(1023);
        builder.Property(e => e.SeedsVisited).HasMaxLength(2047);
        builder.Property(e => e.SeedsNotVisited).HasMaxLength(2047);
        builder.Property(e => e.SeedsRefused).HasMaxLength(2047);
        builder.Property(e => e.ExcludeSent).HasDefaultValue(0);
        builder.Property(e => e.ExcludeLeftOut).HasDefaultValue(0);
        builder.Property(e => e.ExcludeCursorHost).HasMaxLength(253);
        builder.Property(e => e.SeedsExcluded).HasMaxLength(2047);
        builder.Property(e => e.CandidatesExcluded).HasDefaultValue(0);
        builder.Property(e => e.CandidatesExcludedJson).HasColumnType("jsonb");
        builder.Property(e => e.ExcludeNote).HasMaxLength(1023);
        builder.Property(e => e.RefusingHosts).HasDefaultValue(0);
        builder.Property(e => e.RefusingHostsJson).HasColumnType("jsonb");

        builder.HasIndex(e => e.StartedUtc);
    }
}

public class SourceRightsRecordEntityConfiguration : IEntityTypeConfiguration<SourceRightsRecordEntity>
{
    public void Configure(EntityTypeBuilder<SourceRightsRecordEntity> builder)
    {
        builder.ToTable("SourceRightsRecord", schema: "recipe");

        builder.Property(e => e.Host).IsRequired().HasMaxLength(253);
        builder.Property(e => e.RightsClass).IsRequired().HasMaxLength(24);
        builder.Property(e => e.Quote).IsRequired().HasMaxLength(1023);
        builder.Property(e => e.QuoteUrl).IsRequired().HasMaxLength(2047);
        builder.HasIndex(e => e.Host);
    }
}

public class SourceRefusingHostEntityConfiguration : IEntityTypeConfiguration<SourceRefusingHostEntity>
{
    public void Configure(EntityTypeBuilder<SourceRefusingHostEntity> builder)
    {
        builder.ToTable("SourceRefusingHost", schema: "recipe");

        builder.Property(e => e.Host).IsRequired().HasMaxLength(253);
        builder.HasIndex(e => e.Host).IsUnique();
        builder.Property(e => e.LastUrl).HasMaxLength(2047);
        builder.Property(e => e.LastBotChallenge).HasDefaultValue(false);
        builder.Property(e => e.ReleasedNote).HasMaxLength(1023);
    }
}
