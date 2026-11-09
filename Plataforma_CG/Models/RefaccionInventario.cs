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
        public bool SolicitudStockActiva { get; set; }
        public DateTime? FechaSolicitudStock { get; set; }
        public string? UsuarioSolicitudStock { get; set; }

        // Planta a la que pertenece la refacción: TIF o P1.
        // Un mismo SKU puede existir una vez por planta.
        public string? Planta { get; set; }

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

    [Table("RefaccionSapDocumento")]
    public class RefaccionSapDocumento
    {
        public int Id { get; set; }
        public string TipoMovimiento { get; set; } = ""; // SALIDA | SOLICITUD_COMPRA | ENTRADA
        public string Folio { get; set; } = "";
        public string Estado { get; set; } = "PENDIENTE";
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaNecesaria { get; set; }
        public string Usuario { get; set; } = "";
        public string? Referencia { get; set; }
        public string? Comentario { get; set; }
        public int CantidadLineas { get; set; }
        public string JsonSap { get; set; } = "{}";
        public int? SapDocEntry { get; set; }
        public int? SapDocNum { get; set; }
        public DateTime? FechaEnvio { get; set; }
        public string? Error { get; set; }
        public string? RespuestaSap { get; set; }
        public int? SolicitudCompraId { get; set; }
        public int? OrdenCompraDocEntry { get; set; }
        public int? OrdenCompraDocNum { get; set; }
        public bool InventarioAplicado { get; set; }
        public string? JefeAreaUsuario { get; set; }
        public DateTime? JefeAreaFecha { get; set; }
        public string? JefeAreaComentario { get; set; }
        public string? ContaduriaUsuario { get; set; }
        public DateTime? ContaduriaFecha { get; set; }
        public string? ContaduriaComentario { get; set; }
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
        public string? Planta { get; set; }
    }

    // DTO de salida para Cargar (mantiene los nombres JSON que ya usa la vista)
    public class RefaccionCargaDto
    {
        public int id { get; set; }
        public string sku { get; set; } = "";
        public string desc { get; set; } = "";
        public string? unidadMedidaInventario { get; set; }
        public int? catId { get; set; }
        public string cat { get; set; } = "";
        public int? ubiId { get; set; }
        public string loc { get; set; } = "";
        public int stock { get; set; }
        public int min { get; set; }
        public int max { get; set; }
        public decimal costo { get; set; }
        public string? codigoEtiqueta { get; set; }
        public string? foto { get; set; }
        public bool solicitudStockActiva { get; set; }
        public DateTime? fechaSolicitudStock { get; set; }
        public string? usuarioSolicitudStock { get; set; }
        public string? planta { get; set; }
        public List<RefaccionAlmacenDto> almacenes { get; set; } = new();
    }

    public class RefaccionAlmacenDto
    {
        public string codigo { get; set; } = "";
        public decimal? stock { get; set; }
        public decimal? disponible { get; set; }
    }

    public class RefaccionSolicitudStockRequest
    {
        public int Id { get; set; }
        public bool Solicitada { get; set; }
    }

    public class RefaccionSapEnviarRequest
    {
        public string Folio { get; set; } = "";
    }

    public class RefaccionSapSolicitudCompraCrearRequest
    {
        public DateTime? FechaNecesaria { get; set; }
        public string? Comentario { get; set; }
        public List<RefaccionSapSolicitudCompraLineaRequest> Lineas { get; set; } = new();
    }

    public class RefaccionSapEnviarSolicitudRequest
    {
        public int Id { get; set; }
    }

    public class RefaccionSapDecisionSolicitudRequest
    {
        public int Id { get; set; }
        public bool Aprobar { get; set; }
        public string? Comentario { get; set; }
    }

    public class RefaccionSapSolicitudCompraLineaRequest
    {
        public int Id { get; set; }
        public decimal Cantidad { get; set; }
        public string? WarehouseCode { get; set; }
        public string? AccountCode { get; set; }
    }

    public class RefaccionSapRecepcionCrearRequest
    {
        public int SolicitudCompraId { get; set; }
        public int OrdenCompraDocEntry { get; set; }
        public string? Comentario { get; set; }
        public List<RefaccionSapRecepcionLineaRequest> Lineas { get; set; } = new();
    }

    public class RefaccionSapRecepcionLineaRequest
    {
        public int LineNum { get; set; }
        public int Cantidad { get; set; }
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
        public RefaccionSapSalidaDatos? Sap { get; set; }
    }

    public class SalidaPosLinea
    {
        public int Id { get; set; }
        public int Cantidad { get; set; }
        public string? Maquina { get; set; }   // máquina destino por línea (modo por artículo)
        public RefaccionSapSalidaDatos? Sap { get; set; }
    }

    public class SalidaPosRequest
    {
        public string? Referencia { get; set; }
        public string? Maquina { get; set; }
        public string? Comentario { get; set; }
        public RefaccionSapSalidaDatos? Sap { get; set; }
        public List<SalidaPosLinea> Lineas { get; set; } = new();
    }

    public class RefaccionSapEditarSalidaRequest
    {
        public string Folio { get; set; } = "";
        public string? Referencia { get; set; }
        public string? Comentario { get; set; }
        public List<SalidaPosLinea> Lineas { get; set; } = new();
    }

    public class RefaccionSapSalidaDatos
    {
        public string? WarehouseCode { get; set; }
        public string? AccountCode { get; set; }
        public string? CostingCode { get; set; }
        public string? CostingCode2 { get; set; }
        public string? CostingCode3 { get; set; }
        public string? CostingCode4 { get; set; }
        public string? CostingCode5 { get; set; }
    }

    public class VistaModoRequest
    {
        public string? Vista { get; set; }   // grid | table
    }
}
