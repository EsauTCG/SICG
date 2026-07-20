using System;
using System.Collections.Generic;

namespace Plataforma_CG.Models
{
    // Clase principal que recibe la solicitud completa
    public class SolicitudMuestraVM
    {
        public string Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; }

        public string Seller { get; set; }
        public string Client { get; set; }
        public string Species { get; set; }
        public DateTime RequestedDate { get; set; }
        public string Route { get; set; }
        public string Destination { get; set; }
        public string Priority { get; set; }
        public string Notes { get; set; }
        public string Stage { get; set; }
        public string Location { get; set; }
        public int? OrdenVentaId { get; set; }

        public PlaneacionVM Planning { get; set; }
        public List<ItemMuestraVM> Items { get; set; } = new List<ItemMuestraVM>();
    }

    // Datos de planeacion de produccion
    public class PlaneacionVM
    {
        public DateTime ProcessDate { get; set; }
        public string Shift { get; set; }
        public string Line { get; set; }
        public string Planner { get; set; }
        public string Especificacion { get; set; }
        public DateTime ReleasedAt { get; set; }

    }

    // Articulos individuales dentro de la solicitud
    public class ItemMuestraVM
    {
        public string Uid { get; set; }
        public string Sku { get; set; }
        public string WorkSku { get; set; }
        public string Product { get; set; }
        public string Spec { get; set; }
        public int Boxes { get; set; }
        public string Temp { get; set; }

        public List<EtiquetaVM> Labels { get; set; } = new List<EtiquetaVM>();
    }

    // Etiquetas generadas en produccion
    public class EtiquetaVM
    {
        public string Code { get; set; }
        public string ExternalChain { get; set; }
        public string Operator { get; set; }
        public DateTime ProcessedAt { get; set; }
        public string Location { get; set; }
        public decimal? PesoReal { get; set; }
    }

    public class EditarSolicitudModel
    {
        public string Id { get; set; }
        public string Species { get; set; }
        public DateTime RequestedDate { get; set; }
        public string Route { get; set; }
        public string Destination { get; set; }
        public string Priority { get; set; }
        public string Notes { get; set; }
        public List<EditarItemSpecModel> Items { get; set; } = new List<EditarItemSpecModel>();
    }

    public class EditarItemSpecModel
    {
        public string Uid { get; set; }
        public string Spec { get; set; }
    }

}
