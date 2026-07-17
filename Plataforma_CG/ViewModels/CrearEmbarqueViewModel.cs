using System;

namespace Plataforma_CG.ViewModels
{
    public class CrearEmbarqueViewModel
    {
        public int TotalOrdenes { get; set; }
        public int TotalTransferencias { get; set; }
    }

    public class OrdenDisponibleViewModel
    {
        public int Id { get; set; }
        public string Cliente { get; set; } = "";
        public string NombreCliente { get; set; } = "";
        public string Consecutivo { get; set; } = "";
        public string Ruta { get; set; } = "";
        public int Estatus { get; set; }
    }

    public class TransferenciaDisponibleViewModel
    {
        public int Id { get; set; }
        public string Sucursal { get; set; } = "";
        public string Consecutivo { get; set; } = "";
        public DateTime? FechaSolicitud { get; set; }
        public string FechaSolicitudTexto { get; set; } = "";
        public int Estatus { get; set; }
    }
}