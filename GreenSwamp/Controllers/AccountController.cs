using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenSwamp.Data;
using GreenSwamp.Models;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace GreenSwamp.Controllers
{
    // Лаб 6: контроллер аутентификации (логин, регистрация, выход)
    [Route("account")]
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccountController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET /account/login — перенаправляет на страницу логина
        [HttpGet("login")]
        public IActionResult Login()
        {
            // Если уже авторизован — идём в ленту
            if (User.Identity?.IsAuthenticated == true)
                return Redirect("/feed");

            return RedirectToPage("/login");
        }

        // POST /account/login — обрабатывает форму логина
        [HttpPost("login")]
        public async Task<IActionResult> Login(string username, string password)
        {
            // Ищем пользователя с данным username вместе с его данными авторизации
            var user = await _context.Users
                .Include(u => u.Auth)
                .FirstOrDefaultAsync(u => u.Username == username);

            // Проверяем: найден ли пользователь и совпадает ли пароль
            if (user?.Auth == null || !VerifyPassword(password, user.Auth.PasswordHash) || !user.IsActive)
            {
                TempData["Error"] = "Неверный логин или пароль";
                return RedirectToPage("/login");
            }

            // Обновляем время последнего входа
            user.Auth.LastLogin = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Создаём cookie-сессию для пользователя
            await SignInUser(user);
            return Redirect("/feed");
        }

        // GET /account/register — перенаправляет на страницу регистрации
        [HttpGet("register")]
        public IActionResult Register()
        {
            if (User.Identity?.IsAuthenticated == true)
                return Redirect("/feed");

            return RedirectToPage("/register");
        }

        // POST /account/register — обрабатывает форму регистрации
        [HttpPost("register")]
        public async Task<IActionResult> Register(string username, string displayName, string password, string confirmPassword)
        {
            // Проверяем что все поля заполнены
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(displayName) ||
                string.IsNullOrWhiteSpace(password))
            {
                TempData["Error"] = "Все поля обязательны для заполнения";
                return RedirectToPage("/register");
            }

            if (password != confirmPassword)
            {
                TempData["Error"] = "Пароли не совпадают";
                return RedirectToPage("/register");
            }

            if (password.Length < 6)
            {
                TempData["Error"] = "Пароль должен быть минимум 6 символов";
                return RedirectToPage("/register");
            }

            // Проверяем что логин не занят
            if (await _context.Users.AnyAsync(u => u.Username == username))
            {
                TempData["Error"] = "Пользователь с таким именем уже существует";
                return RedirectToPage("/register");
            }

            // Создаём нового пользователя + запись Auth с хэшем пароля
            var user = new User
            {
                Username = username,
                DisplayName = displayName,
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                Auth = new Auth
                {
                    PasswordHash = HashPassword(password),
                    LastLogin = DateTime.UtcNow
                }
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Автоматически входим после регистрации
            await SignInUser(user);
            return Redirect("/feed");
        }

        // GET /account/logout — выход из аккаунта
        [HttpGet("logout")]
        public async Task<IActionResult> Logout(string? returnUrl = null)
        {
            // Удаляем cookie аутентификации
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // Возвращаем на ту страницу, откуда вышел (если передан returnUrl)
            if (!string.IsNullOrEmpty(returnUrl))
            {
                var decodedUrl = Uri.UnescapeDataString(returnUrl);
                if (decodedUrl.StartsWith("/"))
                    return Redirect(decodedUrl);
            }

            return Redirect("/feed");
        }

        // ── Вспомогательные методы ───────────────────────────────────────────────

        // Создаём claims (данные о пользователе) и записываем cookie-сессию
        private async Task SignInUser(User user)
        {
            // Если аватарка не задана — генерируем через DiceBear и сразу сохраняем в БД,
            // чтобы она отображалась везде (в лентах, профилях других пользователей и т.д.)
            if (string.IsNullOrEmpty(user.AvatarUrl))
            {
                user.AvatarUrl = $"https://api.dicebear.com/9.x/adventurer/svg?seed={Uri.EscapeDataString(user.Username)}&backgroundColor=b6e3f4,c0aede,d1d4f9,ffd5dc,ffdfbf";
                await _context.SaveChangesAsync();
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim("DisplayName", user.DisplayName),
                new Claim("AvatarUrl", user.AvatarUrl)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = true,                          // сохранять между закрытием браузера
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
                }
            );
        }

        // Хэшируем пароль с солью через SHA-256
        private static string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + "greenswamp_salt"));
            return Convert.ToBase64String(bytes);
        }

        // Проверяем пароль: хэшируем введённый и сравниваем с хранимым
        private static bool VerifyPassword(string password, string hash)
        {
            return HashPassword(password) == hash;
        }
    }
}