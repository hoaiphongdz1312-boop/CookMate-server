namespace CookMate.Api.Entities;

public class RecipeIngredient
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    public string Name { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "";
    public bool IsOptional { get; set; }
}
