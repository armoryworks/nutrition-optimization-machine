using Microsoft.EntityFrameworkCore;
using Nom.Data.Reference;

namespace Nom.Data
{
    public class ReferenceContext : DbContext
    {
        DbSet<Nom.Data.Reference.Reference> References { get; set; }
        DbSet<Group> Groups { get; set; }

        public ReferenceContext(DbContextOptions<ReferenceContext> options) : base(options)
        {

        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.HasDefaultSchema("reference");

            builder.Entity<Reference.Reference>()
                    .HasMany(e => e.Groups)
                    .WithMany(e => e.References)
                    .UsingEntity(
                        "ReferenceGroupIx",
                        r => r.HasOne(typeof(Reference.Reference)).WithMany().HasForeignKey("ReferenceId").HasPrincipalKey(nameof(Reference.Reference.Id)),
                        l => l.HasOne(typeof(Group)).WithMany().HasForeignKey("GroupId").HasPrincipalKey(nameof(Group.Id)),
                        jt => jt.Ignore("Id")
                                .HasKey("GroupId", "ReferenceId"));
        }
    }
}