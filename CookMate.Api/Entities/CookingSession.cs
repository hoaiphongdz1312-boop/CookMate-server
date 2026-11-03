namespace CookMate.Api.Entities;

public enum CookingStatus
{
    InProgress,
    Completed
}

public class CookingSession
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    public int Servings { get; set; }
    public int CurrentStep { get; set; }

    // Lưu dạng chuỗi "InProgress"/"Completed"
    public CookingStatus Status { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
