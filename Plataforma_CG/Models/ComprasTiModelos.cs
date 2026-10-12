using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Http;

namespace Plataforma_CG.Models;

[Table("CompraTiSolicitud")]
public class CompraTiSolicitud
{
    public int Id { get; set; }
    [MaxLength(30)] public string Folio { get; set; } = "";
    [MaxLength(100)] public string SolicitudSap { get; set; } = "";
    public int? SolicitudSapDocEntry { get; set; }
    public DateTime? SolicitudSapFecha { get; set; }
    [MaxLength(30)] public string SolicitudSapEstatus { get; set; } = "";
    [MaxLength(150)] public string SolicitudSapSolicitante { get; set; } = "";
    public string SolicitudSapSnapshotJson { get; set; } = "";
    [MaxLength(30)] public string TipoCompra { get; set; } = "";
    [MaxLength(250)] public string Titulo { get; set; } = "";
    public string Justificacion { get; set; } = "";
    [MaxLength(100)] public string CentroCosto { get; set; } = "";
    [MaxLength(100)] public string Planta { get; set; } = "";
    [MaxLength(150)] public string Solicitante { get; set; } = "";
    [MaxLength(50)] public string? ProveedorSapCodigo { get; set; }
    [MaxLength(250)] public string ProveedorNombreSnapshot { get; set; } = "";
    [MaxLength(20)] public string ProveedorRfcSnapshot { get; set; } = "";
    [MaxLength(10)] public string Moneda { get; set; } = "MXN";
    [MaxLength(40)] public string Estatus { get; set; } = "BORRADOR";
    public decimal SubtotalCotizado { get; set; }
    public decimal IvaCotizado { get; set; }
    public decimal TotalCotizado { get; set; }
    public decimal SubtotalFactura { get; set; }
    public decimal IvaFactura { get; set; }
    public decimal TotalFactura { get; set; }
    public decimal DiferenciaFacturaCotizacion { get; set; }
    public bool Autorizada { get; set; }
    public bool RecibidaConforme { get; set; }
    public bool ConciliacionOk { get; set; }
    public bool LiberadaPago { get; set; }
    public DateTime? FechaLiberacionPago { get; set; }
    [MaxLength(150)] public string LiberadoPagoPor { get; set; } = "";
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaModificacion { get; set; }
    [MaxLength(150)] public string CreadoPor { get; set; } = "";
    [MaxLength(150)] public string ModificadoPor { get; set; } = "";
    [Timestamp] public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

[Table("CompraTiDetalle")]
public class CompraTiDetalle
{
    public int Id { get; set; }
    public int SolicitudId { get; set; }
    public int? LineaSap { get; set; }
    [MaxLength(100)] public string? ArticuloSapCodigo { get; set; }
    [MaxLength(30)] public string TipoLinea { get; set; } = "ARTICULO";
    [MaxLength(500)] public string Descripcion { get; set; } = "";
    public decimal CantidadSolicitada { get; set; }
    public decimal CantidadRecibida { get; set; }
    [MaxLength(30)] public string Unidad { get; set; } = "PZA";
    [MaxLength(100)] public string? CentroCostoSap { get; set; }
    [MaxLength(50)] public string? AlmacenSap { get; set; }
    [MaxLength(50)] public string? ProveedorPreferidoSap { get; set; }
    public decimal PrecioUnitarioCotizado { get; set; }
    public decimal PrecioUnitarioFacturado { get; set; }
    public bool Activo { get; set; } = true;
}


[Table("CompraTiOrdenCompraSap")]
public class CompraTiOrdenCompraSap
{
    public int Id { get; set; }
    public int SolicitudId { get; set; }

    public int DocEntry { get; set; }
    public int DocNum { get; set; }

    public DateTime? FechaDocumento { get; set; }
    public DateTime? FechaEntrega { get; set; }

    [MaxLength(30)]
    public string Estado { get; set; } = "";

    public bool Cancelada { get; set; }

    [MaxLength(50)]
    public string ProveedorCodigo { get; set; } = "";

    [MaxLength(250)]
    public string ProveedorNombre { get; set; } = "";

    [MaxLength(10)]
    public string Moneda { get; set; } = "MXN";

    public decimal Total { get; set; }
    public int LineasRelacionadas { get; set; }

    [MaxLength(2000)]
    public string Comentarios { get; set; } = "";

    public string SnapshotJson { get; set; } = "";

    public bool Activa { get; set; } = true;
    public DateTime FechaUltimaConsulta { get; set; }

    [MaxLength(150)]
    public string ActualizadoPor { get; set; } = "";
}

[Table("CompraTiCotizacion")]
public class CompraTiCotizacion
{
    public int Id { get; set; }
    public int SolicitudId { get; set; }
    [MaxLength(50)] public string? ProveedorSapCodigo { get; set; }
    [MaxLength(100)] public string NumeroCotizacion { get; set; } = "";
    public DateTime FechaCotizacion { get; set; }
    public DateTime? VigenciaHasta { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }
    [MaxLength(10)] public string Moneda { get; set; } = "MXN";
    [MaxLength(500)] public string RutaArchivo { get; set; } = "";
    [MaxLength(64)] public string HashSha256 { get; set; } = "";
    [MaxLength(40)] public string Estatus { get; set; } = "PENDIENTE_AUTORIZACION";
    public DateTime FechaRegistro { get; set; }
    [MaxLength(150)] public string RegistradoPor { get; set; } = "";
}

[Table("CompraTiFactura")]
public class CompraTiFactura
{
    public int Id { get; set; }
    public int SolicitudId { get; set; }
    [MaxLength(50)] public string Serie { get; set; } = "";
    [MaxLength(100)] public string Folio { get; set; } = "";
    [MaxLength(50)] public string Uuid { get; set; } = "";
    [MaxLength(20)] public string RfcEmisor { get; set; } = "";
    public DateTime FechaFactura { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }
    [MaxLength(500)] public string RutaPdf { get; set; } = "";
    [MaxLength(500)] public string RutaXml { get; set; } = "";
    public decimal DiferenciaContraCotizacion { get; set; }
    public bool ConciliacionOk { get; set; }
    public DateTime FechaRegistro { get; set; }
    [MaxLength(150)] public string RegistradoPor { get; set; } = "";
}

[Table("CompraTiRecepcion")]
public class CompraTiRecepcion
{
    public int Id { get; set; }
    public int SolicitudId { get; set; }
    public DateTime FechaRecepcion { get; set; }
    public bool RecibidaConforme { get; set; }
    public bool RecepcionParcial { get; set; }
    [MaxLength(2000)] public string Observaciones { get; set; } = "";
    [MaxLength(500)] public string EvidenciaRuta { get; set; } = "";
    [MaxLength(150)] public string RecibidoPor { get; set; } = "";
}

[Table("CompraTiAutorizacion")]
public class CompraTiAutorizacion
{
    public int Id { get; set; }
    public int SolicitudId { get; set; }
    [MaxLength(50)] public string Etapa { get; set; } = "";
    [MaxLength(30)] public string Decision { get; set; } = "";
    [MaxLength(1000)] public string Comentario { get; set; } = "";
    [MaxLength(150)] public string Usuario { get; set; } = "";
    public DateTime Fecha { get; set; }
}

[Table("CompraTiBitacora")]
public class CompraTiBitacora
{
    public long Id { get; set; }
    public int SolicitudId { get; set; }
    [MaxLength(80)] public string Accion { get; set; } = "";
    [MaxLength(2000)] public string Detalle { get; set; } = "";
    [MaxLength(150)] public string Usuario { get; set; } = "";
    public DateTime Fecha { get; set; }
}

public sealed class CrearCompraTiDto
{
    public string SolicitudSap { get; set; } = "";
    public int SolicitudSapDocEntry { get; set; }
    public string TipoCompra { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Justificacion { get; set; } = "";
    public string CentroCosto { get; set; } = "";
    public string Planta { get; set; } = "";
    public string ProveedorSapCodigo { get; set; } = "";
    public List<CrearCompraTiDetalleDto> Detalles { get; set; } = new();
}

public sealed class CrearCompraTiDetalleDto
{
    public int? LineaSap { get; set; }
    public string? ArticuloSapCodigo { get; set; }
    public string TipoLinea { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public decimal Cantidad { get; set; }
    public string Unidad { get; set; } = "PZA";
    public string? CentroCostoSap { get; set; }
    public string? AlmacenSap { get; set; }
    public string? ProveedorPreferidoSap { get; set; }
}

public sealed class RegistrarCotizacionCompraTiDto
{
    public int SolicitudId { get; set; }
    public string NumeroCotizacion { get; set; } = "";
    public DateTime? FechaCotizacion { get; set; }
    public DateTime? VigenciaHasta { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }
    public string Moneda { get; set; } = "MXN";
    public IFormFile? Archivo { get; set; }
}

public sealed class AutorizarCompraTiDto
{
    public int SolicitudId { get; set; }
    public bool Autorizar { get; set; }
    public string Comentario { get; set; } = "";
}

public sealed class RegistrarRecepcionCompraTiDto
{
    public int SolicitudId { get; set; }
    public DateTime? FechaRecepcion { get; set; }
    public bool RecibidaConforme { get; set; }
    public bool RecepcionParcial { get; set; }
    public string Observaciones { get; set; } = "";
    public IFormFile? Evidencia { get; set; }
}

public sealed class RegistrarFacturaCompraTiDto
{
    public int SolicitudId { get; set; }
    public string Serie { get; set; } = "";
    public string Folio { get; set; } = "";
    public string Uuid { get; set; } = "";
    public string RfcEmisor { get; set; } = "";
    public DateTime? FechaFactura { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }
    public IFormFile? Pdf { get; set; }
    public IFormFile? Xml { get; set; }
}

public sealed class LiberarCompraTiPagoDto
{
    public int SolicitudId { get; set; }
    public string Comentario { get; set; } = "";
}
