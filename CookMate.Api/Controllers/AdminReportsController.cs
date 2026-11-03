using CookMate.Api.Data;
using CookMate.Api.Dtos;
using CookMate.Api.Entities;
using CookMate.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CookMate.Api.Controllers;

// Xử lý báo cáo vi phạm. Báo cáo gộp theo bài: gỡ bài hoặc bỏ qua đều đóng mọi báo cáo đang mở của bài đó.
[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/reports")]
public class AdminReportsController(AppDbContext db) : ControllerBase
{
    private const int MaxPageSize = 50;
    // Số báo cáo chi tiết trả kèm mỗi bài trong danh sách
    private const int ReportsPerRecipe = 20;

    [HttpGet]
    [ProducesResponseType<PagedResult<ReportedRecipeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(int page = 1, int pageSize = 20)
    {
        if (page < 1) return BadRequest(new ErrorResponse("page phải từ 1 trở lên"));
        if (pageSize < 1) return BadRequest(new ErrorResponse("pageSize phải từ 1 trở lên"));
        pageSize = Math.Min(pageSize, MaxPageSize);

        var grouped = OpenReports()
            .GroupBy(r => r.RecipeId)
            .Select(g => new { RecipeId = g.Key, Count = g.Count(), Latest = g.Max(r => r.CreatedAt) });
        var total = await grouped.CountAsync();
        // Bài nhiều báo cáo nhất lên trước, cùng số thì bài có báo cáo mới hơn lên trước
        var pageRows = await grouped
            .OrderByDescending(g => g.Count)
            .ThenByDescending(g => g.Latest)
            .ThenBy(g => g.RecipeId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var ids = pageRows.Select(g => g.RecipeId).ToList();
        var recipes = await db.Recipes.AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .Select(r => new { r.Id, r.Name, r.ImageUrl, r.AuthorId, AuthorName = r.Author != null ? r.Author.FullName : null })
            .ToDictionaryAsync(r => r.Id);
        var reports = await OpenReports()
            .Where(r => ids.Contains(r.RecipeId))
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new { r.RecipeId, Dto = new ReportDetailDto(r.Id, r.Reason, r.Detail, r.Reporter.FullName, r.CreatedAt) })
            .ToListAsync();

        var items = pageRows.Select(g =>
        {
            var r = recipes[g.RecipeId];
            return new ReportedRecipeDto(
                r.Id, r.Name, r.ImageUrl, r.AuthorId, r.AuthorName, g.Count, g.Latest,
                reports.Where(x => x.RecipeId == g.RecipeId).Select(x => x.Dto).Take(ReportsPerRecipe).ToList());
        }).ToList();

        return Ok(new PagedResult<ReportedRecipeDto>(items, page, pageSize, total));
    }

    // Số bài đang có báo cáo chưa xử lý
    [HttpGet("count")]
    [ProducesResponseType<PendingCountDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Count() =>
        Ok(new PendingCountDto(await OpenReports().Select(r => r.RecipeId).Distinct().CountAsync()));

    // Gỡ bài đang hiển thị: chuyển về Rejected kèm lý do (chủ bài thấy ở Bài của tôi), báo cho tác giả.
    [HttpPost("recipes/{recipeId:int}/takedown")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Takedown(int recipeId, TakedownRequest req)
    {
        var reason = req.Reason.Trim();
        if (reason.Length < 5)
            return BadRequest(new ErrorResponse("Lý do phải từ 5 đến 500 ký tự"));

        var recipe = await db.Recipes.SingleOrDefaultAsync(r => r.Id == recipeId && !r.IsDeleted);
        if (recipe == null)
            return NotFound(new ErrorResponse("Không tìm thấy bài"));
        if (recipe.Status != RecipeStatus.Approved)
            return Conflict(new ErrorResponse("Bài này không còn hiển thị"));

        var now = DateTime.UtcNow;
        recipe.Status = RecipeStatus.Rejected;
        recipe.RejectReason = reason;
        recipe.ReviewedAt = now;
        recipe.ReviewedById = User.GetUserId();
        await ResolveOpenAsync(recipeId, ReportResolutions.Removed, now);
        Notifier.PostRemoved(db, recipe, now);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Admin khác vừa gỡ/xử lý cùng bài (Status là concurrency token)
            return Conflict(new ErrorResponse("Bài này đã được xử lý"));
        }
        return NoContent();
    }

    // Bỏ qua: báo cáo không hợp lệ, bài giữ nguyên
    [HttpPost("recipes/{recipeId:int}/dismiss")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Dismiss(int recipeId)
    {
        var count = await ResolveOpenAsync(recipeId, ReportResolutions.Dismissed, DateTime.UtcNow);
        if (count == 0)
            return NotFound(new ErrorResponse("Bài này không có báo cáo chờ xử lý"));
        await db.SaveChangesAsync();
        return NoContent();
    }

    private IQueryable<RecipeReport> OpenReports() =>
        db.RecipeReports.AsNoTracking().Where(r => r.Status == ReportStatus.Open && !r.Recipe.IsDeleted);

    private async Task<int> ResolveOpenAsync(int recipeId, string resolution, DateTime now)
    {
        var open = await db.RecipeReports
            .Where(r => r.RecipeId == recipeId && r.Status == ReportStatus.Open)
            .ToListAsync();
        var adminId = User.GetUserId();
        foreach (var r in open)
        {
            r.Status = ReportStatus.Resolved;
            r.Resolution = resolution;
            r.ResolvedAt = now;
            r.ResolvedById = adminId;
        }
        return open.Count;
    }
}
