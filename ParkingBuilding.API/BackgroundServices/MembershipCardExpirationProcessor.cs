using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ParkingBuilding.Repository.Entities;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ParkingBuilding.API.BackgroundServices
{
    /// <summary>
    /// Background worker that expires membership cards and releases slots held by expired
    /// pending membership payment transactions.
    /// </summary>
    public class MembershipCardExpirationProcessor : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MembershipCardExpirationProcessor> _logger;

        public MembershipCardExpirationProcessor(IServiceProvider sp, ILogger<MembershipCardExpirationProcessor> logger)
        {
            _serviceProvider = sp;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Membership card expiration processor background service has started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var context = scope.ServiceProvider.GetRequiredService<ParkingManagementDbContext>();

                        var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTimeZone);

                        var expiredCards = await context.MembershipCards
                            .Include(mc => mc.MembershipVehicles)
                            .Include(mc => mc.MembershipSlots)
                                .ThenInclude(ms => ms.Slot)
                            .Where(mc => mc.Status == ParkingStatuses.MonthlyCardActive
                                         && !mc.IsDeleted
                                         && mc.EndTime < localNow)
                            .ToListAsync(stoppingToken);

                        if (expiredCards.Any())
                        {
                            _logger.LogInformation("Found {Count} expired membership cards. Releasing resources.", expiredCards.Count);

                            foreach (var card in expiredCards)
                            {
                                card.Status = ParkingStatuses.MonthlyCardExpired;

                                foreach (var membershipSlot in card.MembershipSlots)
                                {
                                    var slot = membershipSlot.Slot;
                                    if (slot != null && (slot.SlotStatus == ParkingStatuses.SlotReserved || slot.SlotStatus == ParkingStatuses.SlotAvailable))
                                    {
                                        slot.SlotStatus = ParkingStatuses.SlotAvailable;
                                    }
                                }

                                foreach (var vehicle in card.MembershipVehicles)
                                {
                                    vehicle.IsActive = false;
                                }

                                _logger.LogInformation("Expired membership card {CardId} for user {UserId}.", card.MembershipCardId, card.UserId);
                            }

                            await context.SaveChangesAsync(stoppingToken);
                        }

                        var expiredPendingInvoices = await context.Invoices
                            .Where(i => i.PaymentMethod == "VNPAY"
                                        && i.PaymentStatus == "PENDING"
                                        && i.CreatedDate < localNow.AddMinutes(-15)
                                        && i.TransactionCode != null
                                        && i.TransactionCode.StartsWith("MBC_"))
                            .ToListAsync(stoppingToken);

                        if (expiredPendingInvoices.Any())
                        {
                            _logger.LogInformation("Found {Count} expired pending membership transactions.", expiredPendingInvoices.Count);

                            foreach (var invoice in expiredPendingInvoices)
                            {
                                invoice.PaymentStatus = "FAILED";
                                invoice.UpdatedDate = localNow;

                                var tempPrefix = $"TEMP_{invoice.TransactionCode}_";
                                var tickets = await context.Tickets
                                    .Include(t => t.MembershipCard)
                                        .ThenInclude(mc => mc!.MembershipSlots)
                                    .Where(t => t.TicketCode.StartsWith(tempPrefix))
                                    .ToListAsync(stoppingToken);

                                foreach (var ticket in tickets)
                                {
                                    ticket.TicketStatus = ParkingStatuses.TicketExpired;
                                    if (ticket.MembershipCard == null)
                                    {
                                        continue;
                                    }

                                    ticket.MembershipCard.Status = ParkingStatuses.MonthlyCardExpired;

                                    foreach (var membershipSlot in ticket.MembershipCard.MembershipSlots)
                                    {
                                        var slot = await context.ParkingSlots.FirstOrDefaultAsync(s => s.SlotId == membershipSlot.SlotId, stoppingToken);
                                        if (slot != null && slot.SlotStatus == ParkingStatuses.SlotReserved)
                                        {
                                            slot.SlotStatus = ParkingStatuses.SlotAvailable;
                                            _logger.LogInformation(
                                                "Released slot {SlotName} (ID: {SlotId}) from expired transaction {TransactionCode}.",
                                                slot.SlotName,
                                                slot.SlotId,
                                                invoice.TransactionCode);
                                        }
                                    }
                                }
                            }

                            await context.SaveChangesAsync(stoppingToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in MembershipCardExpirationProcessor.");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
