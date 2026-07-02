namespace Plataforma_CG.Models
{
    public class EntregaSapLog
    {
        public int Id { get; set; }
        public string Referencia { get; set; } = "";
        public string Source { get; set; } = "P1";

        // 1 = enviado, 0 = fallo
        public bool Estatus { get; set; }

        public string? Mensaje { get; set; }
        public int? DocEntry { get; set; }
        public int? DocNum { get; set; }

        public DateTime FechaIntento { get; set; } = DateTime.Now;
        public string? Usuario { get; set; }
    }
}
