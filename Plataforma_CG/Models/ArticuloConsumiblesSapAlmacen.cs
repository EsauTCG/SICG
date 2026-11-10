using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    /// <summary>
    /// Existencias de un artículo de consumibles SAP por almacén.
    /// Se sincroniza desde ItemWarehouseInfoCollection de SAP.
    /// </summary>
    [Table("ArticuloConsumiblesSapAlmacen")]
    public class ArticuloConsumiblesSapAlmacen
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(100)]
        public string ProductoCodigo { get; set; } = "";

        [MaxLength(20)]
        public string WarehouseCode { get; set; } = "";

        public decimal? Stock { get; set; }
        public decimal? Comprometido { get; set; }
        public decimal? Pedido { get; set; }
        public decimal? Disponible { get; set; }

        public DateTime FechaModificacion { get; set; } = DateTime.Now;
    }
}
