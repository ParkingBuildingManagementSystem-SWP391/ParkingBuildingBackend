using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using ParkingBuilding.API.Filters;
using Microsoft.IdentityModel.Tokens;
using ParkingBuilding.API.BackgroundServices;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Repository.Repository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using ParkingBuilding.Service.Service;

using System.Text;
using ParkingBuilding.Service.Helpers;

namespace ParkingBuilding.API
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddMemoryCache();

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();

            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
            

            builder.Services.AddDbContext<ParkingManagementDbContext>(options =>
                options.UseSqlServer(connectionString));

            builder.Services.AddScoped<IParkingRepository, ParkingRepository>();

            builder.Services.AddScoped<IVnPayService, VnPayService>();
            builder.Services.AddScoped<IBookingService, BookingService>();
            builder.Services.AddScoped<ICheckInService, CheckInService>();
            builder.Services.AddScoped<ICheckOutService, CheckOutService>();
            builder.Services.AddScoped<IParkingQueryService, ParkingQueryService>();

            builder.Services.AddHostedService<BookingCancellationProcessor>();
            builder.Services.AddHostedService<MonthlyCardExpirationProcessor>();

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowClient", policy =>
                {
                    policy
                        .WithOrigins(
                            "https://localhost:7008",                   
                            "https://mindy.huydevops.id.vn",            
                            "https://parking-building-frontend.vercel.app", 
                            "https://parkingbuilding.vercel.app"          
                        )
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                });
            });

            builder.Services.AddScoped<IUserRepository, UserRepository>();

            builder.Services.AddScoped<IEmailService, EmailService>();

            builder.Services.AddScoped<ITokenService, TokenService>();

            builder.Services.AddScoped<IAuthService, AuthService>();

            builder.Services.AddScoped<IAdminService, AdminService>();

            // Đăng ký cấu hình VnPayConfig từ appsettings.json
            builder.Services.Configure<VnPayConfig>(builder.Configuration.GetSection("VnPayConfig"));

            // Đăng ký dịch vụ thanh toán (Service Layer)
            builder.Services.AddScoped<IPaymentService, PaymentService>();
            builder.Services.AddScoped<IMonthlyCardService, MonthlyCardService>();

            // Đăng ký các Repository riêng lẻ (Repository Layer)
            builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();
            builder.Services.AddScoped<ISessionRepository, SessionRepository>();
            builder.Services.AddScoped<ISlotRepository, SlotRepository>();

            // Đăng ký Repository của phân hệ Manager
            builder.Services.AddScoped<IManagerRepository, ManagerRepository>();

            // Đăng ký Service của phân hệ Manager
            builder.Services.AddScoped<IManagerService, ManagerService>();

            // Đăng ký Repository IncidentReport
            builder.Services.AddScoped<IIncidentReportRepository, IncidentReportRepository>();

            // Đăng ký Service IncidentReport
            builder.Services.AddScoped<IIncidentReportService, IncidentReportService>();

            // Đăng ký UnitOfWork
            builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

            // Đăng ký cấu hình Cloudinary
            builder.Services.Configure<CloudinarySettings>(builder.Configuration.GetSection("CloudinarySettings"));

            // Đăng ký dịch vụ lưu trữ hình ảnh Cloudinary
            builder.Services.AddScoped<IImageStorageService, CloudinaryStorageService>();

            // Đăng ký HttpClient kèm cấu hình Timeout tối đa 10 giây để nhận dạng nhanh hơn và tránh treo cổng xe
            builder.Services.AddHttpClient<IAiRecognitionService, FastApiLicensePlateService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            });


            var jwtSecret = builder.Configuration["JwtSettings:Secret"] ?? "DefaultSuperSecretKeyThatIsAtLeast32BytesLong";
            var key = Encoding.UTF8.GetBytes(jwtSecret);



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
                     ClockSkew = TimeSpan.Zero 
                 };
             });

            builder.Services.AddSwaggerGen(options =>
            {
                options.SchemaFilter<DefaultStringSchemaFilter>();
                options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                    Description = "Nhập 'Bearer' [khoảng trắng] rồi đến token của bạn.\n\nVí dụ: Bearer eyJhbGciOiJIUzI1Ni..."
                });

                options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
                {
                    {
                        new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                        {
                            Reference = new Microsoft.OpenApi.Models.OpenApiReference
                            {
                                Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        new string[] {}
                    }
                 });
            });


            builder.Services.AddAuthorization();

            var app = builder.Build();

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
