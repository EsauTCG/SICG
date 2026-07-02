using System.ComponentModel.DataAnnotations;

namespace Plataforma_CG.Models
{
    public class ProspectoModel
    {

        public int Id { get; set; }
        public string NombreComercial { get; set; }
        public string PersonaAtendio { get; set; }
        public string PerfilPersonal { get; set; } // Empleado, Gerente, Dueño
        public string Ubicacion { get; set; }
        public string TipoCanal { get; set; } // Abarrotes, Carnicería, Autoservicio
        public string TipoProducto { get; set; } // Res, Pollo, Cerdo, Abarrotes
        public string RutaFotoFachada { get; set; }
        public IFormFile FotoFachada { get; set; }
        public string Usuario { get; set; }
        public string ListaPrecios { get; set; }
        public IFormFile ArcListaPrecios { get; set; }
        public string TopTenPrecios { get; set; }
        public int PrecioBajoLista { get; set; } = 0;
        public int Credito { get; set; } = 0;
        public string MetodoPago { get; set; } // Transferencia, Efectivo
        public string OtrasMarcas { get; set; }
        public int FacilitaRebanado { get; set; } = 0;
        public string VolumenCompra { get; set; }
        public string PeriodicidadCompra { get; set; }
        public string AudioPath { get; set; }
        public IFormFile Audio { get; set; }
        public int CantidadVisitas { get; set; }
        public int NumeroTiendas { get; set; }
        public DateTime FechaHora { get; set; } = DateTime.Now;
        public DateTime UltimaVisita { get; set; }

    }
}
