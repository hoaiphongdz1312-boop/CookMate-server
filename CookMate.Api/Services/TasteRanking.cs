using CookMate.Api.Dtos;

namespace CookMate.Api.Services;

public static class TasteRanking
{
    // Món trùng nhiều nhãn hơn xếp trước, bằng điểm thì xếp theo tên. Không có nhãn nào được chọn thì
    // trả danh sách thường theo tên; có nhãn thì chỉ giữ món trùng ít nhất một nhãn.
    public static List<RecipeSummaryDto> Rank(IEnumerable<RecipeSummaryDto> recipes, IReadOnlyCollection<string> userTastes)
    {
        if (userTastes.Count == 0)
            return recipes.OrderBy(r => r.Name).ThenBy(r => r.Id).ToList();

        return recipes
            .Select(r => (Recipe: r, Score: r.Tastes.Count(userTastes.Contains)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Recipe.Name)
            .ThenBy(x => x.Recipe.Id)
            .Select(x => x.Recipe)
            .ToList();
    }
}
