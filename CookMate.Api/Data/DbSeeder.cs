using System.Text.Json;
using CookMate.Api.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CookMate.Api.Data;

// Đọc server/Data/Seed/*.json và nạp vào DB khi bảng còn trống.
// Thiếu file chính thì dùng bản mẫu trong Seed/sample/.
public static class DbSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static async Task SeedAsync(AppDbContext db, string seedDir, ILogger logger)
    {
        if (!await db.Recipes.AnyAsync())
        {
            var path = PickFile(seedDir, "recipes.json", logger);
            var recipes = await ReadAsync<List<RecipeSeed>>(path);
            await SeedRecipesAsync(db, recipes);
            logger.LogInformation("Đã seed {Count} công thức từ {Path}", recipes.Count, path);
        }
        else
        {
            var path = PickFile(seedDir, "recipes.json", logger);
            await SyncSystemRecipesAsync(db, await ReadAsync<List<RecipeSeed>>(path), logger);
        }

        logger.LogInformation(
            "Số dòng sau seed: Categories={Categories}, Recipes={Recipes}, RecipeIngredients={Ingredients}, RecipeSteps={Steps}, Users={Users}",
            await db.Categories.CountAsync(),
            await db.Recipes.CountAsync(),
            await db.RecipeIngredients.CountAsync(),
            await db.RecipeSteps.CountAsync(),
            await db.Users.CountAsync());
    }

    // Admin:Email (+ Admin:Password) đọc từ user-secrets. Email đã có tài khoản thì nâng thành Admin;
    // chưa có thì tạo tài khoản đã xác thực nếu có mật khẩu. Không có API nào tự nâng quyền.
    public static async Task EnsureAdminAsync(
        AppDbContext db, IPasswordHasher<User> hasher, string? email, string? password, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(email)) return;
        email = email.Trim();

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
        if (user == null)
        {
            if (string.IsNullOrEmpty(password) || password.Length < 8)
            {
                logger.LogWarning("Admin:Email chưa có tài khoản và thiếu Admin:Password (tối thiểu 8 ký tự), bỏ qua");
                return;
            }
            user = new User
            {
                Email = email,
                FullName = "Quản trị viên",
                EmailConfirmed = true,
                Role = Roles.Admin,
                CreatedAt = DateTime.UtcNow
            };
            user.PasswordHash = hasher.HashPassword(user, password);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            logger.LogInformation("Đã tạo tài khoản admin {Email}", email);
        }
        else if (user.Role != Roles.Admin)
        {
            user.Role = Roles.Admin;
            await db.SaveChangesAsync();
            logger.LogInformation("Đã nâng {Email} thành admin", email);
        }
    }

    private static string PickFile(string seedDir, string name, ILogger logger)
    {
        var main = Path.Combine(seedDir, name);
        if (File.Exists(main)) return main;

        var sample = Path.Combine(seedDir, "sample", name);
        if (!File.Exists(sample))
            throw new FileNotFoundException($"Không tìm thấy {name} trong {seedDir} hoặc {seedDir}/sample");

        logger.LogWarning("Chưa có {Main}, dùng dữ liệu mẫu {Sample}", main, sample);
        return sample;
    }

    private static async Task<T> ReadAsync<T>(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions)
               ?? throw new InvalidDataException($"File seed rỗng: {path}");
    }

    private static async Task SeedRecipesAsync(AppDbContext db, List<RecipeSeed> recipes)
    {
        // Dùng lại danh mục đã có (nếu có) theo tên
        var categories = await db.Categories.ToDictionaryAsync(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var r in recipes)
        {
            if (!categories.TryGetValue(r.Category.Trim(), out var category))
            {
                category = new Category { Name = r.Category.Trim() };
                categories[category.Name] = category;
                db.Categories.Add(category);
            }

            db.Recipes.Add(new Recipe
            {
                Name = r.Name,
                Description = r.Description,
                ImageUrl = r.ImageUrl,
                Difficulty = r.Difficulty,
                PrepMinutes = r.PrepMinutes,
                CookMinutes = r.CookMinutes,
                BaseServings = r.BaseServings,
                Tastes = TasteTags.Normalize(r.Tastes),
                Status = RecipeStatus.Approved,
                Category = category,
                Ingredients = r.Ingredients.Select(i => new RecipeIngredient
                {
                    Name = i.Name,
                    Quantity = i.Quantity,
                    Unit = i.Unit,
                    IsOptional = i.IsOptional
                }).ToList(),
                // StepNumber đánh theo thứ tự trong file, bắt đầu từ 1
                Steps = r.Steps.Select((s, idx) => new RecipeStep
                {
                    StepNumber = idx + 1,
                    Instruction = s.Instruction,
                    TimerSeconds = s.TimerSeconds
                }).ToList()
            });
        }

        await db.SaveChangesAsync();
    }

    // DB đã seed từ bản cũ: cập nhật ảnh, nhãn khẩu vị và các bước của công thức hệ thống (AuthorId null) theo file seed.
    // Khớp theo tên; bài cộng đồng không bị đụng tới. Chỉ ghi khi có khác biệt.
    private static async Task SyncSystemRecipesAsync(AppDbContext db, List<RecipeSeed> seeds, ILogger logger)
    {
        var byName = seeds.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        var recipes = await db.Recipes
            .Include(r => r.Steps)
            .Where(r => r.AuthorId == null && !r.IsDeleted)
            .ToListAsync();

        var changed = 0;
        foreach (var recipe in recipes)
        {
            if (!byName.TryGetValue(recipe.Name, out var seed)) continue;

            var dirty = false;
            if (recipe.ImageUrl != seed.ImageUrl)
            {
                recipe.ImageUrl = seed.ImageUrl;
                dirty = true;
            }

            var tastes = TasteTags.Normalize(seed.Tastes);
            if (recipe.Tastes != tastes)
            {
                recipe.Tastes = tastes;
                dirty = true;
            }

            var current = recipe.Steps.OrderBy(s => s.StepNumber).ToList();
            var same = current.Count == seed.Steps.Count
                && current.Select((s, i) => s.Instruction == seed.Steps[i].Instruction
                                            && s.TimerSeconds == seed.Steps[i].TimerSeconds).All(x => x);
            if (!same)
            {
                db.RecipeSteps.RemoveRange(recipe.Steps);
                recipe.Steps = seed.Steps.Select((s, idx) => new RecipeStep
                {
                    StepNumber = idx + 1,
                    Instruction = s.Instruction,
                    TimerSeconds = s.TimerSeconds
                }).ToList();
                dirty = true;
            }

            if (dirty) changed++;
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("Đã cập nhật ảnh/nhãn/bước của {Count} công thức hệ thống theo file seed", changed);
        }
    }

    private record RecipeSeed(
        string Name,
        string Description,
        string? ImageUrl,
        int Difficulty,
        int PrepMinutes,
        int CookMinutes,
        int BaseServings,
        string Category,
        List<string>? Tastes,
        List<IngredientSeed> Ingredients,
        List<StepSeed> Steps);

    private record IngredientSeed(string Name, decimal Quantity, string Unit, bool IsOptional);

    private record StepSeed(string Instruction, int TimerSeconds);
}
