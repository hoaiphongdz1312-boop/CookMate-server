using CookMate.Api.Data;
using CookMate.Api.Dtos;
using CookMate.Api.Entities;
using CookMate.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CookMate.Api.Controllers;

[ApiController]
[Route("api/recipes")]
public class RecipesController(AppDbContext db) : ControllerBase
{
    private const int MaxPageSize = 50;

    [HttpGet]
    [ProducesResponseType<PagedResult<RecipeSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetList(
        string? search,
        int? categoryId,
        int? difficulty,
        int? maxMinutes,
        int page = 1,
        int pageSize = 20)
    {
        if (difficulty is < 1 or > 3)
            return BadRequest(new ErrorResponse("difficulty phải từ 1 đến 3"));
        if (maxMinutes is < 0)
            return BadRequest(new ErrorResponse("maxMinutes không được âm"));
        if (page < 1)
            return BadRequest(new ErrorResponse("page phải từ 1 trở lên"));
        if (pageSize < 1)
            return BadRequest(new ErrorResponse("pageSize phải từ 1 trở lên"));
        pageSize = Math.Min(pageSize, MaxPageSize);

        // Tab Công thức chỉ chứa công thức hệ thống; bài cộng đồng nằm ở /api/community/recipes
        var query = db.Recipes.AsNoTracking()
            .Where(r => r.Status == RecipeStatus.Approved && r.AuthorId == null && !r.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Collation mặc định của SQL Server không phân biệt hoa thường;
            // ToLower hai vế để không phụ thuộc collation của DB
            var term = search.Trim().ToLower();
            query = query.Where(r => r.Name.ToLower().Contains(term));
        }
        if (categoryId.HasValue)
            query = query.Where(r => r.CategoryId == categoryId);
        if (difficulty.HasValue)
            query = query.Where(r => r.Difficulty == difficulty);
        if (maxMinutes.HasValue)
            query = query.Where(r => r.PrepMinutes + r.CookMinutes <= maxMinutes);

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(r => r.Name)
            .ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new { Dto = new RecipeSummaryDto(
                r.Id, r.Name, r.Description, r.ImageUrl, r.Difficulty,
                r.PrepMinutes, r.CookMinutes, r.CategoryId, new List<string>()), r.Tastes })
            .ToListAsync();

        var dtos = items.Select(i => i.Dto with { Tastes = TasteTags.Parse(i.Tastes) }).ToList();
        return Ok(new PagedResult<RecipeSummaryDto>(dtos, page, pageSize, total));
    }

    // Món hệ thống đã duyệt xếp theo khẩu vị của người đang đăng nhập; khách hoặc người chưa chọn khẩu vị
    // nhận danh sách thường
    [HttpGet("recommended")]
    [ProducesResponseType<List<RecipeSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetRecommended(int limit = 10)
    {
        if (limit < 1)
            return BadRequest(new ErrorResponse("limit phải từ 1 trở lên"));
        limit = Math.Min(limit, MaxPageSize);

        var userTastes = new List<string>();
        if (User.Identity?.IsAuthenticated == true)
        {
            var userId = User.GetUserId();
            var stored = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId).Select(u => u.Tastes).SingleOrDefaultAsync();
            userTastes = TasteTags.Parse(stored);
        }

        var rows = await db.Recipes.AsNoTracking()
            .Where(r => r.Status == RecipeStatus.Approved && r.AuthorId == null && !r.IsDeleted)
            .Select(r => new { Dto = new RecipeSummaryDto(
                r.Id, r.Name, r.Description, r.ImageUrl, r.Difficulty,
                r.PrepMinutes, r.CookMinutes, r.CategoryId, new List<string>()), r.Tastes })
            .ToListAsync();

        var recipes = rows.Select(x => x.Dto with { Tastes = TasteTags.Parse(x.Tastes) });
        return Ok(TasteRanking.Rank(recipes, userTastes).Take(limit).ToList());
    }

    // Quay ngẫu nhiên một món hệ thống đã duyệt cho "Hôm nay ăn gì?". Đếm rồi Skip(n) thay vì ORDER BY NEWID()
    // hay Random trong truy vấn, để không phụ thuộc cách provider dịch hàm random.
    [HttpGet("random")]
    [ProducesResponseType<RecipeSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRandom(
        int? categoryId,
        int? difficulty,
        int? maxMinutes,
        [FromQuery] int[]? excludeIds)
    {
        if (difficulty is < 1 or > 3)
            return BadRequest(new ErrorResponse("difficulty phải từ 1 đến 3"));
        if (maxMinutes is < 0)
            return BadRequest(new ErrorResponse("maxMinutes không được âm"));

        var query = db.Recipes.AsNoTracking()
            .Where(r => r.Status == RecipeStatus.Approved && r.AuthorId == null && !r.IsDeleted);

        if (categoryId.HasValue)
            query = query.Where(r => r.CategoryId == categoryId);
        if (difficulty.HasValue)
            query = query.Where(r => r.Difficulty == difficulty);
        if (maxMinutes.HasValue)
            query = query.Where(r => r.PrepMinutes + r.CookMinutes <= maxMinutes);
        if (excludeIds is { Length: > 0 })
            query = query.Where(r => !excludeIds.Contains(r.Id));

        var count = await query.CountAsync();
        if (count == 0)
            return NotFound(new ErrorResponse("Không có món nào phù hợp, hãy thử nới bộ lọc"));

        var picked = await query
            .OrderBy(r => r.Id)
            .Skip(Random.Shared.Next(count))
            .Select(r => new { Dto = new RecipeSummaryDto(
                r.Id, r.Name, r.Description, r.ImageUrl, r.Difficulty,
                r.PrepMinutes, r.CookMinutes, r.CategoryId, new List<string>()), r.Tastes })
            .FirstAsync();

        return Ok(picked.Dto with { Tastes = TasteTags.Parse(picked.Tastes) });
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType<RecipeDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        // Bài đã duyệt: ai cũng xem được. Bài chờ duyệt/bị từ chối: chỉ tác giả và admin,
        // người khác nhận 404 như thể bài không tồn tại.
        var viewerId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : (int?)null;
        var isAdmin = User.IsInRole(Roles.Admin);

        var found = await db.Recipes
            .AsNoTracking()
            .Where(r => r.Id == id && !r.IsDeleted
                && (r.Status == RecipeStatus.Approved
                    || isAdmin
                    || (viewerId != null && r.AuthorId == viewerId)))
            .Select(r => new { r.Tastes, Dto = new RecipeDetailDto(
                r.Id, r.Name, r.Description, r.ImageUrl, r.Difficulty,
                r.PrepMinutes, r.CookMinutes, r.BaseServings, r.CategoryId,
                r.Ingredients
                    .OrderBy(i => i.Id)
                    .Select(i => new IngredientDto(i.Id, i.Name, i.Quantity, i.Unit, i.IsOptional))
                    .ToList(),
                r.Steps
                    .OrderBy(s => s.StepNumber)
                    .Select(s => new StepDto(s.StepNumber, s.Instruction, s.TimerSeconds))
                    .ToList(),
                new List<string>(),
                r.AuthorId,
                r.Author != null ? r.Author.FullName : null,
                isAdmin || (viewerId != null && r.AuthorId == viewerId) ? r.Status : (int?)null,
                isAdmin || (viewerId != null && r.AuthorId == viewerId) ? r.RejectReason : null) })
            .SingleOrDefaultAsync();

        return found == null
            ? NotFound(new ErrorResponse("Không tìm thấy công thức"))
            : Ok(found.Dto with { Tastes = TasteTags.Parse(found.Tastes) });
    }
}
