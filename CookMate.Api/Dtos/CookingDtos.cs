using System.ComponentModel.DataAnnotations;

namespace CookMate.Api.Dtos;

public record CreateSessionRequest(
    int RecipeId,
    [Range(1, 20, ErrorMessage = "Khẩu phần phải từ 1 đến 20")] int Servings);

public record UpdateSessionRequest(int CurrentStep);

public record SessionDto(int Id, int CurrentStep, string Status);

public record InProgressDto(int SessionId, int RecipeId, string RecipeName, int CurrentStep, int TotalSteps);

public record HistoryItemDto(int SessionId, int RecipeId, string RecipeName, DateTime CompletedAt);

public record ProgressDto(
    int CompletedCount,
    List<InProgressDto> InProgress,
    List<HistoryItemDto> History);
