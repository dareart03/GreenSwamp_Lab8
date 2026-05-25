using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenSwamp.Data;
using GreenSwamp.Models;

namespace GreenSwamp.Controllers
{
    // Лаб 5: контроллер для страниц с тегами /ponds и /ponds/{tag}
    [Route("ponds")]
    public class PondsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PondsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET /ponds — список всех тегов
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // Все теги, отсортированные по популярности
            var allTags = await _context.Tags
                .OrderByDescending(t => t.UsageCount)
                .ToListAsync();

            // Количество постов для каждого тега
            var tagPostCounts = await _context.PostTags
                .GroupBy(pt => pt.TagId)
                .Select(g => new { TagId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TagId, x => x.Count);

            var trendingPonds = await _context.TrendingPonds
                .OrderByDescending(t => t.RecentPosts)
                .Take(5)
                .ToListAsync();

            ViewBag.AllTags = allTags;
            ViewBag.TagPostCounts = tagPostCounts;
            ViewBag.TrendingPonds = trendingPonds;

            return View();
        }

        // GET /ponds/{tagName} — посты с конкретным тегом
        [HttpGet("{tagName}")]
        public async Task<IActionResult> TagPosts(string tagName)
        {
            var tag = await _context.Tags
                .FirstOrDefaultAsync(t => t.TagName.ToLower() == tagName.ToLower());

            if (tag == null)
                return NotFound();

            // Посты с этим тегом, сортируем по дате
            var posts = await _context.Posts
                .Include(p => p.User)
                .Include(p => p.Interactions)
                .Include(p => p.PostTags)
                    .ThenInclude(pt => pt.Tag)
                .Include(p => p.Event)
                .Where(p => p.PostTags.Any(pt => pt.TagId == tag.TagId))
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

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

            var trendingPonds = await _context.TrendingPonds
                .OrderByDescending(t => t.RecentPosts)
                .Take(5)
                .ToListAsync();

            ViewBag.TagName = tag.TagName;
            ViewBag.Tag = tag;
            ViewBag.PostInteractions = postInteractions;
            ViewBag.TrendingPonds = trendingPonds;

            return View(posts);
        }
    }
}
