namespace Plataforma_CG.ViewModels
{
    public class TransferenciaCargaVM
    {
        public int TransferenciaId { get; set; }
        public string Consecutivo { get; set; }
        public string AlmacenOrigen { get; set; }
        public string AlmacenDestino { get; set; }
        public string UsuarioSolicita { get; set; }
        public DateTime? FechaSolicitud { get; set; }
        public string Observacion { get; set; }
        public List<TransferenciaCargaItemVM> Items { get; set; } = new();


        // ✅ NUEVO
        public List<TransferenciaEtiquetaVM> Etiquetas { get; set; } = new();
    }

    public class TransferenciaCargaItemVM
    {
        public string Sku { get; set; }
        public string Producto { get; set; }

        public decimal Pedido { get; set; }    // Kg pedido
        public decimal Surtido { get; set; }   // Kg surtido

        public int CajasPedido { get; set; }   // Cajas pedidas
        public int CajasSurtido { get; set; }  // Cajas surtidas
    }

}
