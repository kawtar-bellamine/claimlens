using ClaimLens.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClaimLens.Api.Data;

public sealed class ClaimsDb(DbContextOptions<ClaimsDb> options) : DbContext(options)
{
    public DbSet<Claim> Claims => Set<Claim>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        var claim = b.Entity<Claim>();
        claim.Property(c => c.TypeSinistre).HasMaxLength(40);
        claim.Property(c => c.Urgence).HasMaxLength(20);
        claim.Property(c => c.Statut).HasMaxLength(40);
        claim.HasIndex(c => c.CreeLe);
    }
}
