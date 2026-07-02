namespace Plataforma_CG.Models
{
    public class DireccionCliente
    {
        // =========================
        // PK
        // =========================
        public int Id { get; set; }

        // =========================
        // Relación con cliente
        // =========================
        public string Cliente { get; set; } = "";   // CardCode SAP

        // =========================
        // Origen / Control
        // =========================
        public string Origen { get; set; } = "SAP"; // SAP | MANUAL
        public bool Activa { get; set; } = true;
        public DateTime FechaAlta { get; set; } = DateTime.Now;
        public DateTime? FechaActualizacion { get; set; }

        // =========================
        // Identidad de la dirección
        // =========================
        public string AliasDireccion { get; set; } = "";
        public bool EsPrincipal { get; set; } = false;

        // =========================
        // Logística
        // =========================
        public string Cedis { get; set; } = "";
        public string? Ruta { get; set; }

        // =========================
        // Dirección física
        // =========================
        public string Calle { get; set; } = "";
        public string? Colonia { get; set; }
        public string Ciudad { get; set; } = "";
        public string Estado { get; set; } = "";
        public string? CodigoPostal { get; set; }
        public string Pais { get; set; } = "MEXICO";

        // =========================
        // Campos SAP (opcional)
        // =========================
        public string? SapAddressType { get; set; }   // bo_ShipTo / bo_BillTo
        public int? SapRowNum { get; set; }
        public string? SapAddressCode { get; set; }
    }
}
