using CookMate.Api.Data;
using CookMate.Api.Dtos;
using CookMate.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CookMate.Api.Controllers;

// Đăng ký / đăng nhập bằng khuôn mặt (KE_HOACH_v2 mục 3).
// Không ghi embedding hay điểm cosine vào log.
[ApiController]
[Route("api/auth")]
public class FaceAuthController(
    AppDbContext db,
    FaceMatchingService faces,
    JwtTokenService jwt,
    ILogger<FaceAuthController> logger) : ControllerBase
{
    private const string NotRecognized = "Không nhận ra khuôn mặt";
    private const int LoginVectors = 3;
    private const int MinEnrollVectors = 3;
    private const int MaxEnrollVectors = 5;

    [HttpPost("face-enroll")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Enroll(FaceEnrollRequest req)
    {
        var userId = User.GetUserId();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return Unauthorized(new ErrorResponse("Chưa đăng nhập"));

        // Số chiều lấy từ người đăng ký mặt đầu tiên (trừ chính mình, để đăng ký lại được)
        var systemDim = await db.Users
            .Where(u => u.Id != userId && u.FaceDim != null)
            .Select(u => u.FaceDim)
            .FirstOrDefaultAsync();

        int dim;
        try
        {
            dim = faces.Validate(req.Embeddings, MinEnrollVectors, MaxEnrollVectors, systemDim);
        }
        catch (FaceValidationException ex)
        {
            return BadRequest(new ErrorResponse(ex.Message));
        }

        var samples = req.Embeddings.Select(FaceMatchingService.L2Normalize).ToList();
        if (!faces.AreSamplesConsistent(samples))
            return BadRequest(new ErrorResponse("Các mẫu khuôn mặt không đồng nhất, vui lòng chụp lại"));

        var embedding = FaceMatchingService.Average(samples);

        var others = await LoadCandidatesAsync(excludeUserId: userId, dim);
        if (faces.IsDuplicate(embedding, others))
            return Conflict(new ErrorResponse("Khuôn mặt này đã được đăng ký cho một tài khoản khác"));

        user.FaceEmbedding = FaceMatchingService.ToBytes(embedding);
        user.FaceDim = dim;
        user.FaceEnrolledAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation("User {UserId} đã đăng ký khuôn mặt", userId);
        return NoContent();
    }

    // Thất bại vì không nhận ra luôn trả 401 cùng một thông báo, không lộ ai giống nhất
    [HttpPost("face-login")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(FaceLoginRequest req)
    {
        var systemDim = await db.Users
            .Where(u => u.FaceDim != null)
            .Select(u => u.FaceDim)
            .FirstOrDefaultAsync();
        if (systemDim == null)
            return Unauthorized(new ErrorResponse(NotRecognized));

        int dim;
        try
        {
            dim = faces.Validate(req.Embeddings, LoginVectors, LoginVectors, systemDim);
        }
        catch (FaceValidationException ex)
        {
            return BadRequest(new ErrorResponse(ex.Message));
        }

        var queries = req.Embeddings.Select(FaceMatchingService.L2Normalize).ToList();
        var candidates = await LoadCandidatesAsync(excludeUserId: null, dim);
        var userId = faces.FindMatch(queries, candidates);
        if (userId == null)
        {
            logger.LogInformation("Đăng nhập khuôn mặt thất bại");
            return Unauthorized(new ErrorResponse(NotRecognized));
        }

        var user = await db.Users.SingleAsync(u => u.Id == userId);
        logger.LogInformation("User {UserId} đăng nhập bằng khuôn mặt", user.Id);
        return Ok(new AuthResponse(jwt.CreateToken(user), user.ToDto()));
    }

    [HttpDelete("face")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Delete()
    {
        var userId = User.GetUserId();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return Unauthorized(new ErrorResponse("Chưa đăng nhập"));

        user.FaceEmbedding = null;
        user.FaceDim = null;
        user.FaceEnrolledAt = null;
        await db.SaveChangesAsync();

        logger.LogInformation("User {UserId} đã xóa dữ liệu khuôn mặt", userId);
        return NoContent();
    }

    // Chỉ tài khoản đã xác thực email mới được đăng nhập bằng mặt
    private async Task<List<FaceCandidate>> LoadCandidatesAsync(int? excludeUserId, int dim)
    {
        var rows = await db.Users
            .AsNoTracking()
            .Where(u => u.FaceEmbedding != null && u.FaceDim == dim && u.EmailConfirmed)
            .Where(u => excludeUserId == null || u.Id != excludeUserId)
            .Select(u => new { u.Id, u.FaceEmbedding })
            .ToListAsync();
        return rows.Select(r => new FaceCandidate(r.Id, FaceMatchingService.FromBytes(r.FaceEmbedding!))).ToList();
    }
}
