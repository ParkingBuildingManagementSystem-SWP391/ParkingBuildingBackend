using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace ParkingBuilding.Repository.Entities;

public partial class ParkingManagementDbContext : DbContext
{
    public ParkingManagementDbContext()
    {
    }

    public ParkingManagementDbContext(DbContextOptions<ParkingManagementDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Floor> Floors { get; set; }

    public virtual DbSet<IncidentReport> IncidentReports { get; set; }

    public virtual DbSet<Invoice> Invoices { get; set; }

    public virtual DbSet<ParkingSession> ParkingSessions { get; set; }

    public virtual DbSet<ParkingSlot> ParkingSlots { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Ticket> Tickets { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<VehiclesType> VehiclesTypes { get; set; }

    //    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    //#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
    //        => optionsBuilder.UseSqlServer("Server=LAPTOP-UU1O8321\\SQLEXPRESS;Database=ParkingManagementDb;User Id=sa;Password=12345;TrustServerCertificate=True");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {


        


        modelBuilder.Entity<Floor>(entity =>
        {
            entity.HasKey(e => e.FloorId).HasName("PK__Floors__49D1E84B8AF34A55");

            entity.Property(e => e.FloorName).HasMaxLength(255);
        });

        modelBuilder.Entity<IncidentReport>(entity =>
        {
            entity.HasKey(e => e.IncidentId).HasName("PK__Incident__3D8053B2CCAC326A");

            entity.Property(e => e.IssueType).HasMaxLength(255);
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .IsUnicode(false);

            entity.HasOne(d => d.Reported).WithMany(p => p.IncidentReportReporteds)
                .HasForeignKey(d => d.ReportedId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__IncidentR__Repor__6D0D32F4");

            entity.HasOne(d => d.Resolved).WithMany(p => p.IncidentReportResolveds)
                .HasForeignKey(d => d.ResolvedId)
                .HasConstraintName("FK__IncidentR__Resol__6E01572D");

            entity.HasOne(d => d.Session).WithMany(p => p.IncidentReports)
                .HasForeignKey(d => d.SessionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__IncidentR__Sessi__6C190EBB");
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.InvoiceId).HasName("PK__Invoices__D796AAB563280088");

            entity.HasIndex(e => e.SessionId, "UQ__Invoices__C9F4929189173667").IsUnique();

            entity.Property(e => e.PaymentTime).HasColumnType("datetime");
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 2)");

            entity.Property(e => e.PaymentMethod).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.PaymentStatus).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.TransactionCode).HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.CreatedDate).HasColumnType("datetime");
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");



            entity.HasOne(d => d.Session).WithOne(p => p.Invoice)
                .HasForeignKey<Invoice>(d => d.SessionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Invoices__Sessio__68487DD7");

            entity.HasOne(d => d.Staff).WithMany(p => p.Invoices)
                .HasForeignKey(d => d.StaffId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK__Invoices__StaffI__693CA210");
        });

        modelBuilder.Entity<ParkingSession>(entity =>
        {
            entity.HasKey(e => e.SessionId).HasName("PK__ParkingS__C9F492904ACFA3CA");

            entity.ToTable("ParkingSession");

            entity.Property(e => e.BookingTime).HasColumnType("datetime");
            entity.Property(e => e.CheckInImageUrl)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.CheckInTime).HasColumnType("datetime");
            entity.Property(e => e.CheckOutImageUrl)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.CheckOutTime).HasColumnType("datetime");
            entity.Property(e => e.LicenseVehicle)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.SessionStatus)
                .HasMaxLength(50)
                .IsUnicode(false);

            entity.HasOne(d => d.Slot).WithMany(p => p.ParkingSessions)
                .HasForeignKey(d => d.SlotId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ParkingSe__SlotI__619B8048");

            entity.HasOne(d => d.Ticket).WithMany(p => p.ParkingSession)
            .HasForeignKey(d => d.TicketId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_ParkingSession_Tickets");


            entity.HasOne(d => d.Type).WithMany(p => p.ParkingSessions)
                .HasForeignKey(d => d.TypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ParkingSe__TypeI__628FA481");

            entity.HasOne(d => d.User).WithMany(p => p.ParkingSessions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ParkingSe__UserI__60A75C0F");
        });

        modelBuilder.Entity<ParkingSlot>(entity =>
        {
            entity.HasKey(e => e.SlotId).HasName("PK__ParkingS__0A124AAF372C549A");

            entity.Property(e => e.SlotName).HasMaxLength(255);
            entity.Property(e => e.SlotStatus)
                .HasMaxLength(50)
                .IsUnicode(false);

            entity.HasOne(d => d.Floor).WithMany(p => p.ParkingSlots)
                .HasForeignKey(d => d.FloorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ParkingSl__Floor__5812160E");

            entity.HasOne(d => d.Type).WithMany(p => p.ParkingSlots)
                .HasForeignKey(d => d.TypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ParkingSl__TypeI__59063A47");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleId).HasName("PK__Roles__8AFACE1A008BDA2F");

            entity.Property(e => e.RoleName).HasMaxLength(255);
        });

        modelBuilder.Entity<Ticket>(entity =>
        {
            entity.HasKey(e => e.TicketId).HasName("PK__Tickets__712CC607227F4409");

            entity.Property(e => e.TicketId)
           .ValueGeneratedOnAdd();

            entity.Property(e => e.TicketCode)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.TicketStatus)
                .HasMaxLength(50)
                .IsUnicode(false);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__Users__1788CC4C4B8F9513");

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
                .HasConstraintName("FK__Users__RoleId__4E88ABD4");
        });

        modelBuilder.Entity<VehiclesType>(entity =>
        {
            entity.HasKey(e => e.TypeId).HasName("PK__Vehicles__516F03B5E8B37B62");

            entity.ToTable("VehiclesType");

            entity.Property(e => e.TypeName).HasMaxLength(255);
            entity.Property(e => e.DayRate).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.NightRate).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.FullDayRate).HasColumnType("decimal(18, 2)");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
