using CookMate.Api.Data;
using CookMate.Api.Dtos;
using CookMate.Api.Entities;
using CookMate.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CookMate.Api.Controllers;

// Hàng đợi duyệt bài chia sẻ. Xem nội dung đầy đủ bằng GET /api/recipes/{id} (admin xem được bài chờ duyệt).
[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/recipes")]
public class AdminRecipesController(AppDbContext db) : ControllerBase
{
    private const int MaxPageSize = 50;

    [HttpGet("pending")]
    [ProducesResponseType<PagedResult<PendingPostDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Pending(int page = 1, int pageSize = 20)
    {
        if (page < 1) return BadRequest(new ErrorResponse("page phải từ 1 trở lên"));
        if (pageSize < 1) return BadRequest(new ErrorResponse("pageSize phải từ 1 trở lên"));
        pageSize = Math.Min(pageSize, MaxPageSize);

        var query = PendingQuery();
        var total = await query.CountAsync();
        // Bài gửi sớm nhất xử lý trước
        var items = await query
            .OrderBy(r => r.SubmittedAt)
            .ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new PendingPostDto(
                r.Id, r.Name, r.ImageUrl, r.AuthorId!.Value, r.Author!.FullName, r.SubmittedAt!.Value))
            .ToListAsync();

        return Ok(new PagedResult<PendingPostDto>(items, page, pageSize, total));
    }

    // Admin thêm công thức hệ thống: không có tác giả, duyệt sẵn, hiện ngay ở tab Công thức cho mọi người
    [HttpPost]
    [ProducesResponseType<MyPostDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(SavePostRequest req)
    {
        var error = PostValidator.Validate(req);
        if (error != null) return BadRequest(new ErrorResponse(error));
        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId))
            return BadRequest(new ErrorResponse("Danh mục không tồn tại"));

        var now = DateTime.UtcNow;
        var recipe = new Recipe
        {
            AuthorId = null,
            Status = RecipeStatus.Approved,
            SubmittedAt = now,
            ReviewedAt = now,
            ReviewedById = User.GetUserId()
        };
        CommunityController.Fill(recipe, req);

        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();
        return Created($"/api/recipes/{recipe.Id}", new MyPostDto(
            recipe.Id, recipe.Name, recipe.Description, recipe.ImageUrl, recipe.Difficulty,
            recipe.PrepMinutes, recipe.CookMinutes, recipe.CategoryId, recipe.Status, null, recipe.SubmittedAt));
    }

    [HttpGet("pending/count")]
    [ProducesResponseType<PendingCountDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PendingCount() =>
        Ok(new PendingCountDto(await PendingQuery().CountAsync()));

    [HttpPost("{id:int}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(int id)
    {
        return await ReviewAsync(id, RecipeStatus.Approved, null);
    }

    [HttpPost("{id:int}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(int id, RejectRequest req)
    {
        var reason = req.Reason.Trim();
        if (reason.Length < 5)
            return BadRequest(new ErrorResponse("Lý do phải từ 5 đến 500 ký tự"));

        return await ReviewAsync(id, RecipeStatus.Rejected, reason);
    }

    private IQueryable<Recipe> PendingQuery() =>
        db.Recipes.Where(r => r.Status == RecipeStatus.Pending && r.AuthorId != null && !r.IsDeleted);

    // Chỉ bài đang chờ duyệt mới xử lý được. Không thấy bài: 404. Bài đã được xử lý (kể cả bởi admin khác
    // vừa bấm cùng lúc, nhờ concurrency token trên Status): 409.
    private async Task<IActionResult> ReviewAsync(int id, int newStatus, string? reason)
    {
        var recipe = await db.Recipes.SingleOrDefaultAsync(r => r.Id == id && r.AuthorId != null && !r.IsDeleted);
        if (recipe == null)
            return NotFound(new ErrorResponse("Không tìm thấy bài"));
        if (recipe.Status != RecipeStatus.Pending)
            return Conflict(new ErrorResponse("Bài này đã được xử lý"));

        var now = DateTime.UtcNow;
        recipe.Status = newStatus;
        recipe.ReviewedAt = now;
        recipe.ReviewedById = User.GetUserId();
        recipe.RejectReason = reason;
        // Lưu cùng một SaveChanges: xung đột 409 thì thông báo cũng không được tạo
        Notifier.PostReviewed(db, recipe, now);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ErrorResponse("Bài này đã được xử lý"));
        }
        return NoContent();
    }
}
