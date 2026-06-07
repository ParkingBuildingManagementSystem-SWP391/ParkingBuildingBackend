
﻿using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Service.IService;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service
{
    /// <summary>
    /// Lớp nghiệp vụ cấp phát mã thông báo JWT (JSON Web Token) cho quá trình xác thực người dùng.
    /// </summary>
    public class TokenService : ITokenService
    {
        private readonly IConfiguration _config;

        public TokenService(IConfiguration config)
        {
            _config = config;
        }

        /// <summary>
        /// Sinh mã JWT Token bất đối xứng chứa các Claims (UserId, Email, Username, Role) với thời hạn hết hạn là 1 ngày.
        /// </summary>
        public string GenerateJwtToken(User user)
        {
            var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role?.RoleName ?? "Registered_Driver") 
        };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["JwtSettings:Secret"] ?? "DefaultSuperSecretKeyThatIsAtLeast32BytesLong"));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["JwtSettings:Issuer"],
                audience: _config["JwtSettings:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddDays(1), 
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
