using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace ParkingBuilding.Repository.Entities;

public partial class ParkingManagementDbContext : DbContext
{
    public ParkingManagementDbContext(DbContextOptions<ParkingManagementDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Floor> Floors { get; set; }

    public virtual DbSet<IncidentReport> IncidentReports { get; set; }

    public virtual DbSet<Invoice> Invoices { get; set; }

    public virtual DbSet<MonthlyCard> MonthlyCards { get; set; }

    public virtual DbSet<MonthlyTariff> MonthlyTariffs { get; set; }

    public virtual DbSet<ParkingSession> ParkingSessions { get; set; }

    public virtual DbSet<ParkingSlot> ParkingSlots { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Ticket> Tickets { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<VehiclesType> VehiclesTypes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Floor>(entity =>
        {
            entity.HasKey(e => e.FloorId).HasName("PK__Floors__49D1E84B082C288C");

            entity.Property(e => e.FloorName).HasMaxLength(255);
        });

        modelBuilder.Entity<IncidentReport>(entity =>
        {
            entity.HasKey(e => e.IncidentId).HasName("PK__Incident__3D8053B29AAE387E");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.FineAmount)
                .HasDefaultValue(0.00m)
                .HasColumnType("decimal(18, 2)");
            entity.Property(e => e.ImageProofUrl).HasMaxLength(500);
            entity.Property(e => e.IssueType).HasMaxLength(255);
            entity.Property(e => e.ResolvedAt).HasColumnType("datetime");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .IsUnicode(false);

            entity.HasOne(d => d.Reported).WithMany(p => p.IncidentReportReporteds)
                .HasForeignKey(d => d.ReportedId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__IncidentR__Repor__60A75C0F");

            entity.HasOne(d => d.Resolved).WithMany(p => p.IncidentReportResolveds)
                .HasForeignKey(d => d.ResolvedId)
                .HasConstraintName("FK__IncidentR__Resol__619B8048");

            entity.HasOne(d => d.Session).WithMany(p => p.IncidentReports)
                .HasForeignKey(d => d.SessionId)
                .IsRequired(false) // CHỈNH SỬA: Cho phép SessionId Nullable trong Entity Framework
                .HasConstraintName("FK__IncidentR__Sessi__628FA481");
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.InvoiceId).HasName("PK__Invoices__D796AAB5DDE57D88");

            entity.HasIndex(e => e.SessionId, "UQ_Invoices_SessionId_Filtered")
                .IsUnique()
                .HasFilter("([SessionId] IS NOT NULL)");

            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.PaymentMethod)
                .HasMaxLength(50)
                .HasDefaultValue("CASH");
            entity.Property(e => e.PaymentStatus)
                .HasMaxLength(50)
                .HasDefaultValue("PENDING");
            entity.Property(e => e.PaymentTime).HasColumnType("datetime");
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.TransactionCode).HasMaxLength(100);
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");

            entity.HasOne(d => d.Session).WithOne(p => p.Invoice)
                .HasForeignKey<Invoice>(d => d.SessionId)
                .IsRequired(false) // CHỈNH SỬA: Cho phép SessionId Nullable trong Entity Framework
                .HasConstraintName("FK__Invoices__Sessio__6383C8BA");

            entity.HasOne(d => d.Staff).WithMany(p => p.Invoices)
                .HasForeignKey(d => d.StaffId)
                .HasConstraintName("FK__Invoices__StaffI__6477ECF3");
        });

        modelBuilder.Entity<MonthlyCard>(entity =>
        {
            entity.HasKey(e => e.MonthlyCardId).HasName("PK__MonthlyC__D4771EA68DE952B9");

            entity.Property(e => e.EndTime).HasColumnType("datetime");
            entity.Property(e => e.StartTime)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .IsUnicode(false);

            entity.HasOne(d => d.Tariff).WithMany(p => p.MonthlyCards)
                .HasForeignKey(d => d.TariffId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MonthlyCards_MonthlyTariffs");

            entity.HasOne(d => d.Ticket).WithMany(p => p.MonthlyCards)
                .HasForeignKey(d => d.TicketId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MonthlyCards_Tickets");

            entity.HasOne(d => d.User).WithMany(p => p.MonthlyCards)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MonthlyCards_Users");
        });

        modelBuilder.Entity<MonthlyTariff>(entity =>
        {
            entity.HasKey(e => e.TariffId).HasName("PK__MonthlyT__EBAF9DB35B465677");

            entity.Property(e => e.MonthlyPrice).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.Type).WithMany(p => p.MonthlyTariffs)
                .HasForeignKey(d => d.TypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MonthlyTariffs_VehiclesType");
        });

        modelBuilder.Entity<ParkingSession>(entity =>
        {
            entity.HasKey(e => e.SessionId).HasName("PK__ParkingS__C9F4929061E687F5");

            entity.ToTable("ParkingSession");

            entity.HasIndex(e => e.TicketId, "UQ__ParkingS__712CC60626606947").IsUnique();

            entity.Property(e => e.BookingTime).HasColumnType("datetime");
            entity.Property(e => e.CheckInImageUrl).HasMaxLength(500);
            entity.Property(e => e.CheckInTime).HasColumnType("datetime");
            entity.Property(e => e.CheckOutImageUrl).HasMaxLength(500);
            entity.Property(e => e.CheckOutTime).HasColumnType("datetime");
            entity.Property(e => e.ExpectedCheckInTime).HasColumnType("datetime");
            entity.Property(e => e.LicenseVehicle)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.SessionStatus)
                .HasMaxLength(50)
                .IsUnicode(false);

            entity.HasOne(d => d.Slot).WithMany(p => p.ParkingSessions)
                .HasForeignKey(d => d.SlotId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ParkingSe__SlotI__656C112C");

            entity.HasOne(d => d.Ticket).WithOne(p => p.ParkingSession)
                .HasForeignKey<ParkingSession>(d => d.TicketId)
                .HasConstraintName("FK_ParkingSession_Tickets");

            entity.HasOne(d => d.Type).WithMany(p => p.ParkingSessions)
                .HasForeignKey(d => d.TypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ParkingSe__TypeI__6754599E");

            entity.HasOne(d => d.User).WithMany(p => p.ParkingSessions)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK__ParkingSe__UserI__68487DD7");
        });

        modelBuilder.Entity<ParkingSlot>(entity =>
        {
            entity.HasKey(e => e.SlotId).HasName("PK__ParkingS__0A124AAF455861B9");

            entity.Property(e => e.SlotName).HasMaxLength(255);
            entity.Property(e => e.SlotStatus)
                .HasMaxLength(50)
                .IsUnicode(false);

            entity.HasOne(d => d.Floor).WithMany(p => p.ParkingSlots)
                .HasForeignKey(d => d.FloorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ParkingSl__Floor__693CA210");

            entity.HasOne(d => d.Type).WithMany(p => p.ParkingSlots)
                .HasForeignKey(d => d.TypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ParkingSl__TypeI__6A30C649");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleId).HasName("PK__Roles__8AFACE1AA4E7EFE5");

            entity.Property(e => e.RoleName).HasMaxLength(255);
        });

        modelBuilder.Entity<Ticket>(entity =>
        {
            entity.HasKey(e => e.TicketId).HasName("PK__Tickets__712CC607578A8D8D");

            entity.Property(e => e.TicketCode)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.TicketStatus)
                .HasMaxLength(50)
                .IsUnicode(false);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__Users__1788CC4C4FE92D3D");

            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(64)
                .IsUnicode(false);
            entity.Property(e => e.PhoneNumber)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.Username)
                .HasMaxLength(255)
                .IsUnicode(false);

            entity.HasOne(d => d.Role).WithMany(p => p.Users)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Users__RoleId__6B24EA82");
        });

        modelBuilder.Entity<VehiclesType>(entity =>
        {
            entity.HasKey(e => e.TypeId).HasName("PK__Vehicles__516F03B51C1728D8");

            entity.ToTable("VehiclesType");

            entity.Property(e => e.DayRate).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.FullDayRate).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.NightRate).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.TypeName).HasMaxLength(255);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
