using CookMate.Api.Data;
using CookMate.Api.Dtos;
using CookMate.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CookMate.Api.Controllers;

// Thông báo trong app (bài được duyệt, bị từ chối, bị gỡ). App hỏi số chưa đọc khi mở tab.
[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationsController(AppDbContext db) : ControllerBase
{
    private const int MaxPageSize = 50;

    [HttpGet]
    [ProducesResponseType<PagedResult<NotificationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(int page = 1, int pageSize = 20)
    {
        if (page < 1) return BadRequest(new ErrorResponse("page phải từ 1 trở lên"));
        if (pageSize < 1) return BadRequest(new ErrorResponse("pageSize phải từ 1 trở lên"));
        pageSize = Math.Min(pageSize, MaxPageSize);

        var userId = User.GetUserId();
        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NotificationDto(n.Id, n.Type, n.RecipeId, n.Title, n.Body, n.IsRead, n.CreatedAt))
            .ToListAsync();

        return Ok(new PagedResult<NotificationDto>(items, page, pageSize, total));
    }

    [HttpGet("unread-count")]
    [ProducesResponseType<UnreadCountDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UnreadCount()
    {
        var userId = User.GetUserId();
        return Ok(new UnreadCountDto(await db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead)));
    }

    // Đánh dấu đã đọc toàn bộ (gọi khi người dùng mở màn Thông báo)
    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReadAll()
    {
        var userId = User.GetUserId();
        var unread = await db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync();
        foreach (var n in unread) n.IsRead = true;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
