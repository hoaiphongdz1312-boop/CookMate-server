using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CookMate.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace CookMate.Api.Tests;

// Tự duyệt theo ngưỡng, báo cáo vi phạm, chặn người dùng, thông báo (kehoach-duyet-bai.md giai đoạn 5)
public class ModerationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<object> PostBodyAsync(string name)
    {
        var categoryId = await factory.WithDb(db => db.Categories.OrderBy(c => c.Id).Select(c => c.Id).FirstAsync());
        return new
        {
            name,
            description = "Món thử nghiệm",
            imageUrl = (string?)null,
            difficulty = 1,
            prepMinutes = 5,
            cookMinutes = 10,
            baseServings = 2,
            categoryId,
            ingredients = new[] { new { name = "Trứng", quantity = 2m, unit = "quả", isOptional = false } },
            steps = new[] { new { instruction = "Chiên trứng", timerSeconds = 0 } }
        };
    }

    // Trả về (id, status) của bài vừa đăng
    private async Task<(int Id, int Status)> ShareAsync(HttpClient c, string name)
    {
        var res = await c.PostAsJsonAsync("/api/community/recipes", await PostBodyAsync(name));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        return (json.GetProperty("id").GetInt32(), json.GetProperty("status").GetInt32());
    }

    private async Task<int> ApprovedPostAsync(HttpClient author, HttpClient admin, string name)
    {
        var (id, _) = await ShareAsync(author, name);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/recipes/{id}/approve", null)).StatusCode);
        return id;
    }

    private static async Task<List<string>> FeedNamesAsync(HttpClient c)
    {
        var res = await c.GetFromJsonAsync<JsonElement>("/api/community/recipes?pageSize=50");
        return res.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToList();
    }

    private static async Task<int> UnreadAsync(HttpClient c) =>
        (await c.GetFromJsonAsync<JsonElement>("/api/notifications/unread-count")).GetProperty("count").GetInt32();

    // ---------- Ngưỡng tự duyệt ----------

    [Fact]
    public async Task Author_with_three_approved_posts_publishes_directly()
    {
        var author = factory.ClientFor(await factory.CreateUserAsync("tincay@test.vn"));
        var admin = factory.ClientFor(await factory.CreateUserAsync("admin-tc@test.vn", role: Roles.Admin));
        var viewer = factory.ClientFor(await factory.CreateUserAsync("xem-tc@test.vn"));

        for (var i = 1; i <= 2; i++)
            await ApprovedPostAsync(author, admin, $"Bài tin cậy {i}");
        // Mới 2 bài được duyệt: bài thứ 3 vẫn phải chờ
        var (third, status3) = await ShareAsync(author, "Bài tin cậy 3");
        Assert.Equal(RecipeStatus.Pending, status3);
        Assert.DoesNotContain("Bài tin cậy 3", await FeedNamesAsync(viewer));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/recipes/{third}/approve", null)).StatusCode);

        var (fourth, status4) = await ShareAsync(author, "Bài tin cậy 4");
        Assert.Equal(RecipeStatus.Approved, status4);
        Assert.Contains("Bài tin cậy 4", await FeedNamesAsync(viewer));
        var saved = await factory.WithDb(db => db.Recipes.SingleAsync(r => r.Id == fourth));
        Assert.NotNull(saved.ReviewedAt);
        Assert.Null(saved.ReviewedById);
    }

    [Fact]
    public async Task Taken_down_post_no_longer_counts_toward_threshold()
    {
        var author = factory.ClientFor(await factory.CreateUserAsync("mattin@test.vn"));
        var admin = factory.ClientFor(await factory.CreateUserAsync("admin-mt@test.vn", role: Roles.Admin));
        var reporter = factory.ClientFor(await factory.CreateUserAsync("baocao-mt@test.vn"));

        var first = await ApprovedPostAsync(author, admin, "Bài MT 1");
        await ApprovedPostAsync(author, admin, "Bài MT 2");
        await ApprovedPostAsync(author, admin, "Bài MT 3");

        await reporter.PostAsJsonAsync($"/api/community/recipes/{first}/report", new { reason = "spam" });
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync(
            $"/api/admin/reports/recipes/{first}/takedown", new { reason = "Quảng cáo trá hình" })).StatusCode);

        var (_, status) = await ShareAsync(author, "Bài MT 4");
        Assert.Equal(RecipeStatus.Pending, status);
    }

    // ---------- Báo cáo ----------

    [Fact]
    public async Task Report_rules()
    {
        var author = factory.ClientFor(await factory.CreateUserAsync("tg-bc@test.vn"));
        var admin = factory.ClientFor(await factory.CreateUserAsync("admin-bc@test.vn", role: Roles.Admin));
        var reporter = factory.ClientFor(await factory.CreateUserAsync("ngbc@test.vn"));
        var approved = await ApprovedPostAsync(author, admin, "Bài bị báo cáo");
        var (pending, _) = await ShareAsync(author, "Bài chưa duyệt BC");

        Assert.Equal(HttpStatusCode.BadRequest,
            (await reporter.PostAsJsonAsync($"/api/community/recipes/{approved}/report", new { reason = "xyz" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await reporter.PostAsJsonAsync($"/api/community/recipes/{approved}/report", new { reason = "other" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await reporter.PostAsJsonAsync($"/api/community/recipes/{pending}/report", new { reason = "spam" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await author.PostAsJsonAsync($"/api/community/recipes/{approved}/report", new { reason = "spam" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await reporter.PostAsJsonAsync(
            $"/api/community/recipes/{approved}/report", new { reason = "wrong_info", detail = "Sai lượng muối" })).StatusCode);
        // Lần 2 bởi cùng người: bị chặn
        Assert.Equal(HttpStatusCode.Conflict,
            (await reporter.PostAsJsonAsync($"/api/community/recipes/{approved}/report", new { reason = "spam" })).StatusCode);

        // Công thức hệ thống cũng báo cáo được
        var systemId = await factory.WithDb(db => db.Recipes.Where(r => r.AuthorId == null).Select(r => r.Id).FirstAsync());
        Assert.Equal(HttpStatusCode.NoContent,
            (await reporter.PostAsJsonAsync($"/api/community/recipes/{systemId}/report", new { reason = "wrong_info" })).StatusCode);
    }

    [Fact]
    public async Task Admin_takedown_hides_post_resolves_reports_and_notifies_author()
    {
        var authorUser = await factory.CreateUserAsync("tg-go@test.vn");
        var author = factory.ClientFor(authorUser);
        var admin = factory.ClientFor(await factory.CreateUserAsync("admin-go@test.vn", role: Roles.Admin));
        var r1 = factory.ClientFor(await factory.CreateUserAsync("bc1-go@test.vn", "Người Báo Một"));
        var r2 = factory.ClientFor(await factory.CreateUserAsync("bc2-go@test.vn"));
        var id = await ApprovedPostAsync(author, admin, "Bài sẽ bị gỡ");

        await r1.PostAsJsonAsync($"/api/community/recipes/{id}/report", new { reason = "offensive" });
        await r2.PostAsJsonAsync($"/api/community/recipes/{id}/report", new { reason = "spam" });

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/admin/reports?pageSize=50");
        var row = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("recipeId").GetInt32() == id);
        Assert.Equal(2, row.GetProperty("reportCount").GetInt32());
        Assert.Contains(row.GetProperty("reports").EnumerateArray(), x => x.GetProperty("reporterName").GetString() == "Người Báo Một");

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"/api/admin/reports/recipes/{id}/takedown", new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync(
            $"/api/admin/reports/recipes/{id}/takedown", new { reason = "Nội dung phản cảm" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(
            $"/api/admin/reports/recipes/{id}/takedown", new { reason = "Nội dung phản cảm" })).StatusCode);

        Assert.DoesNotContain("Bài sẽ bị gỡ", await FeedNamesAsync(r1));
        var mine = await author.GetFromJsonAsync<JsonElement>("/api/community/recipes/mine?pageSize=50");
        var post = mine.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == id);
        Assert.Equal(RecipeStatus.Rejected, post.GetProperty("status").GetInt32());
        Assert.Equal("Nội dung phản cảm", post.GetProperty("rejectReason").GetString());

        Assert.Equal(0, await factory.WithDb(db =>
            db.RecipeReports.CountAsync(r => r.RecipeId == id && r.Status == ReportStatus.Open)));
        var list2 = await admin.GetFromJsonAsync<JsonElement>("/api/admin/reports?pageSize=50");
        Assert.DoesNotContain(list2.GetProperty("items").EnumerateArray(), i => i.GetProperty("recipeId").GetInt32() == id);

        var types = await factory.WithDb(db => db.Notifications
            .Where(n => n.UserId == authorUser.Id && n.RecipeId == id).Select(n => n.Type).ToListAsync());
        Assert.Contains(NotificationTypes.PostRemoved, types);
    }

    [Fact]
    public async Task Dismiss_keeps_post_visible()
    {
        var author = factory.ClientFor(await factory.CreateUserAsync("tg-bq@test.vn"));
        var admin = factory.ClientFor(await factory.CreateUserAsync("admin-bq@test.vn", role: Roles.Admin));
        var reporter = factory.ClientFor(await factory.CreateUserAsync("bc-bq@test.vn"));
        var id = await ApprovedPostAsync(author, admin, "Bài bị báo nhầm");
        await reporter.PostAsJsonAsync($"/api/community/recipes/{id}/report", new { reason = "copyright" });

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/reports/recipes/{id}/dismiss", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/admin/reports/recipes/{id}/dismiss", null)).StatusCode);
        Assert.Contains("Bài bị báo nhầm", await FeedNamesAsync(reporter));
    }

    [Fact]
    public async Task Report_admin_endpoints_require_admin()
    {
        var c = factory.ClientFor(await factory.CreateUserAsync("thuong-bc@test.vn"));
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/admin/reports")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/admin/reports/count")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.PostAsJsonAsync("/api/admin/reports/recipes/1/takedown", new { reason = "không phù hợp" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/admin/reports/recipes/1/dismiss", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/admin/reports")).StatusCode);
    }

    // ---------- Chặn ----------

    [Fact]
    public async Task Blocked_author_disappears_from_blockers_feed_only()
    {
        var authorUser = await factory.CreateUserAsync("tg-chan@test.vn", "Tác Giả Bị Chặn");
        var author = factory.ClientFor(authorUser);
        var admin = factory.ClientFor(await factory.CreateUserAsync("admin-chan@test.vn", role: Roles.Admin));
        var blockerUser = await factory.CreateUserAsync("nguoichan@test.vn");
        var blocker = factory.ClientFor(blockerUser);
        var other = factory.ClientFor(await factory.CreateUserAsync("nguoikhac-chan@test.vn"));
        await ApprovedPostAsync(author, admin, "Bài của người bị chặn");

        Assert.Equal(HttpStatusCode.BadRequest, (await blocker.PostAsync($"/api/users/{blockerUser.Id}/block", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await blocker.PostAsync("/api/users/999999/block", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await blocker.PostAsync($"/api/users/{authorUser.Id}/block", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await blocker.PostAsync($"/api/users/{authorUser.Id}/block", null)).StatusCode);

        Assert.DoesNotContain("Bài của người bị chặn", await FeedNamesAsync(blocker));
        Assert.Contains("Bài của người bị chặn", await FeedNamesAsync(other));

        var blocks = await blocker.GetFromJsonAsync<JsonElement>("/api/users/me/blocks");
        Assert.Equal("Tác Giả Bị Chặn", blocks.EnumerateArray().Single().GetProperty("fullName").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await blocker.DeleteAsync($"/api/users/{authorUser.Id}/block")).StatusCode);
        Assert.Contains("Bài của người bị chặn", await FeedNamesAsync(blocker));
    }

    // ---------- Thông báo ----------

    [Fact]
    public async Task Author_is_notified_on_approve_and_reject()
    {
        var author = factory.ClientFor(await factory.CreateUserAsync("tg-tb@test.vn"));
        var admin = factory.ClientFor(await factory.CreateUserAsync("admin-tb@test.vn", role: Roles.Admin));
        var (a, _) = await ShareAsync(author, "Bài TB duyệt");
        var (b, _) = await ShareAsync(author, "Bài TB từ chối");
        Assert.Equal(0, await UnreadAsync(author));

        await admin.PostAsync($"/api/admin/recipes/{a}/approve", null);
        await admin.PostAsJsonAsync($"/api/admin/recipes/{b}/reject", new { reason = "Thiếu các bước nấu" });
        Assert.Equal(2, await UnreadAsync(author));

        var list = await author.GetFromJsonAsync<JsonElement>("/api/notifications");
        var items = list.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, n => n.GetProperty("type").GetString() == NotificationTypes.PostApproved
                                    && n.GetProperty("recipeId").GetInt32() == a);
        Assert.Contains(items, n => n.GetProperty("type").GetString() == NotificationTypes.PostRejected
                                    && n.GetProperty("body").GetString()!.Contains("Thiếu các bước nấu"));

        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync("/api/notifications/read-all", null)).StatusCode);
        Assert.Equal(0, await UnreadAsync(author));
        // Thông báo của người khác không lẫn vào
        Assert.Equal(0, await UnreadAsync(admin));
    }
}
