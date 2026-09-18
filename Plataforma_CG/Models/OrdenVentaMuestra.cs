using System;

namespace Plataforma_CG.Models
{
    public class OrdenVentaMuestra
    {
        public int Id { get; set; }
        public int OrdenVentaId { get; set; }
        public string SolicitudMuestraId { get; set; }
        public bool EsMuestra { get; set; }
        public DateTime FechaCreacion { get; set; }
    }
}
