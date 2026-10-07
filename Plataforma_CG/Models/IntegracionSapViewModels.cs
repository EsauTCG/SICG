using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Plataforma_CG.ViewModels
{
    public sealed class IntegracionSapFiltroVM
    {
        public string Source { get; set; } = "P1";
        public string Tipo { get; set; } = "ENTRADA";
        public DateTime Desde { get; set; } = DateTime.Today;
        public DateTime Hasta { get; set; } = DateTime.Today;
        public int? Estatus { get; set; } = null;
        public int? Folio { get; set; }
        public int Top { get; set; } = 500;
    }

    public sealed class IntegracionSapRowVM
    {
        public int IntegracionId { get; set; }
        public int TipoIntegracionId { get; set; }
        public int? Folio { get; set; }
        public DateTime FechaDocumento { get; set; }
        public int Estatus { get; set; }
        public string? SocioNegocio { get; set; }
        public string? Referencia { get; set; }
        public int CantidadLineas { get; set; }
        public int CantidadLotes { get; set; }
        public bool TieneOrdenCompra { get; set; }
        public int UbicacionesSinResolver { get; set; }
        public string? CuentaContable { get; set; }
        public DateTime? UltimoIntento { get; set; }
        public bool? UltimoExitoso { get; set; }
        public int? SapDocEntry { get; set; }
        public int? SapDocNum { get; set; }
        public string? UltimoMensaje { get; set; }

        [JsonIgnore]
        public string JsonSap { get; set; } = "{}";

        public string Planta { get; set; } = "P1";
        public string BaseDatos { get; set; } = "Next";
        public string Tipo { get; set; } = "ENTRADA";
        public string Endpoint { get; set; } = "PurchaseDeliveryNotes";

        public bool Enviado => Estatus == 1 || UltimoExitoso == true;
        public bool TieneErrorConfiguracion =>
            CantidadLineas <= 0 ||
            (Tipo == "SALIDA" && UbicacionesSinResolver > 0);
    }

    public sealed class IntegracionSapIndexVM
    {
        public IntegracionSapFiltroVM Filtro { get; set; } = new();
        public List<IntegracionSapRowVM> Rows { get; set; } = new();
        public string BaseDatos { get; set; } = "Next";
        public string Endpoint { get; set; } = "PurchaseDeliveryNotes";
        public int TipoIntegracionId { get; set; } = 1;
        public string CuentaSalida { get; set; } = "21010300";
    }

    public sealed class EnviarIntegracionSapRequest
    {
        public int IntegracionId { get; set; }
        public string Source { get; set; } = "P1";
        public string Tipo { get; set; } = "ENTRADA";
        public bool Forzar { get; set; }
    }

    public sealed class EnviarIntegracionesSapRequest
    {
        public List<int> IntegracionIds { get; set; } = new();
        public string Source { get; set; } = "P1";
        public string Tipo { get; set; } = "ENTRADA";
        public bool Forzar { get; set; }
    }

    public sealed class IntegracionSapResultadoVM
    {
        public int IntegracionId { get; set; }
        public bool Ok { get; set; }
        public bool YaEnviado { get; set; }
        public string Mensaje { get; set; } = "";
        public string Endpoint { get; set; } = "";
        public int? DocEntry { get; set; }
        public int? DocNum { get; set; }
        public string? Error { get; set; }
        public string? RespuestaSap { get; set; }
    }
}
