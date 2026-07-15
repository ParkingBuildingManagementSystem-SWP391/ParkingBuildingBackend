using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;

namespace ParkingBuilding.Repository.Repository
{
    public class NotificationRepository : GenericRepository<Notification>, INotificationRepository
    {
        public NotificationRepository(ParkingManagementDbContext context) : base(context)
        {
        }
    }
}
