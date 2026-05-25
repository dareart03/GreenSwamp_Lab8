using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using GreenSwamp.Api;
using GreenSwamp.Data;
using GreenSwamp.Models;

namespace GreenSwamp.Controllers.Api
{
    // REST API для сущности Post (посты/ribbits).
    // Маршруты:
    //   GET    /api/v1/posts           — лента постов (с пагинацией)
    //   GET    /api/v1/posts/{id}      — один пост
    //   POST   /api/v1/posts           — создать пост (JWT)
    //   PUT    /api/v1/posts/{id}      — изменить пост (только свой, JWT)
    //   DELETE /api/v1/posts/{id}      — удалить пост (только свой, JWT)
    [ApiController]
    [Route("api/v1/posts")]
    [Produces("application/json")]
    public class PostsRestApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public PostsRestApiController(ApplicationDbContext db)
        {
            _db = db;
        }

        // Маппинг Post → PostResponse DTO
        private static PostResponse ToResponse(Post p) => new(
            p.PostId,
            p.UserId,
            p.User?.Username    ?? "",
            p.User?.DisplayName ?? "",
            p.User?.AvatarUrl,
            p.Content,
            p.PostType,
            p.MediaUrl,
            p.AltText,
            p.CreatedAt,
            p.ParentPostId,
            p.Interactions?.Count(i => i.InteractionType == "like")   ?? 0,
            p.Interactions?.Count(i => i.InteractionType == "reribb") ?? 0,
            p.Interactions?.Count(i => i.InteractionType == "comment")  ?? 0,
            p.PostTags?.Select(pt => pt.Tag?.TagName ?? "").Where(t => t != "").ToList() ?? []
        );

        /// <summary>Получить ленту постов</summary>
        /// <param name="tag">Фильтр по тегу (необязательно)</param>
        /// <param name="userId">Фильтр по автору (необязательно)</param>
        /// <param name="page">Страница (с 1)</param>
        /// <param name="pageSize">Записей на странице (макс. 100)</param>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<PostResponse>), 200)]
        public async Task<IActionResult> GetAll(
            [FromQuery] string? tag      = null,
            [FromQuery] int?    userId   = null,
            [FromQuery] int     page     = 1,
            [FromQuery] int     pageSize = 20)
        {
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.Posts
                .Include(p => p.User)
                .Include(p => p.Interactions)
                .Include(p => p.PostTags).ThenInclude(pt => pt.Tag)
                .Where(p => p.ParentPostId == null)   // только корневые посты
                .AsQueryable();

            // Фильтр по тегу
            if (!string.IsNullOrWhiteSpace(tag))
                query = query.Where(p => p.PostTags.Any(pt => pt.Tag.TagName.ToLower() == tag.ToLower()));

            // Фильтр по автору
            if (userId.HasValue)
                query = query.Where(p => p.UserId == userId.Value);

            var posts = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(posts.Select(ToResponse));
        }

        /// <summary>Получить пост по ID (включая ответы)</summary>
        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(PostResponse), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> GetById(int id)
        {
            var post = await _db.Posts
                .Include(p => p.User)
                .Include(p => p.Interactions).ThenInclude(i => i.User)
                .Include(p => p.PostTags).ThenInclude(pt => pt.Tag)
                .Include(p => p.Replies).ThenInclude(r => r.User)
                .FirstOrDefaultAsync(p => p.PostId == id);

            return post == null
                ? NotFound(new ErrorResponse("Пост не найден"))
                : Ok(ToResponse(post));
        }

        /// <summary>Создать новый пост</summary>
        /// <remarks>
        /// 🔒 Требует JWT.
        /// Теги из поля Tags создаются автоматически, если ещё не существуют.
        /// PostType: "text" — без медиа, "image"/"video" — обязательно передать MediaUrl.
        /// </remarks>
        [HttpPost]
        [Authorize(AuthenticationSchemes = "JwtBearer")]
        [ProducesResponseType(typeof(PostResponse), 201)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        public async Task<IActionResult> Create([FromBody] CreatePostRequest req)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            // Валидация: если тип image/video — нужен MediaUrl
            if (req.PostType is "image" or "video" && string.IsNullOrWhiteSpace(req.MediaUrl))
                return BadRequest(new ErrorResponse("Для типа image/video нужно передать MediaUrl"));

            // Валидация: text-пост не должен содержать MediaUrl
            if (req.PostType == "text" && !string.IsNullOrWhiteSpace(req.MediaUrl))
                return BadRequest(new ErrorResponse("Text-пост не может содержать MediaUrl"));

            var post = new Post
            {
                UserId       = userId,
                Content      = req.Content.Trim(),
                PostType     = req.PostType,
                MediaUrl     = req.MediaUrl,
                AltText      = req.AltText,
                ParentPostId = req.ParentPostId,
                CreatedAt    = DateTime.UtcNow
            };
            _db.Posts.Add(post);
            await _db.SaveChangesAsync();

            // Обрабатываем теги: ищем существующий или создаём новый
            if (req.Tags?.Any() == true)
            {
                foreach (var tagName in req.Tags.Select(t => t.Trim().ToLower()).Distinct())
                {
                    var tag = await _db.Tags.FirstOrDefaultAsync(t => t.TagName == tagName)
                           ?? new Tag { TagName = tagName, CreatedAt = DateTime.UtcNow };

                    if (tag.TagId == 0) _db.Tags.Add(tag);

                    tag.UsageCount++;
                    await _db.SaveChangesAsync();

                    _db.PostTags.Add(new PostTag { PostId = post.PostId, TagId = tag.TagId });
                }
                await _db.SaveChangesAsync();
            }

            // Перезагружаем пост с навигационными свойствами для ответа
            var created = await _db.Posts
                .Include(p => p.User)
                .Include(p => p.Interactions)
                .Include(p => p.PostTags).ThenInclude(pt => pt.Tag)
                .FirstAsync(p => p.PostId == post.PostId);

            return CreatedAtAction(nameof(GetById), new { id = post.PostId }, ToResponse(created));
        }

        /// <summary>Изменить содержимое поста</summary>
        /// <remarks>🔒 Требует JWT. Можно изменить только свой пост. Медиа и теги не меняются.</remarks>
        [HttpPut("{id:int}")]
        [Authorize(AuthenticationSchemes = "JwtBearer")]
        [ProducesResponseType(typeof(PostResponse), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Update(int id, [FromBody] UpdatePostRequest req)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var post = await _db.Posts
                .Include(p => p.User)
                .Include(p => p.Interactions)
                .Include(p => p.PostTags).ThenInclude(pt => pt.Tag)
                .FirstOrDefaultAsync(p => p.PostId == id);

            if (post == null)
                return NotFound(new ErrorResponse("Пост не найден"));

            if (post.UserId != userId)
                return Forbid();

            if (req.Content is not null) post.Content = req.Content.Trim();
            if (req.AltText is not null) post.AltText = req.AltText.Trim();

            await _db.SaveChangesAsync();
            return Ok(ToResponse(post));
        }

        /// <summary>Удалить пост</summary>
        /// <remarks>
        /// 🔒 Требует JWT. Можно удалить только свой пост.
        /// Каскадное удаление: вместе с постом удаляются его interactions и post_tags.
        /// Ответы (replies) получают ParentPostId = NULL (поведение Restrict → сначала удали ответы).
        /// </remarks>
        [HttpDelete("{id:int}")]
        [Authorize(AuthenticationSchemes = "JwtBearer")]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var post = await _db.Posts
                .Include(p => p.Replies)
                .FirstOrDefaultAsync(p => p.PostId == id);

            if (post == null)
                return NotFound(new ErrorResponse("Пост не найден"));

            if (post.UserId != userId)
                return Forbid();

            // Предупреждаем если есть ответы — они станут "висячими"
            // (ParentPostId указывает на несуществующий пост).
            // В схеме стоит Restrict, поэтому нужно сначала удалить ответы.
            if (post.Replies.Any())
                return Conflict(new ErrorResponse(
                    "У поста есть ответы. Сначала удали их или используй DELETE /api/v1/posts/{id}/force",
                    $"Ответов: {post.Replies.Count}"
                ));

            _db.Posts.Remove(post);
            await _db.SaveChangesAsync();

            return NoContent();
        }

        /// <summary>Принудительно удалить пост вместе со всеми ответами</summary>
        /// <remarks>🔒 Требует JWT. Каскадно удаляет все дочерние посты.</remarks>
        [HttpDelete("{id:int}/force")]
        [Authorize(AuthenticationSchemes = "JwtBearer")]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> ForceDelete(int id)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var post = await _db.Posts
                .Include(p => p.Replies)
                .FirstOrDefaultAsync(p => p.PostId == id);

            if (post == null)
                return NotFound(new ErrorResponse("Пост не найден"));

            if (post.UserId != userId)
                return Forbid();

            // Удаляем все ответы рекурсивно, потом сам пост
            _db.Posts.RemoveRange(post.Replies);
            _db.Posts.Remove(post);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
