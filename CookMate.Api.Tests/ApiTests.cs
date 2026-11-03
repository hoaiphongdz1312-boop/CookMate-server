using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using CookMate.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace CookMate.Api.Tests;

public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<string?> ErrorOf(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
    }

    private static Task<HttpResponseMessage> Login(HttpClient c, string email, string password) =>
        c.PostAsJsonAsync("/api/auth/login", new { email, password });

    // ---------- Auth ----------

    [Fact]
    public async Task Register_duplicate_verified_email_returns_409()
    {
        await factory.CreateUserAsync("trung@test.vn");
        var res = await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email = "trung@test.vn", password = ApiFactory.Password, fullName = "Ai Đó" });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.False(string.IsNullOrEmpty(await ErrorOf(res)));
    }

    [Fact]
    public async Task Register_verify_then_login_works()
    {
        var c = factory.CreateClient();
        var reg = await c.PostAsJsonAsync("/api/auth/register",
            new { email = "moi@test.vn", password = ApiFactory.Password, fullName = "Người Mới" });
        Assert.Equal(HttpStatusCode.Created, reg.StatusCode);

        // Chưa xác thực email thì chưa đăng nhập được
        Assert.Equal(HttpStatusCode.Forbidden, (await Login(c, "moi@test.vn", ApiFactory.Password)).StatusCode);

        var mail = factory.Email.Sent.Last(m => m.To == "moi@test.vn");
        var code = Regex.Match(mail.Body, @"\d{6}").Value;
        var verify = await c.PostAsJsonAsync("/api/auth/verify-email", new { email = "moi@test.vn", code });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Login(c, "moi@test.vn", ApiFactory.Password)).StatusCode);
    }

    [Fact]
    public async Task Login_wrong_password_returns_401_with_generic_message()
    {
        await factory.CreateUserAsync("sai@test.vn");
        var c = factory.CreateClient();

        var wrongPass = await Login(c, "sai@test.vn", "mat-khau-khac-1");
        var noUser = await Login(c, "khongco@test.vn", "mat-khau-khac-1");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPass.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, noUser.StatusCode);
        // Cùng một thông báo để không lộ email nào đã đăng ký
        Assert.Equal(await ErrorOf(wrongPass), await ErrorOf(noUser));
    }

    [Theory]
    [InlineData("/api/users/me")]
    [InlineData("/api/users/me/progress")]
    public async Task Protected_endpoints_without_token_return_401_json(string url)
    {
        var res = await factory.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.False(string.IsNullOrEmpty(await ErrorOf(res)));
    }

    [Fact]
    public async Task Invalid_body_returns_json_error()
    {
        var res = await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email = "khong-phai-email", password = "ngan", fullName = "X" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.False(string.IsNullOrEmpty(await ErrorOf(res)));
    }

    // ---------- Công thức ----------

    [Fact]
    public async Task Recipes_filter_by_difficulty_returns_only_that_difficulty()
    {
        await factory.WithDb(async db =>
        {
            if (!await db.Recipes.AnyAsync(r => r.Name == "Món khó kiểm thử"))
            {
                var cat = await db.Categories.FirstAsync();
                db.Recipes.Add(new Recipe
                {
                    Name = "Món khó kiểm thử", Description = "d", Difficulty = 3,
                    PrepMinutes = 5, CookMinutes = 5, BaseServings = 2, CategoryId = cat.Id
                });
                await db.SaveChangesAsync();
            }
            return 0;
        });
        var c = factory.CreateClient();

        var res = await c.GetFromJsonAsync<JsonElement>("/api/recipes?difficulty=3");
        var items = res.GetProperty("items").EnumerateArray().ToList();

        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.Equal(3, i.GetProperty("difficulty").GetInt32()));
        Assert.Equal(items.Count, res.GetProperty("total").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/recipes?difficulty=4")).StatusCode);
    }

    // ---------- Phiên nấu và tiến độ ----------

    [Fact]
    public async Task Patch_step_outside_range_returns_400()
    {
        var user = await factory.CreateUserAsync("buoc@test.vn");
        var c = factory.ClientFor(user);
        var (recipeId, steps) = await FirstRecipeAsync();
        var session = await StartSession(c, recipeId);

        foreach (var bad in new[] { 0, steps + 1, -3 })
        {
            var res = await c.PatchAsJsonAsync($"/api/cooking-sessions/{session}", new { currentStep = bad });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }
        var ok = await c.PatchAsJsonAsync($"/api/cooking-sessions/{session}", new { currentStep = steps });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Two_users_data_is_isolated()
    {
        var a = await factory.CreateUserAsync("a@test.vn");
        var b = await factory.CreateUserAsync("b@test.vn");
        var ca = factory.ClientFor(a);
        var cb = factory.ClientFor(b);
        var (recipeId, _) = await FirstRecipeAsync();
        var sessionA = await StartSession(ca, recipeId);

        // B không sửa/hoàn thành được phiên của A
        Assert.Equal(HttpStatusCode.NotFound,
            (await cb.PatchAsJsonAsync($"/api/cooking-sessions/{sessionA}", new { currentStep = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await cb.PostAsync($"/api/cooking-sessions/{sessionA}/complete", null)).StatusCode);

        // B không thấy tiến độ của A, A thấy phiên của mình
        var progB = await cb.GetFromJsonAsync<JsonElement>("/api/users/me/progress");
        Assert.Empty(progB.GetProperty("inProgress").EnumerateArray());
        Assert.Equal(0, progB.GetProperty("completedCount").GetInt32());

        var progA = await ca.GetFromJsonAsync<JsonElement>("/api/users/me/progress");
        Assert.Single(progA.GetProperty("inProgress").EnumerateArray());
    }

    [Fact]
    public async Task Recipe_list_and_detail_return_taste_tags()
    {
        var id = await factory.WithDb(async db =>
        {
            var r = await db.Recipes.FirstOrDefaultAsync(r => r.Name == "Món vị kiểm thử");
            if (r == null)
            {
                var cat = await db.Categories.FirstAsync();
                r = new Recipe
                {
                    Name = "Món vị kiểm thử", Description = "d", Difficulty = 1,
                    PrepMinutes = 5, CookMinutes = 5, BaseServings = 2, CategoryId = cat.Id,
                    Tastes = TasteTags.Normalize(["béo", "cay", "lạ"])
                };
                db.Recipes.Add(r);
                await db.SaveChangesAsync();
            }
            return r.Id;
        });
        var c = factory.CreateClient();

        var list = await c.GetFromJsonAsync<JsonElement>("/api/recipes?search=Món vị kiểm thử");
        var tags = list.GetProperty("items")[0].GetProperty("tastes").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Equal(["cay", "béo"], tags);

        var detail = await c.GetFromJsonAsync<JsonElement>($"/api/recipes/{id}");
        Assert.Equal(2, detail.GetProperty("tastes").GetArrayLength());
    }

    // ---------- Lỗi chung ----------

    [Fact]
    public async Task Missing_recipe_returns_json_without_stack_trace()
    {
        var res = await factory.CreateClient().GetAsync("/api/recipes/999999");
        var body = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Contains("\"error\"", body);
        Assert.DoesNotContain("   at ", body);
    }

    private async Task<(int RecipeId, int Steps)> FirstRecipeAsync()
    {
        return await factory.WithDb(async db =>
        {
            var r = await db.Recipes.OrderBy(x => x.Id).Select(x => new { x.Id, Steps = x.Steps.Count }).FirstAsync();
            return (r.Id, r.Steps);
        });
    }

    private static async Task<int> StartSession(HttpClient c, int recipeId)
    {
        var res = await c.PostAsJsonAsync("/api/cooking-sessions", new { recipeId, servings = 2 });
        Assert.True(res.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }
}

// Factory riêng (giới hạn mặc định 10/phút) để không ảnh hưởng các test khác
public class RateLimitTests(RateLimitTests.Limited factory) : IClassFixture<RateLimitTests.Limited>
{
    public class Limited : ApiFactory
    {
        public Limited() : base(10) { }
    }

    [Fact]
    public async Task Eleventh_login_in_a_minute_returns_429()
    {
        var c = factory.CreateClient();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++)
            codes.Add((await c.PostAsJsonAsync("/api/auth/login",
                new { email = "x@test.vn", password = "mat-khau-sai-1" })).StatusCode);

        Assert.All(codes.Take(10), s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, codes[10]);
    }
}
