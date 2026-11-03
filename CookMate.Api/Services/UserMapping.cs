using CookMate.Api.Dtos;
using CookMate.Api.Entities;

namespace CookMate.Api.Services;

public static class UserMapping
{
    public static UserDto ToDto(this User u) => new(u.Id, u.Email, u.FullName, u.FaceEmbedding != null, u.Role,
        u.Tastes == null ? null : TasteTags.Parse(u.Tastes));
}
