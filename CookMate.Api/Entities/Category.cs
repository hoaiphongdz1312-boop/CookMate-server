namespace CookMate.Api.Entities;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    public List<Recipe> Recipes { get; set; } = [];
}
