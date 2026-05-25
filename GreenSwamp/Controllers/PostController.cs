using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenSwamp.Data;
using GreenSwamp.Models;

namespace GreenSwamp.Controllers
{
    // Лаб 5: контроллер для страницы одного поста /feed/post/{id}
    [Route("feed/post")]
    public class PostController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PostController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET /feed/post/{postId}
        [HttpGet("{postId:int}")]
        public async Task<IActionResult> Index(int postId)
        {
            // Загружаем пост со всеми связанными данными
            var post = await _context.Posts
                .Include(p => p.User)
                .Include(p => p.Interactions)
                    .ThenInclude(i => i.User)        // автор каждого взаимодействия
                .Include(p => p.PostTags)
                    .ThenInclude(pt => pt.Tag)
                .Include(p => p.Event)
                .Include(p => p.Replies)
                    .ThenInclude(r => r.User)        // авторы ответов
                .FirstOrDefaultAsync(p => p.PostId == postId);

            // Если пост не найден — возвращаем 404
            if (post == null)
                return NotFound();

            // Подсчёт взаимодействий для этого поста
            var counts = new Dictionary<string, int>
            {
                ["likes"]    = post.Interactions.Count(i => i.InteractionType == "like"),
                ["reribbs"]  = post.Interactions.Count(i => i.InteractionType == "reribb"),
                ["comments"] = post.Interactions.Count(i => i.InteractionType == "comment"),
                ["rsvps"]    = post.Interactions.Count(i => i.InteractionType == "rsvp")
            };

            ViewBag.InteractionCounts = counts;

            return View(post);
        }
    }
}
