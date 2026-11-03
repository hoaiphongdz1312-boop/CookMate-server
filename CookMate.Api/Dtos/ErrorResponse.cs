namespace CookMate.Api.Dtos;

// Mọi lỗi trả về đều theo dạng { "error": "..." } (KE_HOACH_v2 mục 5)
public record ErrorResponse(string Error);
