namespace ClinicSaaS.API.Data
{
    // أي كيان يطبق هذا الـ Interface، بيتعبى تلقائياً بمعلومات "مين أنشأ" و"مين عدّل" و"متى"
    public interface IAuditable
    {
        Guid? CreatedBy { get; set; }
        Guid? UpdatedBy { get; set; }
        DateTime? UpdatedAt { get; set; }
    }
}