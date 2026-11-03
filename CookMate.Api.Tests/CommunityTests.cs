using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CookMate.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace CookMate.Api.Tests;

public class CommunityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<int> CategoryIdAsync() =>
        await factory.WithDb(db => db.Categories.OrderBy(c => c.Id).Select(c => c.Id).FirstAsync());

    private async Task<object> ValidPostAsync(string name, Action<Dictionary<string, object?>>? tweak = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["description"] = "Món thử nghiệm",
            ["imageUrl"] = null,
            ["difficulty"] = 2,
            ["prepMinutes"] = 10,
            ["cookMinutes"] = 20,
            ["baseServings"] = 2,
            ["categoryId"] = await CategoryIdAsync(),
            ["ingredients"] = new[] { new { name = "Trứng", quantity = 2m, unit = "quả", isOptional = false } },
            ["steps"] = new[]
            {
                new { instruction = "Đập trứng", timerSeconds = 0 },
                new { instruction = "Chiên", timerSeconds = 120 }
            }
        };
        tweak?.Invoke(body);
        return body;
    }

    private async Task<(HttpClient Client, int Id)> ShareAsync(User author, string name)
    {
        var c = factory.ClientFor(author);
        var res = await c.PostAsJsonAsync("/api/community/recipes", await ValidPostAsync(name));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (c, (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32());
    }

    private static async Task<List<string>> NamesAsync(HttpClient c, string url)
    {
        var res = await c.GetFromJsonAsync<JsonElement>(url);
        return res.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToList();
    }

    // ---------- Rò rỉ và hiển thị ----------

    [Fact]
    public async Task Seeded_recipes_stay_approved_and_listed()
    {
        var c = factory.CreateClient();
        var res = await c.GetFromJsonAsync<JsonElement>("/api/recipes");
        Assert.True(res.GetProperty("total").GetInt32() > 0);
        Assert.Equal(0, await factory.WithDb(db => db.Recipes.CountAsync(r => r.AuthorId == null && r.Status != RecipeStatus.Approved)));
    }

    [Fact]
    public async Task New_post_is_pending_and_hidden_from_lists_and_other_users()
    {
        var author = await factory.CreateUserAsync("tacgia1@test.vn", "Tác Giả Một");
        var other = await factory.CreateUserAsync("khac1@test.vn");
        var admin = await factory.CreateUserAsync("admin1@test.vn", role: Roles.Admin);
        var (ca, id) = await ShareAsync(author, "Bài chờ duyệt A");

        Assert.DoesNotContain("Bài chờ duyệt A", await NamesAsync(factory.CreateClient(), "/api/recipes?pageSize=50"));
        Assert.DoesNotContain("Bài chờ duyệt A", await NamesAsync(factory.ClientFor(other), "/api/community/recipes"));

        Assert.Equal(HttpStatusCode.OK, (await ca.GetAsync($"/api/recipes/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.ClientFor(admin).GetAsync($"/api/recipes/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.ClientFor(other).GetAsync($"/api/recipes/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync($"/api/recipes/{id}")).StatusCode);

        var detail = await ca.GetFromJsonAsync<JsonElement>($"/api/recipes/{id}");
        Assert.Equal(RecipeStatus.Pending, detail.GetProperty("status").GetInt32());
        Assert.Equal("Tác Giả Một", detail.GetProperty("authorName").GetString());
    }

    [Fact]
    public async Task Cannot_start_cooking_session_on_someone_elses_pending_post()
    {
        var author = await factory.CreateUserAsync("tacgia2@test.vn");
        var other = await factory.CreateUserAsync("khac2@test.vn");
        var (ca, id) = await ShareAsync(author, "Bài chờ duyệt B");

        var denied = await factory.ClientFor(other).PostAsJsonAsync("/api/cooking-sessions", new { recipeId = id, servings = 2 });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        // Tác giả thử nấu bài của mình thì được
        var ok = await ca.PostAsJsonAsync("/api/cooking-sessions", new { recipeId = id, servings = 2 });
        Assert.True(ok.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK);
    }

    // ---------- Quyền admin ----------

    [Fact]
    public async Task Admin_endpoints_reject_anonymous_and_normal_users()
    {
        var user = await factory.CreateUserAsync("thuong@test.vn");
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.CreateClient().GetAsync("/api/admin/recipes/pending")).StatusCode);
        var c = factory.ClientFor(user);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/admin/recipes/pending")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/admin/recipes/pending/count")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/admin/recipes/1/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.PostAsJsonAsync("/api/admin/recipes/1/reject", new { reason = "không phù hợp" })).StatusCode);
    }

    [Fact]
    public async Task Approve_publishes_post_and_second_approve_returns_409()
    {
        var author = await factory.CreateUserAsync("tacgia3@test.vn", "Tác Giả Ba");
        var other = await factory.CreateUserAsync("khac3@test.vn");
        var admin = await factory.CreateUserAsync("admin3@test.vn", role: Roles.Admin);
        var (_, id) = await ShareAsync(author, "Bài sẽ được duyệt");
        var ac = factory.ClientFor(admin);

        var pending = await ac.GetFromJsonAsync<JsonElement>("/api/admin/recipes/pending?pageSize=50");
        Assert.Contains(pending.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetInt32() == id);
        Assert.True((await ac.GetFromJsonAsync<JsonElement>("/api/admin/recipes/pending/count")).GetProperty("count").GetInt32() >= 1);

        Assert.Equal(HttpStatusCode.NoContent, (await ac.PostAsync($"/api/admin/recipes/{id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ac.PostAsync($"/api/admin/recipes/{id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ac.PostAsync("/api/admin/recipes/999999/approve", null)).StatusCode);

        var recipe = await factory.WithDb(db => db.Recipes.SingleAsync(r => r.Id == id));
        Assert.Equal(RecipeStatus.Approved, recipe.Status);
        Assert.NotNull(recipe.ReviewedAt);
        Assert.Equal(admin.Id, recipe.ReviewedById);

        // Hiện ở feed cộng đồng, không lẫn vào tab Công thức hệ thống
        var oc = factory.ClientFor(other);
        Assert.Contains("Bài sẽ được duyệt", await NamesAsync(oc, "/api/community/recipes"));
        Assert.DoesNotContain("Bài sẽ được duyệt", await NamesAsync(oc, "/api/recipes?pageSize=50"));
        // Người khác mở chi tiết và nấu như công thức thường, không thấy trạng thái nội bộ
        var detail = await oc.GetFromJsonAsync<JsonElement>($"/api/recipes/{id}");
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("status").ValueKind);
        var session = await oc.PostAsJsonAsync("/api/cooking-sessions", new { recipeId = id, servings = 2 });
        Assert.True(session.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ngắn")]
    public async Task Reject_without_valid_reason_returns_400(string? reason)
    {
        var author = await factory.CreateUserAsync($"tacgia4{reason?.Length}@test.vn");
        var admin = await factory.CreateUserAsync($"admin4{reason?.Length}@test.vn", role: Roles.Admin);
        var (_, id) = await ShareAsync(author, $"Bài lý do {reason?.Length}");

        var res = await factory.ClientFor(admin).PostAsJsonAsync($"/api/admin/recipes/{id}/reject", new { reason });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Reject_then_author_edits_and_resubmits()
    {
        var author = await factory.CreateUserAsync("tacgia5@test.vn");
        var admin = await factory.CreateUserAsync("admin5@test.vn", role: Roles.Admin);
        var (ca, id) = await ShareAsync(author, "Bài bị từ chối");
        var ac = factory.ClientFor(admin);

        Assert.Equal(HttpStatusCode.NoContent,
            (await ac.PostAsJsonAsync($"/api/admin/recipes/{id}/reject", new { reason = "Thiếu bước nấu chín" })).StatusCode);
        // Đã xử lý rồi thì không duyệt hay từ chối lại
        Assert.Equal(HttpStatusCode.Conflict, (await ac.PostAsync($"/api/admin/recipes/{id}/approve", null)).StatusCode);

        var mine = await ca.GetFromJsonAsync<JsonElement>("/api/community/recipes/mine?status=2");
        var item = mine.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == id);
        Assert.Equal("Thiếu bước nấu chín", item.GetProperty("rejectReason").GetString());

        // Sửa: bớt 1 bước, đổi tên → về Pending, xóa lý do
        var edit = await ValidPostAsync("Bài đã sửa", b => b["steps"] = new[] { new { instruction = "Chiên chín kỹ", timerSeconds = 300 } });
        var put = await ca.PutAsJsonAsync($"/api/community/recipes/{id}", edit);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var after = await put.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(RecipeStatus.Pending, after.GetProperty("status").GetInt32());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("rejectReason").ValueKind);

        var detail = await ca.GetFromJsonAsync<JsonElement>($"/api/recipes/{id}");
        Assert.Equal("Bài đã sửa", detail.GetProperty("name").GetString());
        Assert.Single(detail.GetProperty("steps").EnumerateArray());
    }

    // ---------- Sửa, xóa ----------

    [Fact]
    public async Task Approved_post_cannot_be_edited_and_other_users_cannot_edit_or_delete()
    {
        var author = await factory.CreateUserAsync("tacgia6@test.vn");
        var other = await factory.CreateUserAsync("khac6@test.vn");
        var admin = await factory.CreateUserAsync("admin6@test.vn", role: Roles.Admin);
        var (ca, id) = await ShareAsync(author, "Bài đã duyệt không sửa");
        var oc = factory.ClientFor(other);

        // Người khác sửa bài còn chờ duyệt của tác giả: 404; xóa: 403
        var edit = await ValidPostAsync("Sửa trộm");
        Assert.Equal(HttpStatusCode.NotFound, (await oc.PutAsJsonAsync($"/api/community/recipes/{id}", edit)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await oc.DeleteAsync($"/api/community/recipes/{id}")).StatusCode);

        await factory.ClientFor(admin).PostAsync($"/api/admin/recipes/{id}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, (await ca.PutAsJsonAsync($"/api/community/recipes/{id}", edit)).StatusCode);
    }

    [Fact]
    public async Task Delete_permissions_soft_delete_and_hidden_from_lists()
    {
        var a = await factory.CreateUserAsync("xoa_a@test.vn");
        var b = await factory.CreateUserAsync("xoa_b@test.vn");
        var admin = await factory.CreateUserAsync("xoa_admin@test.vn", role: Roles.Admin);
        var ac = factory.ClientFor(a);
        var adminClient = factory.ClientFor(admin);
        string Url(int id) => $"/api/community/recipes/{id}";

        // A xóa bài của A: 204; xóa lại: 404; A xóa bài của B: 403
        var (_, idA) = await ShareAsync(a, "Bài A tự xóa");
        var (_, idB) = await ShareAsync(b, "Bài B của người khác");
        Assert.Equal(HttpStatusCode.NoContent, (await ac.DeleteAsync(Url(idA))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ac.DeleteAsync(Url(idA))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ac.DeleteAsync(Url(idB))).StatusCode);

        // Admin xóa bài Pending, Approved, Rejected của người khác
        var (_, pending) = await ShareAsync(b, "Xóa pending");
        var (_, approved) = await ShareAsync(b, "Xóa approved");
        var (_, rejected) = await ShareAsync(b, "Xóa rejected");
        await adminClient.PostAsync($"/api/admin/recipes/{approved}/approve", null);
        await adminClient.PostAsJsonAsync($"/api/admin/recipes/{rejected}/reject", new { reason = "không phù hợp" });
        Assert.Contains("Xóa approved", await NamesAsync(ac, "/api/community/recipes"));
        foreach (var id in new[] { pending, approved, rejected })
            Assert.Equal(HttpStatusCode.NoContent, (await adminClient.DeleteAsync(Url(id))).StatusCode);

        // Không còn trong feed, bài của tôi, danh sách chờ duyệt
        var gone = new[] { "Bài A tự xóa", "Xóa pending", "Xóa approved", "Xóa rejected" };
        var visible = (await NamesAsync(ac, "/api/community/recipes"))
            .Concat(await NamesAsync(ac, "/api/community/recipes/mine"))
            .Concat(await NamesAsync(factory.ClientFor(b), "/api/community/recipes/mine"))
            .Concat(await NamesAsync(adminClient, "/api/admin/recipes/pending")).ToList();
        Assert.Empty(visible.Intersect(gone));

        // Vẫn còn dòng trong DB với IsDeleted = 1 và có DeletedAt
        var rows = await factory.WithDb(db => db.Recipes.Where(r => new[] { idA, pending, approved, rejected }.Contains(r.Id)).ToListAsync());
        Assert.Equal(4, rows.Count);
        Assert.All(rows, r => { Assert.True(r.IsDeleted); Assert.NotNull(r.DeletedAt); });
        Assert.False((await factory.WithDb(db => db.Recipes.SingleAsync(r => r.Id == idB))).IsDeleted);
    }

    [Fact]
    public async Task Author_delete_removes_approved_post_from_feed_even_with_cooking_session()
    {
        var author = await factory.CreateUserAsync("tacgia7@test.vn");
        var other = await factory.CreateUserAsync("khac7@test.vn");
        var admin = await factory.CreateUserAsync("admin7@test.vn", role: Roles.Admin);
        var (ca, id) = await ShareAsync(author, "Bài sẽ bị xóa");
        await factory.ClientFor(admin).PostAsync($"/api/admin/recipes/{id}/approve", null);
        var oc = factory.ClientFor(other);
        await oc.PostAsJsonAsync("/api/cooking-sessions", new { recipeId = id, servings = 2 });
        Assert.Contains("Bài sẽ bị xóa", await NamesAsync(oc, "/api/community/recipes"));

        Assert.Equal(HttpStatusCode.NoContent, (await ca.DeleteAsync($"/api/community/recipes/{id}")).StatusCode);

        Assert.DoesNotContain("Bài sẽ bị xóa", await NamesAsync(oc, "/api/community/recipes"));
        Assert.Equal(HttpStatusCode.NotFound, (await oc.GetAsync($"/api/recipes/{id}")).StatusCode);
        // Phiên nấu cũ vẫn còn trong DB (xóa mềm), không làm hỏng API tiến độ
        Assert.Equal(HttpStatusCode.OK, (await oc.GetAsync("/api/users/me/progress")).StatusCode);
    }

    // ---------- Validate ----------

    [Fact]
    public async Task Invalid_posts_return_400()
    {
        HttpClient c;
        var bad = new List<Action<Dictionary<string, object?>>>
        {
            b => b["name"] = "",
            b => b["name"] = new string('a', 101),
            b => b["difficulty"] = 4,
            b => b["baseServings"] = 0,
            b => b["categoryId"] = 999999,
            b => b["imageUrl"] = "https://evil.example/x.jpg",
            b => b["ingredients"] = Array.Empty<object>(),
            b => b["ingredients"] = new[] { new { name = "Muối", quantity = -1m, unit = "g", isOptional = false } },
            b => b["ingredients"] = Enumerable.Range(0, 41)
                .Select(i => new { name = "N" + i, quantity = 1m, unit = "g", isOptional = false }).ToArray(),
            b => b["steps"] = Array.Empty<object>(),
            b => b["steps"] = new[] { new { instruction = "  ", timerSeconds = 0 } },
            b => b["steps"] = new[] { new { instruction = "Chiên", timerSeconds = 86401 } },
            b => b["steps"] = Enumerable.Range(0, 31).Select(i => new { instruction = "B" + i, timerSeconds = 0 }).ToArray(),
        };
        var n = 0;
        foreach (var tweak in bad)
        {
            // Mỗi ca một người dùng: yêu cầu bị từ chối vì validate vẫn tính vào giới hạn 5 bài/giờ
            c = factory.ClientFor(await factory.CreateUserAsync($"validate{n++}@test.vn"));
            var res = await c.PostAsJsonAsync("/api/community/recipes", await ValidPostAsync("Món lỗi", tweak));
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }
    }

    // ---------- Upload ảnh ----------

    private static MultipartFormDataContent FileContent(byte[] bytes, string fileName)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", fileName);
        return form;
    }

    [Fact]
    public async Task Upload_accepts_real_png_and_rejects_fake_and_oversized()
    {
        var c = factory.ClientFor(await factory.CreateUserAsync("anh@test.vn"));
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

        var ok = await c.PostAsync("/api/community/images", FileContent(png, "x.png"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var url = (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("imageUrl").GetString()!;
        Assert.Matches(@"^/uploads/[0-9a-f]{32}\.png$", url);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync(url)).StatusCode);

        // File thực thi đổi đuôi .jpg bị từ chối vì byte đầu không phải ảnh
        byte[] exe = [0x4D, 0x5A, 0x90, 0, 3, 0, 0, 0, 4, 0, 0, 0];
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/community/images", FileContent(exe, "x.jpg"))).StatusCode);

        var big = new byte[3 * 1024 * 1024 + 1];
        png.CopyTo(big, 0);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/community/images", FileContent(big, "big.png"))).StatusCode);

        // Bài dùng ảnh đã upload thì được chấp nhận
        var post = await c.PostAsJsonAsync("/api/community/recipes", await ValidPostAsync("Món có ảnh", b => b["imageUrl"] = url));
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
    }

    // ---------- Giới hạn tần suất ----------

    [Fact]
    public async Task Sixth_post_in_an_hour_returns_429()
    {
        var c = factory.ClientFor(await factory.CreateUserAsync("spam@test.vn"));
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
            codes.Add((await c.PostAsJsonAsync("/api/community/recipes", await ValidPostAsync($"Spam {i}"))).StatusCode);

        Assert.All(codes.Take(5), s => Assert.Equal(HttpStatusCode.Created, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, codes[5]);
    }

    [Fact]
    public async Task More_than_ten_pending_posts_returns_429()
    {
        var user = await factory.CreateUserAsync("hangdoi@test.vn");
        var catId = await CategoryIdAsync();
        await factory.WithDb(async db =>
        {
            for (var i = 0; i < 10; i++)
                db.Recipes.Add(new Recipe
                {
                    Name = "Chờ " + i, Description = "d", Difficulty = 1, PrepMinutes = 1, CookMinutes = 1,
                    BaseServings = 1, CategoryId = catId, AuthorId = user.Id,
                    Status = RecipeStatus.Pending, SubmittedAt = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
            return 0;
        });

        var res = await factory.ClientFor(user).PostAsJsonAsync("/api/community/recipes", await ValidPostAsync("Bài thứ 11"));
        Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_share_community_post_but_adds_system_recipe_visible_to_everyone()
    {
        var admin = await factory.CreateUserAsync("admin8@test.vn", role: Roles.Admin);
        var user = await factory.CreateUserAsync("thuong8@test.vn");
        var ac = factory.ClientFor(admin);
        var body = await ValidPostAsync("Món do admin thêm");

        Assert.Equal(HttpStatusCode.Forbidden, (await ac.PostAsJsonAsync("/api/community/recipes", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.ClientFor(user).PostAsJsonAsync("/api/admin/recipes", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.CreateClient().PostAsJsonAsync("/api/admin/recipes", body)).StatusCode);

        var res = await ac.PostAsJsonAsync("/api/admin/recipes", body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // Hiện ngay ở tab Công thức cho người chưa đăng nhập lẫn user thường, không lẫn vào feed cộng đồng
        Assert.Contains("Món do admin thêm", await NamesAsync(factory.CreateClient(), "/api/recipes?pageSize=50"));
        Assert.DoesNotContain("Món do admin thêm", await NamesAsync(factory.ClientFor(user), "/api/community/recipes"));
        var detail = await factory.ClientFor(user).GetFromJsonAsync<JsonElement>($"/api/recipes/{id}");
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("authorId").ValueKind);

        // Dữ liệu sai vẫn bị chặn
        var bad = await ValidPostAsync("Lỗi", b => b["steps"] = Array.Empty<object>());
        Assert.Equal(HttpStatusCode.BadRequest, (await ac.PostAsJsonAsync("/api/admin/recipes", bad)).StatusCode);
    }


    // EF InMemory không áp default của DB nên không bắt được lỗi này khi chạy thật với SQL Server:
    // cột Status mà có default Approved (1) thì Pending (0) bị bỏ khỏi INSERT và bài lên thẳng Approved.
    [Fact]
    public async Task Status_column_has_no_database_default()
    {
        var hasDefault = await factory.WithDb(db =>
            Task.FromResult(db.Model.FindEntityType(typeof(Recipe))!.FindProperty(nameof(Recipe.Status))!
                .FindAnnotation("Relational:DefaultValue") != null));
        Assert.False(hasDefault);
    }

    [Fact]
    public async Task Login_token_carries_role_and_me_returns_it()
    {
        var admin = await factory.CreateUserAsync("vaitro@test.vn", role: Roles.Admin);
        var me = await factory.ClientFor(admin).GetFromJsonAsync<JsonElement>("/api/users/me");
        Assert.Equal(Roles.Admin, me.GetProperty("role").GetString());
    }
}
