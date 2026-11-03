using CookMate.Api.Data;
using CookMate.Api.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CookMate.Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<List<CategoryDto>> GetAll() =>
        await db.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name))
            .ToListAsync();
}
