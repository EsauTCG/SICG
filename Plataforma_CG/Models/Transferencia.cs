// Models/Transferencia.cs
using Plataforma_CG.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public class Transferencia
{
    [Key]
    public int Id { get; set; } // IDENTITY(1,1) en SQL

    [Required, StringLength(20)]
    public string Consecutivo { get; set; } // TRANSF-0000001 (único)

    [Required, StringLength(50)]
    public string Sucursal { get; set; }

    public DateTime? FechaSolicitud { get; set; }

    [StringLength(500)]
    public string Observacion { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.Now;

    public int Estatus { get; set; }

    [StringLength(100)]
    public string UsuarioSolicita { get; set; } = "";

    // si ya tienes detalle, lo dejas aquí
    public List<TransferenciaDetalle> Detalles { get; set; } = new();
}
