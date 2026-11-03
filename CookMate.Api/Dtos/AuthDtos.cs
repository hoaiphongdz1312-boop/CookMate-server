using System.ComponentModel.DataAnnotations;

namespace CookMate.Api.Dtos;

public record RegisterRequest(
    [Required(ErrorMessage = "Thiếu email")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    [MaxLength(256, ErrorMessage = "Email quá dài")]
    string Email,

    [Required(ErrorMessage = "Thiếu mật khẩu")]
    [MinLength(8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự")]
    [MaxLength(100, ErrorMessage = "Mật khẩu quá dài")]
    string Password,

    [Required(ErrorMessage = "Thiếu họ tên")]
    [MaxLength(100, ErrorMessage = "Họ tên quá dài")]
    string FullName);

public record LoginRequest(
    [Required(ErrorMessage = "Thiếu email")] string Email,
    [Required(ErrorMessage = "Thiếu mật khẩu")] string Password);

public record UserDto(int Id, string Email, string FullName, bool HasFace, string Role, List<string>? Tastes = null);

public record GoogleLoginRequest(
    [Required(ErrorMessage = "Thiếu idToken")] string IdToken);

public record AuthResponse(string Token, UserDto User);

public record RegisterResponse(string Email, bool VerificationRequired);

public record VerifyEmailRequest(
    [Required(ErrorMessage = "Thiếu email")] string Email,
    [Required(ErrorMessage = "Vui lòng nhập mã")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Mã gồm 6 chữ số")]
    string Code);

public record EmailOnlyRequest(
    [Required(ErrorMessage = "Thiếu email")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    string Email);

public record ResetPasswordRequest(
    [Required(ErrorMessage = "Thiếu email")] string Email,
    [Required(ErrorMessage = "Vui lòng nhập mã")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Mã gồm 6 chữ số")]
    string Code,
    [Required(ErrorMessage = "Thiếu mật khẩu mới")]
    [MinLength(8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự")]
    [MaxLength(100, ErrorMessage = "Mật khẩu quá dài")]
    string NewPassword);
