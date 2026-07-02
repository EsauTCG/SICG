namespace Plataforma_CG.Models
{
    // ── Lista de precios (tab principal) ──────────────────────
    public class ProductoPrecioDto
    {
        public string Sku { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public string Demanda { get; set; } = "";   // BAJA | MEDIA | ALTA
        public decimal PrecioBase { get; set; }
        public string Vendedor { get; set; } = "";
        public List<ClienteAsociadoDto> Clientes { get; set; } = new();

        // Precios calculados por canal (se llenan en el controller)
        public PrecioCanalDto? PrecioSpot { get; set; }
        public PrecioCanalDto? PrecioActivo { get; set; }
        public PrecioCanalDto? PrecioEstrategico { get; set; }
    }

    public class ClienteAsociadoDto
    {
        public string Nombre { get; set; } = "";
        public string Canal { get; set; } = "";  // U_MT_Clasificacion mapeado
    }

    public class PrecioCanalDto
    {
        public string Canal { get; set; } = "";
        public decimal? DescPermitido { get; set; }   // null = NO VENDER
        public decimal? PrecioFinal { get; set; }
        public string Status { get; set; } = "";  // PERMITIDO | SIN DESCUENTO | NO VENDER
    }

    // ── Reglas comerciales ────────────────────────────────────
    public class ReglaComercialDto
    {
        public int Id { get; set; }
        public string Demanda { get; set; } = "";
        public string Canal { get; set; } = "";
        public decimal? DescuentoMonto { get; set; }
    }

    public class GuardarReglasRequest
    {
        public List<ReglaComercialDto> Reglas { get; set; } = new();
        public string Usuario { get; set; } = "";
    }

    // ── Autorizaciones ────────────────────────────────────────
    public class AutorizacionDto
    {
        public int Id { get; set; }
        public string Folio { get; set; } = "";
        public string Fecha { get; set; } = "";
        public string Vendedor { get; set; } = "";
        public string Cliente { get; set; } = "";
        public string Sku { get; set; } = "";
        public string ProductoDesc { get; set; } = "";
        public decimal DescSolicitado { get; set; }
        public string Status { get; set; } = "pending";
        // calculados
        public decimal PrecioBase { get; set; }
        public decimal? LimitePermitido { get; set; }
        public string Demanda { get; set; } = "";
        public string Canal { get; set; } = "";
        public string CanalCliente { get; set; } = "";
    }

    public class ProcesarAutorizacionRequest
    {
        public int Id { get; set; }
        public string Accion { get; set; } = ""; // "approve" | "reject"
        public decimal DescuentoFinal { get; set; }
        public string Usuario { get; set; } = "";
    }

    // ── Reporte histórico ─────────────────────────────────────
    public class ReporteAutorizacionDto
    {
        public int Id { get; set; }
        public string Folio { get; set; } = "";
        public string Fecha { get; set; } = "";
        public string Vendedor { get; set; } = "";
        public string Cliente { get; set; } = "";
        public string Producto { get; set; } = "";
        public decimal PrecioFinal { get; set; }
        public decimal DescAplicado { get; set; }
        public string Resolvio { get; set; } = "";
    }

    // ============================================================
    // DTOs auxiliares — agregar en Models/DTOs/ControlPreciosDtos.cs
    // ============================================================

    public class RecalcularDemandaRequest
    {
        public int PeriodoDias { get; set; } = 90;
        public string? Usuario { get; set; }
    }

    // DTO para leer el resultado del stored procedure
    public class ResumenDemandaRow
    {
        public int TotalSkus { get; set; }
        public int TotalBaja { get; set; }
        public int TotalMedia { get; set; }
        public int TotalAlta { get; set; }
        public decimal? UmbralBaja { get; set; }
        public decimal? UmbralAlta { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public string? Temporada { get; set; }
    }


    public class VentaMensualSku
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string Periodo { get; set; } = "";
        public string Sku { get; set; } = "";
        public decimal KgTotales { get; set; }
    }

    public class HistorialDemandaMes
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string Periodo { get; set; } = "";
        public decimal KgBaja { get; set; }
        public decimal KgMedia { get; set; }
        public decimal KgAlta { get; set; }
        public decimal KgTotal { get; set; }
        public int SkusBaja { get; set; }
        public int SkusMedia { get; set; }
        public int SkusAlta { get; set; }
    }
}
