using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

using Microsoft.IdentityModel.Tokens;
using ParkingBuilding.API.BackgroundServices;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Repository.Repository;
using ParkingBuilding.Service.IService;
using ParkingBuilding.Service.Service;

using System.Text;

namespace ParkingBuilding.API
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddMemoryCache();

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

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowClient", policy =>
                {
                    policy.WithOrigins("https://localhost:7008") // URL của Frontend
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                });
            });




            builder.Services.AddScoped<IUserRepository, UserRepository>();
            builder.Services.AddScoped<IEmailService, EmailService>();
            builder.Services.AddScoped<ITokenService, TokenService>();
            builder.Services.AddScoped<IAuthService, AuthService>();

            var jwtSecret = builder.Configuration["JwtSettings:Secret"] ?? "DefaultSuperSecretKeyThatIsAtLeast32BytesLong";
            var key = Encoding.UTF8.GetBytes(jwtSecret);

            builder.Services.AddScoped<IUserRepository, UserRepository>();
            builder.Services.AddScoped<IEmailService, EmailService>();
            builder.Services.AddScoped<ITokenService, TokenService>();
            builder.Services.AddScoped<IAuthService, AuthService>();

            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })

            .AddJwtBearer(options =>
             {
                 options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                 {
                     ValidateIssuer = true,
                     ValidateAudience = true,
                     ValidateLifetime = true,
                     ValidateIssuerSigningKey = true,
                     ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
                     ValidAudience = builder.Configuration["JwtSettings:Audience"],
                     IssuerSigningKey = new SymmetricSecurityKey(key),
                     ClockSkew = TimeSpan.Zero // Không cho phép độ lệch thời gian hết hạn
                 };
             });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }


            app.UseCors("AllowClient");
            app.UseHttpsRedirection();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}
