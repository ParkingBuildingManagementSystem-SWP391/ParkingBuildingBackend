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
            entity.HasKey(e => e.FloorId).HasName("PK__Floors__49D1E84B78EF6B82");

            entity.Property(e => e.FloorName).HasMaxLength(255);
        });

        modelBuilder.Entity<IncidentReport>(entity =>
        {
            entity.HasKey(e => e.IncidentId).HasName("PK__Incident__3D8053B252C903A9");

            entity.Property(e => e.IssueType).HasMaxLength(255);
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .IsUnicode(false);

            entity.HasOne(d => d.Reported).WithMany(p => p.IncidentReportReporteds)
                .HasForeignKey(d => d.ReportedId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__IncidentR__Repor__6754599E");

            entity.HasOne(d => d.Resolved).WithMany(p => p.IncidentReportResolveds)
                .HasForeignKey(d => d.ResolvedId)
                .HasConstraintName("FK__IncidentR__Resol__68487DD7");

            entity.HasOne(d => d.Session).WithMany(p => p.IncidentReports)
                .HasForeignKey(d => d.SessionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__IncidentR__Sessi__693CA210");
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.InvoiceId).HasName("PK__Invoices__D796AAB5DD459146");

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
                .IsRequired(false) // Thêm dòng này để cho phép SessionId rỗng trong
                .HasConstraintName("FK__Invoices__Sessio__6A30C649");

            entity.HasOne(d => d.Staff).WithMany(p => p.Invoices)
                .HasForeignKey(d => d.StaffId)
                .HasConstraintName("FK__Invoices__StaffI__6B24EA82");
        });

        modelBuilder.Entity<MonthlyCard>(entity =>
        {
            entity.HasKey(e => e.MonthlyCardId).HasName("PK__MonthlyC__D4771EA66B1BD21D");

            entity.HasIndex(e => e.SlotId, "UQ_Active_Monthly_Slot")
                .IsUnique()
                .HasFilter("([Status]='Active' AND [IsDeleted]=(0))");

            entity.Property(e => e.EndTime).HasColumnType("datetime");
            entity.Property(e => e.LicenseVehicle)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.StartTime)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .IsUnicode(false);

            entity.HasOne(d => d.Slot).WithOne(p => p.MonthlyCard)
                .HasForeignKey<MonthlyCard>(d => d.SlotId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MonthlyCards_ParkingSlots");

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
            entity.HasKey(e => e.TariffId).HasName("PK__MonthlyT__EBAF9DB3501EFC4F");

            entity.Property(e => e.MonthlyPrice).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.Type).WithMany(p => p.MonthlyTariffs)
                .HasForeignKey(d => d.TypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MonthlyTariffs_VehiclesType");
        });

        modelBuilder.Entity<ParkingSession>(entity =>
        {
            entity.HasKey(e => e.SessionId).HasName("PK__ParkingS__C9F49290D5847980");

            entity.ToTable("ParkingSession");

            entity.HasIndex(e => e.TicketId, "UQ__ParkingS__712CC60608E26A58").IsUnique();

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
                .HasConstraintName("FK__ParkingSe__SlotI__6C190EBB");

            entity.HasOne(d => d.Ticket).WithOne(p => p.ParkingSession)
                .HasForeignKey<ParkingSession>(d => d.TicketId)
                .HasConstraintName("FK_ParkingSession_Tickets");

            entity.HasOne(d => d.Type).WithMany(p => p.ParkingSessions)
                .HasForeignKey(d => d.TypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ParkingSe__TypeI__6D0D32F4");

            entity.HasOne(d => d.User).WithMany(p => p.ParkingSessions)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK__ParkingSe__UserI__6E01572D");
        });

        modelBuilder.Entity<ParkingSlot>(entity =>
        {
            entity.HasKey(e => e.SlotId).HasName("PK__ParkingS__0A124AAFC83FDD0E");

            entity.Property(e => e.SlotName).HasMaxLength(255);
            entity.Property(e => e.SlotStatus)
                .HasMaxLength(50)
                .IsUnicode(false);

            entity.HasOne(d => d.Floor).WithMany(p => p.ParkingSlots)
                .HasForeignKey(d => d.FloorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ParkingSl__Floor__6FE99F9F");

            entity.HasOne(d => d.Type).WithMany(p => p.ParkingSlots)
                .HasForeignKey(d => d.TypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ParkingSl__TypeI__70DDC3D8");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleId).HasName("PK__Roles__8AFACE1ABAF75769");

            entity.Property(e => e.RoleName).HasMaxLength(255);
        });

        modelBuilder.Entity<Ticket>(entity =>
        {
            entity.HasKey(e => e.TicketId).HasName("PK__Tickets__712CC607FAAB2619");

            entity.Property(e => e.TicketCode)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.TicketStatus)
                .HasMaxLength(50)
                .IsUnicode(false);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__Users__1788CC4C9EBD1730");

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
                .HasConstraintName("FK__Users__RoleId__71D1E811");
        });

        modelBuilder.Entity<VehiclesType>(entity =>
        {
            entity.HasKey(e => e.TypeId).HasName("PK__Vehicles__516F03B57D1E1248");

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
