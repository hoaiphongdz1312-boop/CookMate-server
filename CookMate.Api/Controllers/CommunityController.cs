using CookMate.Api.Data;
using CookMate.Api.Dtos;
using CookMate.Api.Entities;
using CookMate.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CookMate.Api.Controllers;

// Chia sẻ công thức: bài của tác giả mới chờ admin duyệt; tác giả đã có đủ CommunityOptions.AutoApproveAfter
// bài được duyệt thì bài mới lên feed ngay. Bài trên feed có thể bị báo cáo, người xem chặn được tác giả.
[ApiController]
[Authorize]
[Route("api/community")]
public class CommunityController(AppDbContext db, UploadOptions uploads, CommunityOptions options) : ControllerBase
{
    private const int MaxPageSize = 50;
    private const int MaxPendingPerUser = 10;
    private const long MaxImageBytes = 3 * 1024 * 1024;

    [HttpPost("images")]
    [RequestSizeLimit(MaxImageBytes + 64 * 1024)]
    [ProducesResponseType<UploadImageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadImage(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new ErrorResponse("Chưa chọn ảnh"));
        if (file.Length > MaxImageBytes)
            return BadRequest(new ErrorResponse("Ảnh tối đa 3 MB"));

        // Không tin đuôi file hay Content-Type: đọc vài byte đầu để nhận dạng định dạng thật
        var header = new byte[12];
        int read;
        await using (var s = file.OpenReadStream())
            read = await s.ReadAsync(header);
        var ext = DetectImageExtension(header.AsSpan(0, read));
        if (ext == null)
            return BadRequest(new ErrorResponse("Chỉ nhận ảnh JPG, PNG hoặc WebP"));

        // Tên file do server đặt, không dùng tên người dùng gửi lên
        var name = Guid.NewGuid().ToString("N") + ext;
        Directory.CreateDirectory(uploads.Directory);
        await using (var fs = System.IO.File.Create(Path.Combine(uploads.Directory, name)))
            await file.CopyToAsync(fs);

        return Ok(new UploadImageResponse("/uploads/" + name));
    }

    private static string? DetectImageExtension(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return ".jpg";
        if (h.Length >= 8 && h[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return ".png";
        if (h.Length >= 12 && h[..4].SequenceEqual("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8)) return ".webp";
        return null;
    }

    [HttpPost("recipes")]
    [EnableRateLimiting(RateLimitPolicies.SharePost)]
    [ProducesResponseType<MyPostDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Create(SavePostRequest req)
    {
        // Admin không chia sẻ bài cộng đồng (sẽ tự duyệt bài của mình); admin thêm công thức ở /api/admin/recipes
        if (User.IsInRole(Roles.Admin))
            return StatusCode(StatusCodes.Status403Forbidden,
                new ErrorResponse("Admin không chia sẻ bài cộng đồng, hãy dùng chức năng thêm công thức"));

        var error = PostValidator.Validate(req);
        if (error != null) return BadRequest(new ErrorResponse(error));
        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId))
            return BadRequest(new ErrorResponse("Danh mục không tồn tại"));

        var userId = User.GetUserId();
        if (await PendingCountAsync(userId) >= MaxPendingPerUser)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new ErrorResponse($"Bạn đang có {MaxPendingPerUser} bài chờ duyệt, hãy đợi admin xử lý bớt"));

        var now = DateTime.UtcNow;
        var trusted = await ApprovedCountAsync(userId) >= options.AutoApproveAfter;
        // Bài tự duyệt có ReviewedAt (để xếp trong feed) nhưng ReviewedById = null: không admin nào duyệt
        var recipe = new Recipe
        {
            AuthorId = userId,
            Status = trusted ? RecipeStatus.Approved : RecipeStatus.Pending,
            SubmittedAt = now,
            ReviewedAt = trusted ? now : null
        };
        Fill(recipe, req);

        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();
        return Created($"/api/recipes/{recipe.Id}", ToMine(recipe));
    }

    [HttpGet("recipes")]
    [ProducesResponseType<PagedResult<CommunityPostDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Feed(string? search, int? categoryId, int page = 1, int pageSize = 20)
    {
        var paging = CheckPaging(page, pageSize);
        if (paging != null) return paging;
        pageSize = Math.Min(pageSize, MaxPageSize);

        // Bỏ bài của những người mà người xem đã chặn
        var viewerId = User.GetUserId();
        var query = db.Recipes.AsNoTracking()
            .Where(r => r.Status == RecipeStatus.Approved && r.AuthorId != null && !r.IsDeleted)
            .Where(r => !db.UserBlocks.Any(b => b.BlockerId == viewerId && b.BlockedId == r.AuthorId));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(r => r.Name.ToLower().Contains(term));
        }
        if (categoryId.HasValue)
            query = query.Where(r => r.CategoryId == categoryId);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(r => r.ReviewedAt)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new CommunityPostDto(
                r.Id, r.Name, r.Description, r.ImageUrl, r.Difficulty,
                r.PrepMinutes, r.CookMinutes, r.CategoryId,
                r.AuthorId, r.Author!.FullName, r.ReviewedAt))
            .ToListAsync();

        return Ok(new PagedResult<CommunityPostDto>(items, page, pageSize, total));
    }

    [HttpGet("recipes/mine")]
    [ProducesResponseType<PagedResult<MyPostDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Mine(int? status, int page = 1, int pageSize = 20)
    {
        if (status is < 0 or > 2)
            return BadRequest(new ErrorResponse("status phải từ 0 đến 2"));
        var paging = CheckPaging(page, pageSize);
        if (paging != null) return paging;
        pageSize = Math.Min(pageSize, MaxPageSize);

        var userId = User.GetUserId();
        var query = db.Recipes.AsNoTracking().Where(r => r.AuthorId == userId && !r.IsDeleted);
        if (status.HasValue)
            query = query.Where(r => r.Status == status);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(r => r.SubmittedAt)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new MyPostDto(
                r.Id, r.Name, r.Description, r.ImageUrl, r.Difficulty,
                r.PrepMinutes, r.CookMinutes, r.CategoryId,
                r.Status, r.RejectReason, r.SubmittedAt))
            .ToListAsync();

        return Ok(new PagedResult<MyPostDto>(items, page, pageSize, total));
    }

    // Báo cáo bài đang hiển thị (bài cộng đồng hoặc công thức hệ thống). Mỗi người báo cáo một bài một lần.
    [HttpPost("recipes/{id:int}/report")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Report(int id, ReportRequest req)
    {
        var reason = req.Reason?.Trim().ToLowerInvariant();
        if (reason == null || !ReportReasons.All.Contains(reason))
            return BadRequest(new ErrorResponse("Lý do báo cáo không hợp lệ"));
        var detail = string.IsNullOrWhiteSpace(req.Detail) ? null : req.Detail.Trim();
        if (detail is { Length: > 500 })
            return BadRequest(new ErrorResponse("Mô tả tối đa 500 ký tự"));
        if (reason == ReportReasons.Other && detail == null)
            return BadRequest(new ErrorResponse("Vui lòng mô tả vi phạm"));

        var userId = User.GetUserId();
        var recipe = await db.Recipes.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == id && r.Status == RecipeStatus.Approved && !r.IsDeleted);
        if (recipe == null)
            return NotFound(new ErrorResponse("Không tìm thấy công thức"));
        if (recipe.AuthorId == userId)
            return BadRequest(new ErrorResponse("Không thể báo cáo bài của chính mình"));
        if (await db.RecipeReports.AnyAsync(r => r.RecipeId == id && r.ReporterId == userId))
            return Conflict(new ErrorResponse("Bạn đã báo cáo bài này rồi"));

        db.RecipeReports.Add(new RecipeReport
        {
            RecipeId = id,
            ReporterId = userId,
            Reason = reason,
            Detail = detail,
            CreatedAt = DateTime.UtcNow
        });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Hai yêu cầu cùng lúc cùng qua được AnyAsync: index duy nhất (RecipeId, ReporterId) chặn bản thứ hai
            return Conflict(new ErrorResponse("Bạn đã báo cáo bài này rồi"));
        }
        return NoContent();
    }

    // Chỉ sửa được khi chờ duyệt hoặc bị từ chối; sửa xong quay về chờ duyệt (kể cả tác giả đủ ngưỡng tự duyệt,
    // vì bài bị từ chối/bị gỡ cần admin xem lại). Bài đã duyệt không cho sửa để không đổi nội dung sau khi qua kiểm tra (muốn đổi thì xóa và đăng lại).
    [HttpPut("recipes/{id:int}")]
    [ProducesResponseType<MyPostDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(int id, SavePostRequest req)
    {
        var userId = User.GetUserId();
        var recipe = await db.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .SingleOrDefaultAsync(r => r.Id == id && r.AuthorId == userId && !r.IsDeleted);
        if (recipe == null)
            return NotFound(new ErrorResponse("Không tìm thấy bài"));
        if (recipe.Status == RecipeStatus.Approved)
            return Conflict(new ErrorResponse("Bài đã được duyệt không thể sửa, hãy xóa và đăng lại"));

        var error = PostValidator.Validate(req);
        if (error != null) return BadRequest(new ErrorResponse(error));
        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId))
            return BadRequest(new ErrorResponse("Danh mục không tồn tại"));

        if (recipe.Status == RecipeStatus.Rejected && await PendingCountAsync(userId) >= MaxPendingPerUser)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new ErrorResponse($"Bạn đang có {MaxPendingPerUser} bài chờ duyệt, hãy đợi admin xử lý bớt"));

        Apply(recipe, req);
        SyncIngredients(recipe, req.Ingredients!);
        SyncSteps(recipe, req.Steps!);

        recipe.Status = RecipeStatus.Pending;
        recipe.SubmittedAt = DateTime.UtcNow;
        recipe.ReviewedAt = null;
        recipe.ReviewedById = null;
        recipe.RejectReason = null;

        await db.SaveChangesAsync();
        return Ok(ToMine(recipe));
    }

    // Tác giả hoặc admin được xóa bài cộng đồng (mọi trạng thái). Xóa mềm vì CookingSession có thể còn tham chiếu.
    [HttpDelete("recipes/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var recipe = await db.Recipes.SingleOrDefaultAsync(r => r.Id == id && r.AuthorId != null && !r.IsDeleted);
        if (recipe == null)
            return NotFound(new ErrorResponse("Không tìm thấy bài"));
        if (recipe.AuthorId != User.GetUserId() && !User.IsInRole(Roles.Admin))
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse("Bạn không có quyền xóa bài này"));

        recipe.IsDeleted = true;
        recipe.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // Đếm thẳng từ bảng Recipes, không lưu cột đếm riêng. Bài bị gỡ chuyển sang Rejected nên tự rời số đếm.
    private Task<int> ApprovedCountAsync(int userId) =>
        db.Recipes.CountAsync(r => r.AuthorId == userId && r.Status == RecipeStatus.Approved && !r.IsDeleted);

    private Task<int> PendingCountAsync(int userId) =>
        db.Recipes.CountAsync(r => r.AuthorId == userId && r.Status == RecipeStatus.Pending && !r.IsDeleted);

    private IActionResult? CheckPaging(int page, int pageSize)
    {
        if (page < 1) return BadRequest(new ErrorResponse("page phải từ 1 trở lên"));
        if (pageSize < 1) return BadRequest(new ErrorResponse("pageSize phải từ 1 trở lên"));
        return null;
    }

    // Điền nội dung từ request đã validate vào công thức mới (dùng cả cho admin thêm công thức)
    internal static void Fill(Recipe recipe, SavePostRequest req)
    {
        Apply(recipe, req);
        recipe.Ingredients = req.Ingredients!.Select(NewIngredient).ToList();
        recipe.Steps = req.Steps!.Select((s, idx) => NewStep(s, idx + 1)).ToList();
    }

    private static void Apply(Recipe r, SavePostRequest req)
    {
        r.Name = req.Name!.Trim();
        r.Description = req.Description!.Trim();
        r.ImageUrl = req.ImageUrl;
        r.Difficulty = req.Difficulty;
        r.PrepMinutes = req.PrepMinutes;
        r.CookMinutes = req.CookMinutes;
        r.BaseServings = req.BaseServings;
        r.CategoryId = req.CategoryId;
    }

    private static RecipeIngredient NewIngredient(PostIngredientRequest i) => new()
    {
        Name = i.Name!.Trim(),
        Quantity = i.Quantity,
        Unit = i.Unit!.Trim(),
        IsOptional = i.IsOptional
    };

    private static RecipeStep NewStep(PostStepRequest s, int number) => new()
    {
        StepNumber = number,
        Instruction = s.Instruction!.Trim(),
        TimerSeconds = s.TimerSeconds
    };

    // Cập nhật tại chỗ theo thứ tự thay vì xóa rồi thêm, để không đụng index duy nhất (RecipeId, StepNumber)
    private void SyncIngredients(Recipe recipe, List<PostIngredientRequest> incoming)
    {
        var old = recipe.Ingredients.OrderBy(i => i.Id).ToList();
        for (var i = 0; i < incoming.Count; i++)
        {
            if (i < old.Count)
            {
                old[i].Name = incoming[i].Name!.Trim();
                old[i].Quantity = incoming[i].Quantity;
                old[i].Unit = incoming[i].Unit!.Trim();
                old[i].IsOptional = incoming[i].IsOptional;
            }
            else
                recipe.Ingredients.Add(NewIngredient(incoming[i]));
        }
        db.RecipeIngredients.RemoveRange(old.Skip(incoming.Count));
    }

    private void SyncSteps(Recipe recipe, List<PostStepRequest> incoming)
    {
        var old = recipe.Steps.OrderBy(s => s.StepNumber).ToList();
        for (var i = 0; i < incoming.Count; i++)
        {
            if (i < old.Count)
            {
                old[i].Instruction = incoming[i].Instruction!.Trim();
                old[i].TimerSeconds = incoming[i].TimerSeconds;
            }
            else
                recipe.Steps.Add(NewStep(incoming[i], i + 1));
        }
        db.RecipeSteps.RemoveRange(old.Skip(incoming.Count));
    }

    private static MyPostDto ToMine(Recipe r) => new(
        r.Id, r.Name, r.Description, r.ImageUrl, r.Difficulty, r.PrepMinutes, r.CookMinutes,
        r.CategoryId, r.Status, r.RejectReason, r.SubmittedAt);
}
