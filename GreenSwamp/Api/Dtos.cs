namespace GreenSwamp.Api
{
    // ── Запросы (Request DTOs) ────────────────────────────────────────────────
    // DTO — Data Transfer Object: объекты, которые приходят в теле запроса.
    // Мы не принимаем Model напрямую, чтобы клиент не мог изменить поля,
    // которые он не должен трогать (например UserId, CreatedAt).

    // POST /api/v1/users  — создать пользователя
    public record CreateUserRequest(
        string Username,
        string DisplayName,
        string? Bio,
        string? AvatarUrl,
        string Password    // пароль для таблицы auth
    );

    // PUT /api/v1/users/{id}
    public record UpdateUserRequest(
        string? DisplayName,
        string? Bio,
        string? AvatarUrl
    );

    // POST /api/v1/posts
    public record CreatePostRequest(
        string Content,
        string PostType,        // "text" | "image" | "video"
        string? MediaUrl,
        string? AltText,
        int? ParentPostId,
        List<string>? Tags      // список названий тегов (создаются автоматически)
    );

    // PUT /api/v1/posts/{id}
    public record UpdatePostRequest(
        string? Content,
        string? AltText
    );

    // POST /api/v1/tags
    public record CreateTagRequest(string TagName);

    // POST /api/v1/interactions
    public record CreateInteractionRequest(
        int PostId,
        string InteractionType,  // "like" | "reribb" | "comment" | "rsvp"
        string? Content          // текст комментария (только для type=comment)
    );

    // POST /api/v1/auth/login — получить JWT-токен
    public record LoginRequest(string Username, string Password);

    // ── Ответы (Response DTOs) ────────────────────────────────────────────────
    // Возвращаем только нужные поля, скрываем хэши паролей и внутренние ключи.

    public record UserResponse(
        int UserId,
        string Username,
        string DisplayName,
        string? Bio,
        string? AvatarUrl,
        DateTime CreatedAt,
        bool IsActive,
        int PostCount,
        int InteractionCount
    );

    public record PostResponse(
        int PostId,
        int UserId,
        string AuthorUsername,
        string AuthorDisplayName,
        string? AuthorAvatarUrl,
        string Content,
        string PostType,
        string? MediaUrl,
        string? AltText,
        DateTime CreatedAt,
        int? ParentPostId,
        int LikeCount,
        int ReribbCount,
        int CommentCount,
        List<string> Tags
    );

    public record TagResponse(
        int TagId,
        string TagName,
        int UsageCount,
        DateTime CreatedAt
    );

    public record InteractionResponse(
        int InteractionId,
        int UserId,
        string Username,
        int PostId,
        string InteractionType,
        string? Content,
        DateTime CreatedAt
    );

    public record AuthResponse(
        string Token,
        string Username,
        string DisplayName,
        DateTime ExpiresAt
    );

    // Универсальный ответ на ошибку
    public record ErrorResponse(string Error, string? Detail = null);
}
