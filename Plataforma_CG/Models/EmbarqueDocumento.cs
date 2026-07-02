using Plataforma_CG.Models;
using System.ComponentModel.DataAnnotations;

public class EmbarqueDocumento
{
    public int Id { get; set; }

    public int EmbarqueId { get; set; }
    public Embarque Embarque { get; set; }

    public int DocumentoId { get; set; }

    [Required]
    [StringLength(20)]
    public string TipoDocumento { get; set; } // "OV" o "TRANSFERENCIA"

    public DateTime FechaRegistro { get; set; } = DateTime.Now;
}
