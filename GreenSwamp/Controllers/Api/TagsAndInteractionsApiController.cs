using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using GreenSwamp.Api;
using GreenSwamp.Data;
using GreenSwamp.Models;

namespace GreenSwamp.Controllers.Api
{
    // ══════════════════════════════════════════════════════════════════════════
    // REST API для Tags (теги / ponds)
    // Маршруты:
    //   GET    /api/v1/tags            — все теги
    //   GET    /api/v1/tags/{name}     — один тег + его посты
    //   POST   /api/v1/tags            — создать тег (JWT)
    //   DELETE /api/v1/tags/{id}       — удалить тег (JWT)
    // ══════════════════════════════════════════════════════════════════════════
    [ApiController]
    [Route("api/v1/tags")]
    [Produces("application/json")]
    public class TagsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public TagsApiController(ApplicationDbContext db)
        {
            _db = db;
        }

        /// <summary>Получить все теги, отсортированные по популярности</summary>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<TagResponse>), 200)]
        public async Task<IActionResult> GetAll()
        {
            var tags = await _db.Tags
                .OrderByDescending(t => t.UsageCount)
                .ToListAsync();

            return Ok(tags.Select(t => new TagResponse(t.TagId, t.TagName, t.UsageCount, t.CreatedAt)));
        }

        /// <summary>Получить тег и список постов с ним</summary>
        [HttpGet("{name}")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> GetByName(string name)
        {
            var tag = await _db.Tags
                .Include(t => t.PostTags)
                    .ThenInclude(pt => pt.Post)
                        .ThenInclude(p => p!.User)
                .FirstOrDefaultAsync(t => t.TagName.ToLower() == name.ToLower());

            if (tag == null)
                return NotFound(new ErrorResponse($"Тег '{name}' не найден"));

            var posts = tag.PostTags
                .Where(pt => pt.Post != null)
                .Select(pt => new {
                    pt.Post!.PostId,
                    pt.Post.Content,
                    Author   = pt.Post.User?.Username,
                    pt.Post.CreatedAt
                })
                .OrderByDescending(p => p.CreatedAt);

            return Ok(new {
                Tag   = new TagResponse(tag.TagId, tag.TagName, tag.UsageCount, tag.CreatedAt),
                Posts = posts
            });
        }

        /// <summary>Создать новый тег</summary>
        /// <remarks>🔒 Требует JWT.</remarks>
        [HttpPost]
        [Authorize(AuthenticationSchemes = "JwtBearer")]
        [ProducesResponseType(typeof(TagResponse), 201)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> Create([FromBody] CreateTagRequest req)
        {
            var name = req.TagName.Trim().ToLower();

            if (await _db.Tags.AnyAsync(t => t.TagName == name))
                return Conflict(new ErrorResponse($"Тег '{name}' уже существует"));

            var tag = new Tag { TagName = name, CreatedAt = DateTime.UtcNow, UsageCount = 0 };
            _db.Tags.Add(tag);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetByName), new { name = tag.TagName },
                new TagResponse(tag.TagId, tag.TagName, tag.UsageCount, tag.CreatedAt));
        }

        /// <summary>Удалить тег</summary>
        /// <remarks>
        /// 🔒 Требует JWT.
        /// При удалении тега все записи post_tags для него удаляются каскадно (CASCADE DELETE).
        /// Сами посты остаются, только теряют этот тег.
        /// </remarks>
        [HttpDelete("{id:int}")]
        [Authorize(AuthenticationSchemes = "JwtBearer")]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Delete(int id)
        {
            var tag = await _db.Tags.FindAsync(id);
            if (tag == null)
                return NotFound(new ErrorResponse("Тег не найден"));

            // EF удалит связанные PostTag записи благодаря CASCADE в схеме
            _db.Tags.Remove(tag);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }


    // ══════════════════════════════════════════════════════════════════════════
    // REST API для Interactions (лайки, ретвиты, комментарии, rsvp)
    // Маршруты:
    //   GET    /api/v1/interactions?postId=&userId=   — получить с фильтром
    //   POST   /api/v1/interactions                   — создать (JWT)
    //   DELETE /api/v1/interactions/{id}              — удалить своё (JWT)
    // ══════════════════════════════════════════════════════════════════════════
    [ApiController]
    [Route("api/v1/interactions")]
    [Produces("application/json")]
    public class InteractionsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public InteractionsApiController(ApplicationDbContext db)
        {
            _db = db;
        }

        private static InteractionResponse ToResponse(Interaction i) => new(
            i.InteractionId,
            i.UserId,
            i.User?.Username ?? "",
            i.PostId,
            i.InteractionType,
            i.Content,
            i.CreatedAt
        );

        /// <summary>Получить взаимодействия с фильтрами</summary>
        /// <param name="postId">Фильтр по посту</param>
        /// <param name="userId">Фильтр по пользователю</param>
        /// <param name="type">Фильтр по типу: like / reribb / comment / rsvp</param>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<InteractionResponse>), 200)]
        public async Task<IActionResult> GetAll(
            [FromQuery] int?    postId = null,
            [FromQuery] int?    userId = null,
            [FromQuery] string? type   = null)
        {
            var query = _db.Interactions
                .Include(i => i.User)
                .AsQueryable();

            if (postId.HasValue)  query = query.Where(i => i.PostId == postId.Value);
            if (userId.HasValue)  query = query.Where(i => i.UserId == userId.Value);
            if (type is not null) query = query.Where(i => i.InteractionType == type);

            var result = await query
                .OrderByDescending(i => i.CreatedAt)
                .Take(100)
                .ToListAsync();

            return Ok(result.Select(ToResponse));
        }

        /// <summary>Добавить взаимодействие (лайк / reribb / comment / rsvp)</summary>
        /// <remarks>
        /// 🔒 Требует JWT.
        /// На один пост можно оставить только одно взаимодействие каждого типа.
        /// Поле content обязательно только для type = "comment".
        /// </remarks>
        [HttpPost]
        [Authorize(AuthenticationSchemes = "JwtBearer")]
        [ProducesResponseType(typeof(InteractionResponse), 201)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> Create([FromBody] CreateInteractionRequest req)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            // Валидация типа
            var validTypes = new[] { "like", "reribb", "comment", "rsvp" };
            if (!validTypes.Contains(req.InteractionType))
                return BadRequest(new ErrorResponse($"Неверный тип. Допустимые: {string.Join(", ", validTypes)}"));

            // Для комментария нужен текст
            if (req.InteractionType == "comment" && string.IsNullOrWhiteSpace(req.Content))
                return BadRequest(new ErrorResponse("Для комментария нужно передать content"));

            // Проверяем существование поста
            if (!await _db.Posts.AnyAsync(p => p.PostId == req.PostId))
                return BadRequest(new ErrorResponse("Пост не найден"));

            // Уникальность: один пользователь — один тип на один пост
            var exists = await _db.Interactions.AnyAsync(i =>
                i.UserId          == userId &&
                i.PostId          == req.PostId &&
                i.InteractionType == req.InteractionType);

            if (exists)
                return Conflict(new ErrorResponse(
                    $"Ты уже поставил '{req.InteractionType}' на этот пост"
                ));

            var interaction = new Interaction
            {
                UserId          = userId,
                PostId          = req.PostId,
                InteractionType = req.InteractionType,
                Content         = req.Content?.Trim(),
                CreatedAt       = DateTime.UtcNow
            };
            _db.Interactions.Add(interaction);
            await _db.SaveChangesAsync();

            // Подгружаем User для ответа
            await _db.Entry(interaction).Reference(i => i.User).LoadAsync();

            return CreatedAtAction(nameof(GetAll), new { postId = req.PostId }, ToResponse(interaction));
        }

        /// <summary>Удалить своё взаимодействие (убрать лайк, etc.)</summary>
        /// <remarks>
        /// 🔒 Требует JWT. Можно удалить только своё взаимодействие.
        /// При удалении взаимодействие удаляется физически (не мягко).
        /// </remarks>
        [HttpDelete("{id:int}")]
        [Authorize(AuthenticationSchemes = "JwtBearer")]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var interaction = await _db.Interactions.FindAsync(id);
            if (interaction == null)
                return NotFound(new ErrorResponse("Взаимодействие не найдено"));

            if (interaction.UserId != userId)
                return Forbid();

            _db.Interactions.Remove(interaction);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
