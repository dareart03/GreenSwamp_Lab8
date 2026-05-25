using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenSwamp.Api;
using GreenSwamp.Data;
using System.Security.Cryptography;
using System.Text;

namespace GreenSwamp.Controllers.Api
{
    // POST /api/v1/auth/login — выдаёт JWT-токен по логину и паролю.
    // Токен затем нужно передавать в заголовке запросов, требующих авторизации:
    //   Authorization: Bearer <token>
    [ApiController]
    [Route("api/v1/auth")]
    [Produces("application/json")]
    public class AuthApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IJwtService _jwt;

        public AuthApiController(ApplicationDbContext db, IJwtService jwt)
        {
            _db  = db;
            _jwt = jwt;
        }

        /// <summary>Получить JWT-токен по логину и паролю</summary>
        /// <remarks>
        /// Пример запроса:
        ///
        ///     POST /api/v1/auth/login
        ///     { "username": "lily_pad", "password": "password123" }
        ///
        /// Полученный token вставляй в Swagger через кнопку Authorize (Bearer схема).
        /// </remarks>
        [HttpPost("login")]
        [ProducesResponseType(typeof(AuthResponse), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            // Ищем пользователя и его запись аутентификации
            var user = await _db.Users
                .Include(u => u.Auth)
                .FirstOrDefaultAsync(u => u.Username == req.Username && u.IsActive);

            if (user?.Auth == null)
                return Unauthorized(new ErrorResponse("Неверный логин или пароль"));

            // Хэшируем пароль тем же способом что и AccountController:
            // SHA256(password + "greenswamp_salt") → Base64
            // ВАЖНО: алгоритм должен совпадать везде, иначе пользователи
            // зарегистрированные через сайт не смогут войти через API и наоборот.
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(req.Password + "greenswamp_salt"));
            var hash = Convert.ToBase64String(hashBytes);

            if (user.Auth.PasswordHash != hash)
                return Unauthorized(new ErrorResponse("Неверный логин или пароль"));

            // Обновляем время последнего входа
            user.Auth.LastLogin = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var token     = _jwt.GenerateToken(user);
            var expiresAt = DateTime.UtcNow.AddHours(24);

            return Ok(new AuthResponse(token, user.Username, user.DisplayName, expiresAt));
        }
    }
}
