using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenSwamp.Data;
using GreenSwamp.Models;

namespace GreenSwamp.Controllers
{
    // Лаб 5: контроллер для страницы ленты новостей /feed
    [Route("feed")]
    public class FeedController : Controller
    {
        private readonly ApplicationDbContext _context;

        public FeedController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET /feed
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // Загружаем посты вместе со связанными данными (Include = JOIN в SQL)
            var posts = await _context.Posts
                .Include(p => p.User)                        // автор поста
                .Include(p => p.Interactions)                // лайки, реribbs и т.д.
                .Include(p => p.PostTags)
                    .ThenInclude(pt => pt.Tag)               // теги поста
                .Include(p => p.Event)                       // информация о событии (если есть)
                .Where(p => p.ParentPostId == null)          // только корневые посты, не ответы
                .OrderByDescending(p => p.CreatedAt)         // сначала самые новые
                .Take(50)                                    // не более 50 постов
                .ToListAsync();

            // Считаем количество каждого типа взаимодействий для каждого поста
            var postInteractions = new Dictionary<int, Dictionary<string, int>>();
            foreach (var post in posts)
            {
                postInteractions[post.PostId] = new Dictionary<string, int>
                {
                    ["likes"]    = post.Interactions.Count(i => i.InteractionType == "like"),
                    ["reribbs"]  = post.Interactions.Count(i => i.InteractionType == "reribb"),
                    ["comments"] = post.Interactions.Count(i => i.InteractionType == "comment"),
                    ["rsvps"]    = post.Interactions.Count(i => i.InteractionType == "rsvp")
                };
            }

            // Трендовые теги из SQL VIEW trending_ponds
            var trendingPonds = await _context.TrendingPonds
                .OrderByDescending(t => t.RecentPosts)
                .Take(5)
                .ToListAsync();

            // ViewBag — способ передать дополнительные данные во View
            ViewBag.PostInteractions = postInteractions;
            ViewBag.TrendingPonds = trendingPonds;

            return View(posts);
        }
    }
}
