// ViewModels/SolicitudTransferenciaViewModel.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.Rendering;

public class SolicitudTransferenciaViewModel
{
    public string Consecutivo { get; set; } // mostrado en la vista

    [Required] public string Sucursal { get; set; }
    [Required, DataType(DataType.Date)] public DateTime FechaSolicitud { get; set; } = DateTime.Today;
    public string Observacion { get; set; }

    // 🔽 Agrega esta línea
    public string Accion { get; set; }

    public List<SolicitudTransferenciaProductoVM> Productos { get; set; } = new();
    public IEnumerable<SelectListItem> SeriesDisponibles { get; set; } = new List<SelectListItem>();
}

public class SolicitudTransferenciaProductoVM
{
    public string ProductoCodigo { get; set; }
    public string ProductoNombre { get; set; }
    public decimal CantidadKg { get; set; }
    public string Nota { get; set; }
    public decimal Presupuesto { get; set; }
    public decimal Disponible { get; set; }    
    public decimal Cajas { get; set; }
}
