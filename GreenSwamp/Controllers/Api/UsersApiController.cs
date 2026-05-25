using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GreenSwamp.Api;
using GreenSwamp.Data;
using GreenSwamp.Models;

namespace GreenSwamp.Controllers.Api
{
    // REST API для сущности User (пользователи).
    // Маршруты:
    //   GET    /api/v1/users           — список всех пользователей
    //   GET    /api/v1/users/{id}      — один пользователь
    //   POST   /api/v1/users           — создать пользователя (открыто)
    //   PUT    /api/v1/users/{id}      — изменить профиль (только свой, JWT)
    //   DELETE /api/v1/users/{id}      — удалить (только свой, JWT)
    [ApiController]
    [Route("api/v1/users")]
    [Produces("application/json")]
    public class UsersApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public UsersApiController(ApplicationDbContext db)
        {
            _db = db;
        }

        // Вспомогательный метод — превращает User в UserResponse DTO
        private static UserResponse ToResponse(User u) => new(
            u.UserId,
            u.Username,
            u.DisplayName,
            u.Bio,
            u.AvatarUrl,
            u.CreatedAt,
            u.IsActive,
            u.Posts?.Count ?? 0,
            u.Interactions?.Count ?? 0
        );

        /// <summary>Получить список всех пользователей</summary>
        /// <param name="page">Номер страницы (с 1)</param>
        /// <param name="pageSize">Записей на странице (макс. 100)</param>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<UserResponse>), 200)]
        public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            pageSize = Math.Clamp(pageSize, 1, 100);

            var users = await _db.Users
                .Include(u => u.Posts)
                .Include(u => u.Interactions)
                .Where(u => u.IsActive)
                .OrderByDescending(u => u.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(users.Select(ToResponse));
        }

        /// <summary>Получить пользователя по ID</summary>
        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(UserResponse), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> GetById(int id)
        {
            var user = await _db.Users
                .Include(u => u.Posts)
                .Include(u => u.Interactions)
                .FirstOrDefaultAsync(u => u.UserId == id);

            return user == null
                ? NotFound(new ErrorResponse("Пользователь не найден"))
                : Ok(ToResponse(user));
        }

        /// <summary>Создать нового пользователя</summary>
        /// <remarks>
        /// Пароль хэшируется SHA-256 и сохраняется в таблице auth.
        /// Этот же логин/пароль используется для получения JWT-токена.
        /// </remarks>
        [HttpPost]
        [ProducesResponseType(typeof(UserResponse), 201)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
        {
            // Проверяем уникальность username
            if (await _db.Users.AnyAsync(u => u.Username == req.Username))
                return Conflict(new ErrorResponse($"Пользователь '{req.Username}' уже существует"));

            var user = new User
            {
                Username    = req.Username.Trim().ToLower(),
                DisplayName = req.DisplayName.Trim(),
                Bio         = req.Bio?.Trim(),
                AvatarUrl   = req.AvatarUrl ?? "/images/default-avatar.svg",
                CreatedAt   = DateTime.UtcNow,
                IsActive    = true
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            // Сохраняем хэш пароля — тот же алгоритм что в AccountController:
            // SHA256(password + "greenswamp_salt") → Base64
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes    = sha256.ComputeHash(Encoding.UTF8.GetBytes(req.Password + "greenswamp_salt"));
            var passwordHash = Convert.ToBase64String(hashBytes);

            _db.Auths.Add(new Auth
            {
                UserId       = user.UserId,
                PasswordHash = passwordHash
            });
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = user.UserId }, ToResponse(user));
        }

        /// <summary>Изменить профиль пользователя</summary>
        /// <remarks>🔒 Требует JWT. Можно изменить только свой профиль.</remarks>
        [HttpPut("{id:int}")]
        [Authorize(AuthenticationSchemes = "JwtBearer")]
        [ProducesResponseType(typeof(UserResponse), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest req)
        {
            // Проверяем, что это свой профиль
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (currentUserId != id)
                return Forbid();

            var user = await _db.Users.FindAsync(id);
            if (user == null)
                return NotFound(new ErrorResponse("Пользователь не найден"));

            // Обновляем только переданные поля (null = не менять)
            if (req.DisplayName is not null) user.DisplayName = req.DisplayName.Trim();
            if (req.Bio         is not null) user.Bio         = req.Bio.Trim();
            if (req.AvatarUrl   is not null) user.AvatarUrl   = req.AvatarUrl;

            await _db.SaveChangesAsync();
            return Ok(ToResponse(user));
        }

        /// <summary>Удалить пользователя</summary>
        /// <remarks>
        /// 🔒 Требует JWT. Можно удалить только свой аккаунт.
        /// Все посты и взаимодействия удаляются каскадно (CASCADE DELETE).
        /// </remarks>
        [HttpDelete("{id:int}")]
        [Authorize(AuthenticationSchemes = "JwtBearer")]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Delete(int id)
        {
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (currentUserId != id)
                return Forbid();

            var user = await _db.Users.FindAsync(id);
            if (user == null)
                return NotFound(new ErrorResponse("Пользователь не найден"));

            // Помечаем как неактивного вместо физического удаления —
            // чтобы сохранить исторические данные (посты и взаимодействия остаются)
            user.IsActive = false;
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
