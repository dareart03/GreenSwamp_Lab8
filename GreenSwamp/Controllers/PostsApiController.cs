using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenSwamp.Data;
using GreenSwamp.Models;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace GreenSwamp.Controllers
{
    // Лаб 6: API-контроллер для создания постов и взаимодействий без перезагрузки страницы
    // [Authorize] — все методы доступны только авторизованным пользователям
    [Route("api/posts")]
    [ApiController]
    [Authorize]
    public class PostsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;

        public PostsApiController(ApplicationDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // POST /api/posts — создать новый пост (вызывается через fetch из JS)
        // RequestSizeLimit — ограничение тела запроса в байтах.
        // RequestFormLimits — ограничение multipart-формы. Нужно указывать ОБА атрибута!
        // Без RequestFormLimits ASP.NET применяет дефолтный лимит 128 KB на форму —
        // загрузка фото молча обрывается ещё до попадания в контроллер.
        [HttpPost]
        [RequestSizeLimit(50_000_000)]
        [RequestFormLimits(MultipartBodyLengthLimit = 50_000_000)]
        public async Task<IActionResult> CreatePost([FromForm] string content, IFormFile? media)
        {
            if (string.IsNullOrWhiteSpace(content) && media == null)
                return BadRequest(new { error = "Пост не может быть пустым" });

            // Получаем id текущего пользователя из claims (cookie-сессии)
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var post = new Post
            {
                UserId = userId,
                Content = content?.Trim() ?? "",
                PostType = "text",
                CreatedAt = DateTime.UtcNow
            };

            // Если прикреплён файл — сохраняем его в wwwroot/uploads
            if (media != null && media.Length > 0)
            {
                var uploadsFolder = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                // Уникальное имя файла чтобы не было конфликтов
                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(media.FileName)}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await media.CopyToAsync(stream);
                }

                // Определяем тип медиа по Content-Type
                if (media.ContentType.StartsWith("image/"))
                {
                    post.PostType = "image";
                    post.MediaType = "image";
                    post.MediaUrl = $"/uploads/{fileName}";
                }
                else if (media.ContentType.StartsWith("video/"))
                {
                    post.PostType = "video";
                    post.MediaType = "video";
                    post.MediaUrl = $"/uploads/{fileName}";
                }
                post.AltText = $"Uploaded {post.MediaType}";
            }

            _context.Posts.Add(post);
            await _context.SaveChangesAsync();

            // Извлекаем хэштеги из текста и сохраняем их в БД
            if (!string.IsNullOrWhiteSpace(content))
            {
                var hashtags = ExtractHashtags(content);
                foreach (var tagName in hashtags)
                {
                    // Ищем тег или создаём новый
                    var tag = await _context.Tags
                        .FirstOrDefaultAsync(t => t.TagName.ToLower() == tagName.ToLower());

                    if (tag == null)
                    {
                        tag = new Tag { TagName = tagName.ToLower(), CreatedAt = DateTime.UtcNow };
                        _context.Tags.Add(tag);
                        await _context.SaveChangesAsync();
                    }

                    tag.UsageCount++;

                    // Связываем пост с тегом (если связь ещё не существует)
                    var existingLink = await _context.PostTags
                        .FirstOrDefaultAsync(pt => pt.PostId == post.PostId && pt.TagId == tag.TagId);

                    if (existingLink == null)
                        _context.PostTags.Add(new PostTag { PostId = post.PostId, TagId = tag.TagId });
                }
                await _context.SaveChangesAsync();
            }

            // Возвращаем созданный пост в JSON для обновления страницы через JS
            var fullPost = await _context.Posts
                .Include(p => p.User)
                .Include(p => p.PostTags).ThenInclude(pt => pt.Tag)
                .FirstAsync(p => p.PostId == post.PostId);

            return Ok(new
            {
                postId = fullPost.PostId,
                content = fullPost.Content,
                contentWithLinks = ContentWithLinks(fullPost.Content),
                postType = fullPost.PostType,
                mediaUrl = fullPost.MediaUrl,
                mediaType = fullPost.MediaType,
                createdAt = fullPost.CreatedAt.ToString("MMM d"),
                user = new
                {
                    username = fullPost.User.Username,
                    displayName = fullPost.User.DisplayName,
                    avatarUrl = fullPost.User.AvatarUrl ?? ""
                },
                tags = fullPost.PostTags.Select(pt => pt.Tag.TagName).ToList()
            });
        }

        // POST /api/posts/{postId}/interactions — поставить или убрать лайк/рериbb и т.д.
        [HttpPost("{postId:int}/interactions")]
        public async Task<IActionResult> AddInteraction(int postId, [FromBody] InteractionDto dto)
        {
            var allowed = new[] { "like", "reribb", "comment", "rsvp" };
            if (!allowed.Contains(dto.Type))
                return BadRequest(new { error = "Неверный тип взаимодействия" });

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var post = await _context.Posts.FindAsync(postId);
            if (post == null) return NotFound();

            // Если взаимодействие уже есть — удаляем (toggle)
            var existing = await _context.Interactions
                .FirstOrDefaultAsync(i => i.UserId == userId && i.PostId == postId && i.InteractionType == dto.Type);

            if (existing != null)
            {
                _context.Interactions.Remove(existing);
                await _context.SaveChangesAsync();
                var newCount = await _context.Interactions.CountAsync(i => i.PostId == postId && i.InteractionType == dto.Type);
                return Ok(new { action = "removed", count = newCount });
            }

            // Иначе добавляем новое взаимодействие
            _context.Interactions.Add(new Interaction
            {
                UserId = userId,
                PostId = postId,
                InteractionType = dto.Type,
                Content = dto.Content,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            var count = await _context.Interactions.CountAsync(i => i.PostId == postId && i.InteractionType == dto.Type);
            return Ok(new { action = "added", count });
        }

        // ── Вспомогательные методы ───────────────────────────────────────────────

        // Находим все #хэштеги в тексте через регулярное выражение
        private static List<string> ExtractHashtags(string content)
        {
            return Regex.Matches(content, @"#(\w+)")
                        .Cast<System.Text.RegularExpressions.Match>()
                        .Select(m => m.Groups[1].Value.ToLower())
                        .Distinct()
                        .ToList();
        }

        // Заменяем #хэштеги в тексте на кликабельные ссылки
        private static string ContentWithLinks(string content)
        {
            return Regex.Replace(content, @"#(\w+)",
                "<a href='/ponds/$1' class='text-swamp-600 hover:underline'>#$1</a>");
        }
    }

    // DTO (Data Transfer Object) — структура, которую принимаем из JSON при запросе на взаимодействие
    public class InteractionDto
    {
        public string Type { get; set; } = string.Empty;
        public string? Content { get; set; }
    }
}
