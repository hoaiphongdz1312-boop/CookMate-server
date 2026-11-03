namespace CookMate.Api.Dtos;

public record CategoryDto(int Id, string Name);

public record RecipeSummaryDto(
    int Id,
    string Name,
    string Description,
    string? ImageUrl,
    int Difficulty,
    int PrepMinutes,
    int CookMinutes,
    int CategoryId,
    List<string> Tastes);

public record PagedResult<T>(List<T> Items, int Page, int PageSize, int Total);

public record IngredientDto(int Id, string Name, decimal Quantity, string Unit, bool IsOptional);

public record StepDto(int StepNumber, string Instruction, int TimerSeconds);

// AuthorId/AuthorName: null với công thức hệ thống.
// Status (0 chờ duyệt, 1 đã duyệt, 2 bị từ chối) và RejectReason chỉ có khi người xem là tác giả hoặc admin.
public record RecipeDetailDto(
    int Id,
    string Name,
    string Description,
    string? ImageUrl,
    int Difficulty,
    int PrepMinutes,
    int CookMinutes,
    int BaseServings,
    int CategoryId,
    List<IngredientDto> Ingredients,
    List<StepDto> Steps,
    List<string> Tastes,
    int? AuthorId = null,
    string? AuthorName = null,
    int? Status = null,
    string? RejectReason = null);

public record TastesRequest(List<string>? Tastes);
