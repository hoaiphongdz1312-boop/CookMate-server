using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CookMate.Api.Entities;
using CookMate.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CookMate.Api.Tests;

public class GoogleLoginTests(GoogleLoginTests.GoogleFactory factory) : IClassFixture<GoogleLoginTests.GoogleFactory>
{
    // Verifier giả: idToken là khóa tra trong bảng Tokens, không có thì coi như token sai
    public class FakeGoogleVerifier : IGoogleTokenVerifier
    {
        public Dictionary<string, GoogleIdentity> Tokens { get; } = [];

        public Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken ct = default) =>
            Task.FromResult(Tokens.GetValueOrDefault(idToken));
    }

    public class GoogleFactory : ApiFactory
    {
        public FakeGoogleVerifier Google { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Google:ClientId", "test-client-id.apps.googleusercontent.com");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGoogleTokenVerifier>();
                services.AddSingleton<IGoogleTokenVerifier>(Google);
            });
        }
    }

    private static Task<HttpResponseMessage> GoogleLogin(HttpClient c, string idToken) =>
        c.PostAsJsonAsync("/api/auth/google", new { idToken });

    private static async Task<string?> ErrorOf(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
    }

    private string AddToken(string token, string sub, string email, bool verified = true, string? name = "Tên Google")
    {
        factory.Google.Tokens[token] = new GoogleIdentity(sub, email, verified, name);
        return token;
    }

    [Fact]
    public async Task New_google_user_is_created_and_gets_token()
    {
        AddToken("t-new", "sub-new", "Moi.Google@Test.vn");

        var res = await GoogleLogin(factory.CreateClient(), "t-new");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("token").GetString()));
        Assert.Equal("moi.google@test.vn", body.GetProperty("user").GetProperty("email").GetString());

        var user = await factory.WithDb(db => db.Users.SingleAsync(u => u.Email == "moi.google@test.vn"));
        Assert.Equal("sub-new", user.GoogleId);
        Assert.True(user.EmailConfirmed);
        Assert.Equal("", user.PasswordHash);
        Assert.Equal("Tên Google", user.FullName);

        // Đăng nhập lần hai vào đúng tài khoản cũ, không tạo thêm
        Assert.Equal(HttpStatusCode.OK, (await GoogleLogin(factory.CreateClient(), "t-new")).StatusCode);
        Assert.Equal(1, await factory.WithDb(db => db.Users.CountAsync(u => u.GoogleId == "sub-new")));
    }

    [Fact]
    public async Task New_google_user_without_name_uses_email_prefix()
    {
        AddToken("t-noname", "sub-noname", "khongten@test.vn", name: null);

        Assert.Equal(HttpStatusCode.OK, (await GoogleLogin(factory.CreateClient(), "t-noname")).StatusCode);

        var user = await factory.WithDb(db => db.Users.SingleAsync(u => u.GoogleId == "sub-noname"));
        Assert.Equal("khongten", user.FullName);
    }

    [Fact]
    public async Task Existing_verified_user_is_linked_and_keeps_password()
    {
        var existing = await factory.CreateUserAsync("cu@test.vn");
        AddToken("t-cu", "sub-cu", "cu@test.vn");

        var res = await GoogleLogin(factory.CreateClient(), "t-cu");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(existing.Id, body.GetProperty("user").GetProperty("id").GetInt32());

        var user = await factory.WithDb(db => db.Users.SingleAsync(u => u.Id == existing.Id));
        Assert.Equal("sub-cu", user.GoogleId);
        Assert.Equal(existing.PasswordHash, user.PasswordHash);

        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = "cu@test.vn", password = ApiFactory.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Existing_unverified_user_gets_password_cleared_when_linked()
    {
        // Kẻ tấn công đăng ký email của nạn nhân với mật khẩu của mình rồi bỏ đó, chưa xác thực
        var c = factory.CreateClient();
        var reg = await c.PostAsJsonAsync("/api/auth/register",
            new { email = "nannhan@test.vn", password = ApiFactory.Password, fullName = "Kẻ Gian" });
        Assert.Equal(HttpStatusCode.Created, reg.StatusCode);
        AddToken("t-victim", "sub-victim", "nannhan@test.vn");

        var res = await GoogleLogin(c, "t-victim");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var user = await factory.WithDb(db => db.Users.SingleAsync(u => u.Email == "nannhan@test.vn"));
        Assert.Equal("sub-victim", user.GoogleId);
        Assert.True(user.EmailConfirmed);
        Assert.Equal("", user.PasswordHash);

        // Mật khẩu cũ của kẻ tấn công không còn dùng được
        var login = await c.PostAsJsonAsync("/api/auth/login",
            new { email = "nannhan@test.vn", password = ApiFactory.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Invalid_token_returns_401()
    {
        var res = await GoogleLogin(factory.CreateClient(), "token-sai");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("Đăng nhập Google thất bại", await ErrorOf(res));
    }

    [Fact]
    public async Task Unverified_google_email_returns_403_and_creates_nothing()
    {
        AddToken("t-unverified", "sub-unverified", "chuaxt@test.vn", verified: false);

        var res = await GoogleLogin(factory.CreateClient(), "t-unverified");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.False(await factory.WithDb(db => db.Users.AnyAsync(u => u.Email == "chuaxt@test.vn")));
    }

    [Fact]
    public async Task Google_only_user_cannot_password_login_and_does_not_throw()
    {
        AddToken("t-only", "sub-only", "chigoogle@test.vn");
        Assert.Equal(HttpStatusCode.OK, (await GoogleLogin(factory.CreateClient(), "t-only")).StatusCode);

        foreach (var password in new[] { ApiFactory.Password, "x" })
        {
            var res = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
                new { email = "chigoogle@test.vn", password });
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        }
    }

    [Fact]
    public async Task Missing_client_id_returns_503()
    {
        using var unconfigured = new ApiFactory();

        var res = await GoogleLogin(unconfigured.CreateClient(), "bat-ky");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }
}
