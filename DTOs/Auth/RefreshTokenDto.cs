namespace ClinicSaaS.API.DTOs.Auth
{
    // ما يُرسله المستخدم عند تجديد التوكن
    public class RefreshTokenDto
    {
        public string RefreshToken { get; set; } = default!;// التوكن القديم الذي سيتم تجديده
    }
}