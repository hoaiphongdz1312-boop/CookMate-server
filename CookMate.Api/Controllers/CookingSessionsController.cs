using CookMate.Api.Data;
using CookMate.Api.Dtos;
using CookMate.Api.Entities;
using CookMate.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CookMate.Api.Controllers;

// Phiên nấu và tiến độ: chỉ thao tác dữ liệu của người trong token, id người khác trả 404.
[ApiController]
[Authorize]
[Route("api")]
public class CookingSessionsController(AppDbContext db) : ControllerBase
{
    [HttpPost("cooking-sessions")]
    [ProducesResponseType<SessionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<SessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(CreateSessionRequest req)
    {
        var userId = User.GetUserId();
        // Chỉ được nấu công thức mình được phép xem (đã duyệt, hoặc bài của chính mình / admin xem)
        var isAdmin = User.IsInRole(Roles.Admin);
        if (!await db.Recipes.AnyAsync(r => r.Id == req.RecipeId && !r.IsDeleted
                && (r.Status == RecipeStatus.Approved || isAdmin || r.AuthorId == userId)))
            return NotFound(new ErrorResponse("Không tìm thấy công thức"));

        // Đã có phiên dở của cùng món thì trả lại phiên đó
        var existing = await db.CookingSessions
            .Where(s => s.UserId == userId && s.RecipeId == req.RecipeId && s.Status == CookingStatus.InProgress)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync();
        if (existing != null)
            return Ok(ToDto(existing));

        var now = DateTime.UtcNow;
        var session = new CookingSession
        {
            UserId = userId,
            RecipeId = req.RecipeId,
            Servings = req.Servings,
            CurrentStep = 1,
            Status = CookingStatus.InProgress,
            StartedAt = now,
            UpdatedAt = now
        };
        db.CookingSessions.Add(session);
        await db.SaveChangesAsync();
        return Created($"/api/cooking-sessions/{session.Id}", ToDto(session));
    }

    [HttpPatch("cooking-sessions/{id:int}")]
    [ProducesResponseType<SessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, UpdateSessionRequest req)
    {
        var userId = User.GetUserId();
        var session = await db.CookingSessions.SingleOrDefaultAsync(s => s.Id == id && s.UserId == userId);
        if (session == null)
            return NotFound(new ErrorResponse("Không tìm thấy phiên nấu"));
        if (session.Status == CookingStatus.Completed)
            return BadRequest(new ErrorResponse("Phiên nấu đã hoàn thành"));

        var totalSteps = await db.RecipeSteps.CountAsync(s => s.RecipeId == session.RecipeId);
        if (req.CurrentStep < 1 || req.CurrentStep > totalSteps)
            return BadRequest(new ErrorResponse($"Bước phải từ 1 đến {totalSteps}"));

        session.CurrentStep = req.CurrentStep;
        session.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToDto(session));
    }

    [HttpPost("cooking-sessions/{id:int}/complete")]
    [ProducesResponseType<SessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Complete(int id)
    {
        var userId = User.GetUserId();
        var session = await db.CookingSessions.SingleOrDefaultAsync(s => s.Id == id && s.UserId == userId);
        if (session == null)
            return NotFound(new ErrorResponse("Không tìm thấy phiên nấu"));

        // Gọi lại nhiều lần không đổi CompletedAt
        if (session.Status != CookingStatus.Completed)
        {
            var now = DateTime.UtcNow;
            session.Status = CookingStatus.Completed;
            session.CompletedAt = now;
            session.UpdatedAt = now;
            await db.SaveChangesAsync();
        }
        return Ok(ToDto(session));
    }

    [HttpGet("users/me/progress")]
    [ProducesResponseType<ProgressDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Progress()
    {
        var userId = User.GetUserId();

        var completedCount = await db.CookingSessions
            .CountAsync(s => s.UserId == userId && s.Status == CookingStatus.Completed);

        var inProgress = await db.CookingSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.Status == CookingStatus.InProgress)
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => new InProgressDto(
                s.Id, s.RecipeId, s.Recipe.Name, s.CurrentStep, s.Recipe.Steps.Count))
            .ToListAsync();

        var history = await db.CookingSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.Status == CookingStatus.Completed)
            .OrderByDescending(s => s.CompletedAt)
            .Take(50)
            .Select(s => new HistoryItemDto(s.Id, s.RecipeId, s.Recipe.Name, s.CompletedAt!.Value))
            .ToListAsync();

        return Ok(new ProgressDto(completedCount, inProgress, history));
    }

    private static SessionDto ToDto(CookingSession s) => new(s.Id, s.CurrentStep, s.Status.ToString());
}
