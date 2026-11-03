using System.ComponentModel.DataAnnotations;

namespace CookMate.Api.Dtos;

// Số lượng và số chiều vector kiểm tra ở FaceMatchingService.Validate
public record FaceEnrollRequest(
    [Required(ErrorMessage = "Thiếu embeddings")] List<float[]> Embeddings);

public record FaceLoginRequest(
    [Required(ErrorMessage = "Thiếu embeddings")] List<float[]> Embeddings);
