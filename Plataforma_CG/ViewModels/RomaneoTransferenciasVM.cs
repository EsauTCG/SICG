using Plataforma_CG.Models;

namespace Plataforma_CG.ViewModels
{
    public class RomaneoTransferenciasVM
    {
        public DateTime? Desde { get; set; }
        public DateTime? Hasta { get; set; }
        public string Destino { get; set; }
        public string Pedido { get; set; }

        public string CodigoEtiqueta { get; set; } = "";

        public List<string> Destinos { get; set; } = new();
        public List<string> Pedidos { get; set; } = new();  
   
        public List<string> Tarimas { get; set; } = new();

        // ✅ Selecciones MULTI
        public List<string> DestinosSeleccionados { get; set; } = new();
        public List<string> PedidosSeleccionados { get; set; } = new();
        public List<string> TarimasSeleccionadas { get; set; } = new();


        public List<RomaneoTransferenciasRowVM> Rows { get; set; } = new();

        public List<TransferenciaScanEtiquetaDetalleVM> DetalleEtiquetas { get; set; } = new();



    }

    public class RomaneoTransferenciasRowVM
    {

        public int TransferenciaId { get; set; }
        public string Pedido { get; set; }
        public string Destino { get; set; }
        public string ProductoCodigo { get; set; }
        public string ProductoNombre { get; set; }
        public int Cajas { get; set; }
        public decimal Peso { get; set; }
        public string Tarima { get; set; }
        public DateTime Fecha { get; set; }
    }

}
