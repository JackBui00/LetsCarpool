using System;
using System.Collections.Generic;
using LetsCarpool.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace LetsCarpool.Server.Data;

public partial class YourDbContextName : DbContext
{
    public YourDbContextName()
    {
    }

    public YourDbContextName(DbContextOptions<YourDbContextName> options)
        : base(options)
    {
    }

    public virtual DbSet<AuthTokens> AuthTokens { get; set; }

    public virtual DbSet<CarpoolMatches> CarpoolMatches { get; set; }

    public virtual DbSet<Locations> Locations { get; set; }

    public virtual DbSet<Notifications> Notifications { get; set; }

    public virtual DbSet<RideHistory> RideHistory { get; set; }

    public virtual DbSet<RidePreferences> RidePreferences { get; set; }

    public virtual DbSet<RideRequests> RideRequests { get; set; }

    public virtual DbSet<Roles> Roles { get; set; }

    public virtual DbSet<Users> Users { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlServer("Server=tcp:letscarpooldb.database.windows.net;Database=carpoolDB;Authentication=Active Directory Interactive");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuthTokens>(entity =>
        {
            entity.HasKey(e => e.TokenId);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.ExpirationDate).HasColumnType("datetime");
            entity.Property(e => e.RefreshToken).HasMaxLength(255);
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.User).WithMany(p => p.AuthTokens)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_AuthTokens_UserId_Users");
        });

        modelBuilder.Entity<CarpoolMatches>(entity =>
        {
            entity.HasKey(e => e.CarpoolMatchId);

            entity.HasIndex(e => e.DriverId, "IX_CarpoolMatches_DriverId");

            entity.HasIndex(e => e.RiderId, "IX_CarpoolMatches_RiderId");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.MatchStatus).HasMaxLength(50);
            entity.Property(e => e.MatchTime).HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.Driver).WithMany(p => p.CarpoolMatchesDriver)
                .HasForeignKey(d => d.DriverId)
                .HasConstraintName("FK_CarpoolMatches_DriverId_Users");

            entity.HasOne(d => d.RideRequest).WithMany(p => p.CarpoolMatches)
                .HasForeignKey(d => d.RideRequestId)
                .HasConstraintName("FK_CarpoolMatches_RideRequestId_RideRequests");

            entity.HasOne(d => d.Rider).WithMany(p => p.CarpoolMatchesRider)
                .HasForeignKey(d => d.RiderId)
                .HasConstraintName("FK_CarpoolMatches_RiderId_Users");
        });

        modelBuilder.Entity<Locations>(entity =>
        {
            entity.HasKey(e => e.LocationId);

            entity.HasIndex(e => e.UserId, "IX_Locations_UserId");

            entity.Property(e => e.Address).HasMaxLength(255);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.LocationName).HasMaxLength(50);
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.User).WithMany(p => p.Locations)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_Locations_UserId_Users");
        });

        modelBuilder.Entity<Notifications>(entity =>
        {
            entity.HasKey(e => e.NotificationId);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.IsRead).HasDefaultValue(false);
            entity.Property(e => e.Message).HasMaxLength(500);
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.User).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_Notifications_UserId_Users");
        });

        modelBuilder.Entity<RideHistory>(entity =>
        {
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.EndLocation).HasMaxLength(255);
            entity.Property(e => e.RideEndTime).HasColumnType("datetime");
            entity.Property(e => e.RideStartTime).HasColumnType("datetime");
            entity.Property(e => e.StartLocation).HasMaxLength(255);
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.Driver).WithMany(p => p.RideHistoryDriver)
                .HasForeignKey(d => d.DriverId)
                .HasConstraintName("FK_RideHistory_DriverId_Users");

            entity.HasOne(d => d.Rider).WithMany(p => p.RideHistoryRider)
                .HasForeignKey(d => d.RiderId)
                .HasConstraintName("FK_RideHistory_RiderId_Users");
        });

        modelBuilder.Entity<RidePreferences>(entity =>
        {
            entity.HasKey(e => e.PreferenceId);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.PreferredPickupLocation).HasMaxLength(255);
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.User).WithMany(p => p.RidePreferences)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_RidePreferences_UserId_Users");
        });

        modelBuilder.Entity<RideRequests>(entity =>
        {
            entity.HasKey(e => e.RideRequestId);

            entity.HasIndex(e => e.UserId, "IX_RideRequests_UserId");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.DestinationLocation).HasMaxLength(255);
            entity.Property(e => e.PickupLocation).HasMaxLength(255);
            entity.Property(e => e.PreferredPickupTime).HasColumnType("datetime");
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.User).WithMany(p => p.RideRequests)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_RideRequests_UserId_Users");
        });

        modelBuilder.Entity<Roles>(entity =>
        {
            entity.HasKey(e => e.RoleId);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.RoleName).HasMaxLength(100);
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
        });

        modelBuilder.Entity<Users>(entity =>
        {
            entity.HasKey(e => e.UserId);

            entity.HasIndex(e => e.Email, "IX_Users_Email");

            entity.HasIndex(e => e.Email, "UQ_Users_Email").IsUnique();

            entity.HasIndex(e => e.Email, "UQ__Users__A9D105344BA86A33").IsUnique();

            entity.Property(e => e.Address).HasMaxLength(255);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.PasswordHash).HasMaxLength(255);
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
