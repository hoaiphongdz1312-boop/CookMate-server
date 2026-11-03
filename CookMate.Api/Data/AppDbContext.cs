using CookMate.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace CookMate.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<RecipeStep> RecipeSteps => Set<RecipeStep>();
    public DbSet<CookingSession> CookingSessions => Set<CookingSession>();
    public DbSet<EmailCode> EmailCodes => Set<EmailCode>();
    public DbSet<RecipeReport> RecipeReports => Set<RecipeReport>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(100).IsRequired();
            e.Property(x => x.GoogleId).HasMaxLength(64);
            e.HasIndex(x => x.GoogleId).IsUnique().HasFilter("[GoogleId] IS NOT NULL");
            e.Property(x => x.FaceEmbedding).HasColumnType("varbinary(max)").IsRequired(false);
            e.Property(x => x.Role).HasMaxLength(20).IsRequired().HasDefaultValue(Roles.User);
            e.Property(x => x.Tastes).HasMaxLength(100);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        b.Entity<Category>(e =>
        {
            e.ToTable("Categories");
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<Recipe>(e =>
        {
            e.ToTable("Recipes", t =>
            {
                t.HasCheckConstraint("CK_Recipes_Difficulty", "[Difficulty] BETWEEN 1 AND 3");
                t.HasCheckConstraint("CK_Recipes_BaseServings", "[BaseServings] >= 1");
                t.HasCheckConstraint("CK_Recipes_Minutes", "[PrepMinutes] >= 0 AND [CookMinutes] >= 0");
                t.HasCheckConstraint("CK_Recipes_Status", "[Status] BETWEEN 0 AND 2");
            });
            // Không đặt default ở DB: default Approved khiến EF bỏ cột khi INSERT Pending (0) và bài lên thẳng Approved.
            // Là concurrency token: UPDATE kèm "WHERE Status = giá trị đã đọc", nên hai admin xử lý chồng
            // nhau thì người sau nhận DbUpdateConcurrencyException (409)
            e.Property(x => x.Status).IsConcurrencyToken();
            e.Property(x => x.RejectReason).HasMaxLength(500);
            e.HasIndex(x => new { x.Status, x.SubmittedAt });
            e.HasIndex(x => new { x.AuthorId, x.Status });
            e.HasOne(x => x.Author)
                .WithMany()
                .HasForeignKey(x => x.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ReviewedBy)
                .WithMany()
                .HasForeignKey(x => x.ReviewedById)
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000).IsRequired();
            e.Property(x => x.ImageUrl).HasMaxLength(500);
            e.Property(x => x.Tastes).HasMaxLength(100).IsRequired().HasDefaultValue("");
            e.HasIndex(x => x.Name);
            e.HasOne(x => x.Category)
                .WithMany(c => c.Recipes)
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RecipeIngredient>(e =>
        {
            e.ToTable("RecipeIngredients", t =>
                t.HasCheckConstraint("CK_RecipeIngredients_Quantity", "[Quantity] >= 0"));
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(10, 2);
            e.Property(x => x.Unit).HasMaxLength(30).IsRequired();
            e.HasOne(x => x.Recipe)
                .WithMany(r => r.Ingredients)
                .HasForeignKey(x => x.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RecipeStep>(e =>
        {
            e.ToTable("RecipeSteps", t =>
            {
                t.HasCheckConstraint("CK_RecipeSteps_StepNumber", "[StepNumber] >= 1");
                t.HasCheckConstraint("CK_RecipeSteps_TimerSeconds", "[TimerSeconds] >= 0");
            });
            e.Property(x => x.Instruction).HasMaxLength(2000).IsRequired();
            e.HasIndex(x => new { x.RecipeId, x.StepNumber }).IsUnique();
            e.HasOne(x => x.Recipe)
                .WithMany(r => r.Steps)
                .HasForeignKey(x => x.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CookingSession>(e =>
        {
            e.ToTable("CookingSessions", t =>
            {
                t.HasCheckConstraint("CK_CookingSessions_Servings", "[Servings] BETWEEN 1 AND 20");
                t.HasCheckConstraint("CK_CookingSessions_CurrentStep", "[CurrentStep] >= 1");
            });
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.UserId, x.Status });
            e.HasOne(x => x.User)
                .WithMany(u => u.CookingSessions)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Recipe)
                .WithMany()
                .HasForeignKey(x => x.RecipeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<EmailCode>(e =>
        {
            e.ToTable("EmailCodes");
            e.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CodeHash).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.UserId, x.Purpose });
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RecipeReport>(e =>
        {
            e.ToTable("RecipeReports", t =>
                t.HasCheckConstraint("CK_RecipeReports_Status", "[Status] BETWEEN 0 AND 1"));
            e.Property(x => x.Reason).HasMaxLength(20).IsRequired();
            e.Property(x => x.Detail).HasMaxLength(500);
            e.Property(x => x.Resolution).HasMaxLength(20);
            // Mỗi người báo cáo một bài một lần
            e.HasIndex(x => new { x.RecipeId, x.ReporterId }).IsUnique();
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasOne(x => x.Recipe)
                .WithMany()
                .HasForeignKey(x => x.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Reporter)
                .WithMany()
                .HasForeignKey(x => x.ReporterId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ResolvedBy)
                .WithMany()
                .HasForeignKey(x => x.ResolvedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<UserBlock>(e =>
        {
            e.ToTable("UserBlocks", t =>
                t.HasCheckConstraint("CK_UserBlocks_NotSelf", "[BlockerId] <> [BlockedId]"));
            e.HasKey(x => new { x.BlockerId, x.BlockedId });
            e.HasIndex(x => x.BlockedId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.Blocker)
                .WithMany()
                .HasForeignKey(x => x.BlockerId)
                .OnDelete(DeleteBehavior.Cascade);
            // SQL Server không cho hai đường cascade từ Users vào cùng một bảng
            e.HasOne(x => x.Blocked)
                .WithMany()
                .HasForeignKey(x => x.BlockedId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Notification>(e =>
        {
            e.ToTable("Notifications");
            e.Property(x => x.Type).HasMaxLength(30).IsRequired();
            e.Property(x => x.Title).HasMaxLength(100).IsRequired();
            e.Property(x => x.Body).HasMaxLength(800).IsRequired();
            e.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt });
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
