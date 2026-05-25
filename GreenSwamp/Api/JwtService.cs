using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using GreenSwamp.Models;

namespace GreenSwamp.Api
{
    // Сервис для создания и валидации JWT-токенов.
    // JWT (JSON Web Token) — стандарт для передачи аутентификационных данных.
    // Токен состоит из трёх частей: header.payload.signature
    // Клиент хранит токен и отправляет его в заголовке: Authorization: Bearer <token>
    public interface IJwtService
    {
        string GenerateToken(User user);
    }

    public class JwtService : IJwtService
    {
        private readonly IConfiguration _config;

        public JwtService(IConfiguration config)
        {
            _config = config;
        }

        public string GenerateToken(User user)
        {
            var jwtSettings = _config.GetSection("JwtSettings");
            var secretKey   = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey не задан");
            var issuer      = jwtSettings["Issuer"]    ?? "GreenSwamp";
            var audience    = jwtSettings["Audience"]  ?? "GreenSwampApi";
            var expireHours = int.Parse(jwtSettings["ExpiresInHours"] ?? "24");

            // Claims — данные, закодированные внутри токена
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name,           user.Username),
                new Claim("display_name",            user.DisplayName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()) // уникальный id токена
            };

            // Подпись токена — без правильного ключа сервер отклонит подделанный токен
            var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer:             issuer,
                audience:           audience,
                claims:             claims,
                expires:            DateTime.UtcNow.AddHours(expireHours),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
