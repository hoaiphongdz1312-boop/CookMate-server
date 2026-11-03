using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CookMate.Api.Dtos;
using CookMate.Api.Services;

namespace CookMate.Api.Tests;

public class TasteTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static RecipeSummaryDto R(int id, string name, params string[] tastes) =>
        new(id, name, "d", null, 1, 5, 5, 1, tastes.ToList());

    [Fact]
    public void Rank_puts_more_matching_tags_first_and_drops_non_matching()
    {
        var recipes = new[] { R(1, "B", "cay"), R(2, "A", "cay", "chua"), R(3, "C", "ngọt") };

        var ranked = TasteRanking.Rank(recipes, ["cay", "chua"]);

        Assert.Equal([2, 1], ranked.Select(r => r.Id));
    }

    [Fact]
    public void Rank_without_user_tastes_returns_plain_list_by_name()
    {
        var recipes = new[] { R(1, "B", "cay"), R(2, "A", "ngọt") };

        Assert.Equal([2, 1], TasteRanking.Rank(recipes, []).Select(r => r.Id));
    }

    [Fact]
    public async Task Set_tastes_saves_and_recommended_orders_by_match()
    {
        var user = await factory.CreateUserAsync("khauvi@test.vn");
        var c = factory.ClientFor(user);

        var me = await c.GetFromJsonAsync<JsonElement>("/api/users/me");
        Assert.Equal(JsonValueKind.Null, me.GetProperty("tastes").ValueKind);

        var put = await c.PutAsJsonAsync("/api/users/me/tastes", new { tastes = new[] { "cay" } });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        me = await c.GetFromJsonAsync<JsonElement>("/api/users/me");
        Assert.Equal("cay", me.GetProperty("tastes")[0].GetString());

        var items = (await c.GetFromJsonAsync<JsonElement>("/api/recipes/recommended")).EnumerateArray().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.Contains("cay", i.GetProperty("tastes").EnumerateArray().Select(t => t.GetString())));
    }

    [Fact]
    public async Task Skipping_tastes_stores_empty_list_and_recommended_is_plain()
    {
        var user = await factory.CreateUserAsync("boqua@test.vn");
        var c = factory.ClientFor(user);

        await c.PutAsJsonAsync("/api/users/me/tastes", new { tastes = Array.Empty<string>() });
        var me = await c.GetFromJsonAsync<JsonElement>("/api/users/me");
        Assert.Equal(0, me.GetProperty("tastes").GetArrayLength());

        var plain = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/recipes/recommended");
        var mine = await c.GetFromJsonAsync<JsonElement>("/api/recipes/recommended");
        Assert.Equal(plain.GetArrayLength(), mine.GetArrayLength());
        Assert.True(plain.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Set_tastes_rejects_unknown_tag_and_anonymous()
    {
        var user = await factory.CreateUserAsync("sai@test.vn");
        var res = await factory.ClientFor(user).PutAsJsonAsync("/api/users/me/tastes", new { tastes = new[] { "mặn chát" } });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var anon = await factory.CreateClient().PutAsJsonAsync("/api/users/me/tastes", new { tastes = new[] { "cay" } });
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
    }
}
