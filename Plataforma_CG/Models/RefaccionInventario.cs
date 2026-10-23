using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    [Table("Refaccion")]
    public class Refaccion
    {
        public int Id { get; set; }
        public string Sku { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public int? CategoriaId { get; set; }
        public int? UbicacionId { get; set; }
        public int Stock { get; set; }
        public int StockMinimo { get; set; }
        public int? StockMaximo { get; set; }
        public decimal Costo { get; set; }
        public string? CodigoEtiqueta { get; set; }
        public string? FotoUrl { get; set; }
        public bool Activo { get; set; } = true;
        public DateTime FechaCreacion { get; set; }
        public string? UsuarioCreacion { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public string? UsuarioModificacion { get; set; }

        public RefaccionCategoria? Categoria { get; set; }
        public RefaccionUbicacion? Ubicacion { get; set; }
    }

    [Table("RefaccionCategoria")]
    public class RefaccionCategoria
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public bool Activo { get; set; } = true;
        public DateTime FechaCreacion { get; set; }
        public string? UsuarioCreacion { get; set; }
    }

    [Table("RefaccionUbicacion")]
    public class RefaccionUbicacion
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public bool Activo { get; set; } = true;
        public DateTime FechaCreacion { get; set; }
        public string? UsuarioCreacion { get; set; }
    }

    [Table("RefaccionMaquina")]
    public class RefaccionMaquina
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public bool Activo { get; set; } = true;
        public DateTime FechaCreacion { get; set; }
        public string? UsuarioCreacion { get; set; }
    }

    [Table("RefaccionMovimiento")] 
    public class RefaccionMovimiento
    {
        public int Id { get; set; }
        public int RefaccionId { get; set; }
        public string Tipo { get; set; } = "";
        public int Cantidad { get; set; }
        public int StockResultante { get; set; }
        public string? Folio { get; set; }
        public string? Referencia { get; set; }
        public string? MaquinaDestino { get; set; }
        public string? Comentario { get; set; }
        public string? Usuario { get; set; }
        public DateTime Fecha { get; set; }

        public Refaccion? Refaccion { get; set; }
    }

    // ==========================
    // DTOs de petición (pantalla)
    // ==========================
    public class RefaccionGuardarRequest
    {
        public int Id { get; set; }
        public string? Sku { get; set; }
        public string? Desc { get; set; }
        public string? Cat { get; set; }
        public string? Loc { get; set; }
        public int Stock { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public decimal Costo { get; set; }
    }

    public class CatalogoGuardarRequest
    {
        public int Id { get; set; }
        public string? Nombre { get; set; }
    }

    public class CatalogoEliminarRequest
    {
        public int Id { get; set; }
    }

    public class MovimientoRequest
    {
        public int Id { get; set; }
        public string? Tipo { get; set; }        // ENTRADA | SALIDA | AJUSTE
        public int Cantidad { get; set; }        // con signo en ajuste
        public string? Referencia { get; set; }
        public string? Maquina { get; set; }
        public string? Comentario { get; set; }
    }

    public class SalidaPosLinea
    {
        public int Id { get; set; }
        public int Cantidad { get; set; }
        public string? Maquina { get; set; }   // máquina destino por línea (modo por artículo)
    }

    public class SalidaPosRequest
    {
        public string? Referencia { get; set; }
        public string? Maquina { get; set; }
        public string? Comentario { get; set; }
        public List<SalidaPosLinea> Lineas { get; set; } = new();
    }

    public class VistaModoRequest
    {
        public string? Vista { get; set; }   // grid | table
    }
}
