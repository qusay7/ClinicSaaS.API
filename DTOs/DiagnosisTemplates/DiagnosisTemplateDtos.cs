namespace ClinicSaaS.API.DTOs.DiagnosisTemplates
{
    public class DiagnosisMedicationDto
    {
        public string DrugName { get; set; } = "";
        public string? Instructions { get; set; }
    }

    public class CreateDiagnosisTemplateDto
    {
        public string Name { get; set; } = "";
        public List<DiagnosisMedicationDto> Medications { get; set; } = new();
    }
}
