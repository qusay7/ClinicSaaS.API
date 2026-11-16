namespace ClinicSaaS.API.DTOs.DiagnosisTemplates
{
    public class DiagnosisMedicationDto
    {
        public string DrugName { get; set; } = "";
        public string? Instructions { get; set; }
    }

    public class DiagnosisTestDto
    {
        public string TestName { get; set; } = "";
        public string Category { get; set; } = "lab"; // "lab" | "radiology"
    }

    public class CreateDiagnosisTemplateDto
    {
        public string Name { get; set; } = "";
        public List<DiagnosisMedicationDto> Medications { get; set; } = new();
        public List<DiagnosisTestDto> Tests { get; set; } = new();
    }
}
