namespace CookMate.Api.Entities;

public class RecipeStep
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    public int StepNumber { get; set; }
    public string Instruction { get; set; } = "";

    // 0 nếu bước không cần hẹn giờ
    public int TimerSeconds { get; set; }
}
