namespace Plataforma_CG.ViewModels
{
    public class TransferenciaListadoVM
    {

        public int Id { get; set; }
        public string Consecutivo { get; set; } = "";

        // 🔹 aquí usamos DateTime? porque la entidad es DateTime?
        public DateTime? FechaSolicitud { get; set; }
        public DateTime? FechaCreacion { get; set; }

        public string SucursalCodigo { get; set; } = "";
        public string SucursalNombre { get; set; } = "";

        public string Observacion { get; set; } = "";
        public decimal TotalKg { get; set; }

        public decimal TotalCajas { get; set; }

        // De momento no tienes campo Estatus en la entidad,
        // lo dejamos como int y lo rellenamos con un valor fijo (ej. 1 = Pendiente)
        public int Estatus { get; set; }

        public string UsuarioSolicita { get; set; }
    }
}
