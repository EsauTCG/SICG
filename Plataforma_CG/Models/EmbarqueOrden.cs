using Plataforma_CG.Models;
using System.ComponentModel.DataAnnotations.Schema;

[Table("EmbarqueOrdenes")]
public class EmbarqueOrden
{
    public int Id { get; set; }
    public int EmbarqueId { get; set; }
    public int OrdenId { get; set; }

    public Embarque Embarque { get; set; }
    public OrdenVenta Orden { get; set; }
}