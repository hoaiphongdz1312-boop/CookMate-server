using CookMate.Api.Data;
using CookMate.Api.Dtos;
using CookMate.Api.Entities;
using CookMate.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CookMate.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public class UsersController(AppDbContext db) : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me()
    {
        var userId = User.GetUserId();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId);

        // Token hợp lệ nhưng tài khoản đã bị xóa
        if (user == null)
            return Unauthorized(new ErrorResponse("Chưa đăng nhập"));

        return Ok(user.ToDto());
    }

    // Rỗng = người dùng đã bỏ qua bước chọn khẩu vị; null = chưa được hỏi
    [HttpPut("me/tastes")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetTastes(TastesRequest request)
    {
        var tastes = request.Tastes ?? [];
        var unknown = tastes.FirstOrDefault(t => !TasteTags.All.Contains(t, StringComparer.OrdinalIgnoreCase));
        if (unknown != null)
            return BadRequest(new ErrorResponse($"Nhãn khẩu vị không hợp lệ: {unknown}"));

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == User.GetUserId());
        if (user == null)
            return Unauthorized(new ErrorResponse("Chưa đăng nhập"));

        user.Tastes = TasteTags.Normalize(tastes);
        await db.SaveChangesAsync();
        return Ok(user.ToDto());
    }

    // Danh sách người mình đã chặn, mới chặn ở trên cùng
    [HttpGet("me/blocks")]
    [ProducesResponseType<List<BlockedUserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Blocks()
    {
        var userId = User.GetUserId();
        var items = await db.UserBlocks.AsNoTracking()
            .Where(b => b.BlockerId == userId)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new BlockedUserDto(b.BlockedId, b.Blocked.FullName, b.CreatedAt))
            .ToListAsync();
        return Ok(items);
    }

    // Chặn người dùng: bài của họ không còn hiện trong feed cộng đồng của mình. Chặn lại lần nữa không lỗi.
    [HttpPost("{id:int}/block")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Block(int id)
    {
        var userId = User.GetUserId();
        if (id == userId)
            return BadRequest(new ErrorResponse("Không thể tự chặn chính mình"));
        if (!await db.Users.AnyAsync(u => u.Id == id))
            return NotFound(new ErrorResponse("Không tìm thấy người dùng"));
        if (await db.UserBlocks.AnyAsync(b => b.BlockerId == userId && b.BlockedId == id))
            return NoContent();

        db.UserBlocks.Add(new UserBlock { BlockerId = userId, BlockedId = id, CreatedAt = DateTime.UtcNow });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Bấm chặn hai lần cùng lúc: khóa chính (BlockerId, BlockedId) đã có, kết quả vẫn là đã chặn
        }
        return NoContent();
    }

    [HttpDelete("{id:int}/block")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unblock(int id)
    {
        var userId = User.GetUserId();
        var block = await db.UserBlocks.SingleOrDefaultAsync(b => b.BlockerId == userId && b.BlockedId == id);
        if (block != null)
        {
            db.UserBlocks.Remove(block);
            await db.SaveChangesAsync();
        }
        return NoContent();
    }
}
