using ParkingBuilding.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using ParkingBuilding.Repository;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Repository.Repository;
using ParkingBuilding.Service.IService;
using ParkingBuilding.Service.Service;
using ParkingBuilding.API.BackgroundServices;

namespace ParkingBuilding.API
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // 1. Lấy Connection String từ appsettings.json
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

            // 2. Đăng ký DbContext vào DI Container
            builder.Services.AddDbContext<ParkingManagementDbContext>(options =>
                options.UseSqlServer(connectionString));

            // booking
            // 3. Đăng ký Dependency Injection cho Lớp Repository
            // Sử dụng AddScoped để quản lý vòng đời theo Request HTTP
            builder.Services.AddScoped<IParkingRepository, ParkingRepository>();

            // 4. Đăng ký Dependency Injection cho Lớp Service
            builder.Services.AddScoped<IParkingService, ParkingService>();

            // 5. Đăng ký BackgroundService chạy ngầm (Quét và tự động hủy đơn sau 15p)
            builder.Services.AddHostedService<BookingCancellationService>();


            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }



            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}
