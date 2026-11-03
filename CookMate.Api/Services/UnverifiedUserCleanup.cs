using CookMate.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CookMate.Api.Services;

// Chạy nền: xóa tài khoản không xác thực email trong vòng MaxAge kể từ lúc đăng ký.
// Mã email, phiên nấu, lịch sử... của user bị xóa theo nhờ khóa ngoại ON DELETE CASCADE.
public class UnverifiedUserCleanup(IServiceScopeFactory scopes, ILogger<UnverifiedUserCleanup> logger) : BackgroundService
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var cutoff = DateTime.UtcNow - MaxAge;
                    var deleted = await db.Users
                        .Where(u => !u.EmailConfirmed && u.CreatedAt < cutoff)
                        .ExecuteDeleteAsync(stoppingToken);
                    if (deleted > 0)
                        logger.LogInformation("Đã xóa {Count} tài khoản chưa xác thực email", deleted);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Dọn tài khoản chưa xác thực thất bại");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Server đang tắt: thoát êm, không coi là lỗi.
        }
    }
}
