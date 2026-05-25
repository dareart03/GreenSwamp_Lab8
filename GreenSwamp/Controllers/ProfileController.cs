using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenSwamp.Data;
using GreenSwamp.Models;

namespace GreenSwamp.Controllers
{
    // Лаб 5: контроллер для страницы профиля /profile/{username}
    [Route("profile")]
    public class ProfileController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ProfileController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET /profile/{username}
        [HttpGet("{username}")]
        public async Task<IActionResult> Index(string username)
        {
            // ВАЖНО: нельзя использовать .Where() внутри Include() вместе с вложенным
            // ThenInclude — EF Core игнорирует теги в таком случае.
            // Вместо этого загружаем все посты, а фильтрацию делаем после.
            var user = await _context.Users
                .Include(u => u.Posts)
                    .ThenInclude(p => p.Interactions)
                .Include(u => u.Posts)
                    .ThenInclude(p => p.PostTags)
                        .ThenInclude(pt => pt.Tag)
                .Include(u => u.Posts)
                    .ThenInclude(p => p.Event)
                .FirstOrDefaultAsync(u => u.Username == username);

            if (user == null)
                return NotFound();

            // Фильтруем ответы и сортируем уже в памяти — после того как EF подгрузил теги
            user.Posts = user.Posts
                .Where(p => p.ParentPostId == null)
                .OrderByDescending(p => p.CreatedAt)
                .ToList();

            // Подсчёт взаимодействий для каждого поста пользователя
            var postInteractions = new Dictionary<int, Dictionary<string, int>>();
            foreach (var post in user.Posts)
            {
                postInteractions[post.PostId] = new Dictionary<string, int>
                {
                    ["likes"]    = post.Interactions.Count(i => i.InteractionType == "like"),
                    ["reribbs"]  = post.Interactions.Count(i => i.InteractionType == "reribb"),
                    ["comments"] = post.Interactions.Count(i => i.InteractionType == "comment")
                };
            }

            ViewBag.PostInteractions = postInteractions;

            return View(user);
        }
    }
}
