using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CookMate.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace CookMate.Api.Tests;

public class RandomRecipeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<List<int>> AllSystemIds() => await factory.WithDb(db =>
        db.Recipes.Where(r => r.AuthorId == null && r.Status == RecipeStatus.Approved && !r.IsDeleted)
            .Select(r => r.Id).ToListAsync());

    [Fact]
    public async Task Random_returns_different_recipes_across_calls()
    {
        var c = factory.CreateClient();
        var seen = new HashSet<int>();
        for (var i = 0; i < 25; i++)
        {
            var r = await c.GetFromJsonAsync<JsonElement>("/api/recipes/random");
            seen.Add(r.GetProperty("id").GetInt32());
        }
        Assert.True(seen.Count > 1);
    }

    [Fact]
    public async Task Random_excludes_given_ids_until_none_left()
    {
        var c = factory.CreateClient();
        var ids = await AllSystemIds();
        var rest = ids.Skip(1).Select(i => $"excludeIds={i}");

        var r = await c.GetFromJsonAsync<JsonElement>("/api/recipes/random?" + string.Join('&', rest));
        Assert.Equal(ids[0], r.GetProperty("id").GetInt32());

        var all = await c.GetAsync("/api/recipes/random?" + string.Join('&', ids.Select(i => $"excludeIds={i}")));
        Assert.Equal(HttpStatusCode.NotFound, all.StatusCode);
        Assert.Contains("\"error\"", await all.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Random_respects_filters_and_validates_input()
    {
        var c = factory.CreateClient();
        var r = await c.GetFromJsonAsync<JsonElement>("/api/recipes/random?difficulty=1&maxMinutes=30");
        Assert.Equal(1, r.GetProperty("difficulty").GetInt32());
        Assert.True(r.GetProperty("prepMinutes").GetInt32() + r.GetProperty("cookMinutes").GetInt32() <= 30);

        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/recipes/random?maxMinutes=0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/recipes/random?categoryId=99999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/recipes/random?difficulty=9")).StatusCode);
    }

    [Fact]
    public async Task Random_skips_community_and_deleted_recipes()
    {
        var user = await factory.CreateUserAsync("quay@test.vn");
        await factory.WithDb(async db =>
        {
            var cat = await db.Categories.FirstAsync();
            db.Recipes.Add(new Recipe { Name = "Bài cộng đồng", Description = "d", Difficulty = 3, PrepMinutes = 1, CookMinutes = 1,
                BaseServings = 1, CategoryId = cat.Id, AuthorId = user.Id, Status = RecipeStatus.Approved });
            db.Recipes.Add(new Recipe { Name = "Món đã xóa", Description = "d", Difficulty = 3, PrepMinutes = 1, CookMinutes = 1,
                BaseServings = 1, CategoryId = cat.Id, IsDeleted = true });
            await db.SaveChangesAsync();
            return 0;
        });

        var res = await factory.CreateClient().GetAsync("/api/recipes/random?maxMinutes=2");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
