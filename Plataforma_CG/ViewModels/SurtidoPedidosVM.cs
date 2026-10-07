using System;
using System.Collections.Generic;
using System.Linq;

namespace Plataforma_CG.ViewModels
{
    public class SurtidoPedidoVM
    {
        public int SolicitudSurtidoId { get; set; }

        public DateTime FechaHora { get; set; }

        public string Pedido { get; set; } = "";

        public string Cliente { get; set; } = "";

        public int EstatusId { get; set; }

        public string Planta { get; set; } = "";

        public string OrigenConexion { get; set; } = "";

        // ============================================================
        // CONTEXTO DEL ALMACÉN DE ESTA TARJETA
        //
        // Picking puede mostrar varios almacenes simultáneamente.
        // Al abrir el pedido, este código vuelve a fijar el contexto
        // correcto para PEPS / P1 / TIF.
        // ============================================================
        public string AlmacenCodigo { get; set; } = "";

        public string AlmacenNombre { get; set; } = "";

        public string AlmacenSucursal { get; set; } = "";

        public string AlmacenClasificacion { get; set; } = "";

        // Cajas solicitadas en SolicitudSurtidoDetalle del almacén de esta tarjeta.
        public int CajasSolicitadas { get; set; }

        // Cajas ya ligadas oficialmente en SalidaEmbarque.
        public int CajasSurtidas { get; set; }

        // Cajas bajadas por Montacarguista y pendientes de Capturista.
        public int CajasBajadas { get; set; }

        public int CajasPendientes =>
            Math.Max(
                0,
                CajasSolicitadas - CajasSurtidas - CajasBajadas
            );

        public int AvancePorcentaje =>
            CajasSolicitadas <= 0
                ? 0
                : Math.Min(
                    100,
                    (int)Math.Round(
                        ((CajasSurtidas + CajasBajadas) * 100m)
                        / CajasSolicitadas
                    )
                );
    }


    public class SurtidoModuloPedidosVM
    {
        // Cuando se filtra a un solo almacén, aquí queda ese contexto.
        // En modo TODOS puede contener el primero únicamente por compatibilidad
        // con vistas/código existente; la lista real está en Almacenes.
        public SurtidoAlmacenVM Almacen { get; set; }
            = new SurtidoAlmacenVM();

        // Todos los almacenes autorizados para el usuario.
        public List<SurtidoAlmacenVM> Almacenes { get; set; }
            = new List<SurtidoAlmacenVM>();

        // "" = TODOS MIS ALMACENES
        public string FiltroAlmacen { get; set; } = "";

        public bool EsTodosAlmacenes =>
            string.IsNullOrWhiteSpace(FiltroAlmacen);

        public int TotalAlmacenesConsultados { get; set; }

        public DateTime FechaInicio { get; set; }

        public DateTime FechaFin { get; set; }

        public List<SurtidoPedidoVM> Pedidos { get; set; }
            = new List<SurtidoPedidoVM>();

        public string NombreConexion { get; set; } = "";

        public string NombreUsuario { get; set; } = "";

        public string? Error { get; set; }

        public int TotalPedidos =>
            Pedidos?.Count ?? 0;

        public int TotalCajasPendientes =>
            Pedidos?.Sum(x => x.CajasPendientes) ?? 0;

        public int TotalCajasBajadas =>
            Pedidos?.Sum(x => x.CajasBajadas) ?? 0;
    }


    public class SurtidoPedidoDetalleVM
    {
        public int SolicitudSurtidoId { get; set; }

        public string Articulo { get; set; } = "";

        public string ProductoNombre { get; set; } = "";

        public string Almacen { get; set; } = "";

        public string AlmacenNombre { get; set; } = "";

        public bool ObligaUbicacion { get; set; }

        public bool TieneConfiguracionUbicacion { get; set; }

        public decimal Cantidad { get; set; }

        public DateTime? FechaHora { get; set; }

        public decimal? KilosCaja { get; set; }

        public int? Rotacion { get; set; }

        public string Master { get; set; } = "";

        public bool EncontradoEnArticuloSap { get; set; }
    }


    public class SurtidoArticuloSapVM
    {
        public string ProductoCodigo { get; set; } = "";

        public string ProductoNombre { get; set; } = "";

        public decimal? KilosCaja { get; set; }

        public int? Rotacion { get; set; }

        public string Master { get; set; } = "";
    }


    public class SurtidoPedidoDetallePaginaVM
    {
        public SurtidoAlmacenVM Almacen { get; set; }
            = new SurtidoAlmacenVM();

        public int SolicitudSurtidoId { get; set; }

        public SurtidoPedidoVM? Pedido { get; set; }

        public List<SurtidoPedidoDetalleVM> Detalle { get; set; }
            = new List<SurtidoPedidoDetalleVM>();

        public string NombreConexion { get; set; } = "";

        public string? Error { get; set; }

        public int TotalLineas =>
            Detalle?.Count ?? 0;

        public decimal TotalCantidad =>
            Detalle?.Sum(x => x.Cantidad) ?? 0m;
    }


    // ============================================================
    // PEPS REAL
    // Cada ProduccionId representa una caja disponible.
    // ============================================================
    public class SurtidoPepsCajaVM
    {
        public long ProduccionId { get; set; }

        public string CodigoEtiqueta { get; set; } = "";

        public string Articulo { get; set; } = "";

        public string ProductoNombre { get; set; } = "";

        public string Almacen { get; set; } = "";

        public string Lote { get; set; } = "";

        public DateTime? FechaProduccion { get; set; }

        public decimal PesoNeto { get; set; }

        public long? TarimaId { get; set; }

        public string TarimaCodigo { get; set; } = "";

        // Todavía queda null hasta localizar la fuente real de ubicación.
        public string UbicacionOrigen { get; set; } = "";

        public int OrdenPeps { get; set; }

        public bool EsRecomendada { get; set; }

        public bool PuedeBajar { get; set; }

        public string MotivoBloqueo { get; set; } = "";

        public string CodigoEscaneoEsperado =>
            !string.IsNullOrWhiteSpace(TarimaCodigo)
                ? TarimaCodigo
                : CodigoEtiqueta;
    }


    public class SurtidoPepsProductoVM
    {
        public string Articulo { get; set; } = "";

        public string ProductoNombre { get; set; } = "";

        public string Almacen { get; set; } = "";

        public string AlmacenNombre { get; set; } = "";

        public int CajasSolicitadas { get; set; }

        public int CajasSurtidas { get; set; }

        public int CajasBajadas { get; set; }

        public int CajasPendientes { get; set; }

        public int CajasDisponibles { get; set; }

        public decimal? KilosCaja { get; set; }

        public int? Rotacion { get; set; }

        public string Master { get; set; } = "";

        public List<SurtidoPepsCajaVM> Cajas { get; set; }
            = new List<SurtidoPepsCajaVM>();

        public int CajasRecomendadas =>
            Cajas?.Count(x => x.EsRecomendada) ?? 0;
    }


    public class SurtidoPedidoPepsVM
    {
        public SurtidoAlmacenVM Almacen { get; set; }
            = new SurtidoAlmacenVM();

        public SurtidoPedidoVM Pedido { get; set; }
            = new SurtidoPedidoVM();

        public string NombreConexion { get; set; } = "";

        public List<SurtidoPepsProductoVM> Productos { get; set; }
            = new List<SurtidoPepsProductoVM>();

        public SurtidoPepsCajaVM? Recomendada { get; set; }

        public string? Error { get; set; }

        public int CajasSolicitadas =>
            Productos?.Sum(x => x.CajasSolicitadas) ?? 0;

        public int CajasSurtidas =>
            Productos?.Sum(x => x.CajasSurtidas) ?? 0;

        public int CajasBajadas =>
            Productos?.Sum(x => x.CajasBajadas) ?? 0;

        public int CajasPendientes =>
            Productos?.Sum(x => x.CajasPendientes) ?? 0;

        public int AvancePorcentaje =>
            CajasSolicitadas <= 0
                ? 0
                : Math.Min(
                    100,
                    (int)Math.Round(
                        ((CajasSurtidas + CajasBajadas) * 100m)
                        / CajasSolicitadas
                    )
                );
    }


    public class SurtidoPickingTareaVM
    {
        public SurtidoAlmacenVM Almacen { get; set; }
            = new SurtidoAlmacenVM();

        public SurtidoPedidoVM Pedido { get; set; }
            = new SurtidoPedidoVM();

        public SurtidoPepsCajaVM Caja { get; set; }
            = new SurtidoPepsCajaVM();

        public string NombreConexion { get; set; } = "";

        public string? Error { get; set; }
    }


    public class SurtidoBajadaFilaVM
    {
        public long Id { get; set; }

        public long SolicitudSurtidoId { get; set; }

        public long ProduccionId { get; set; }

        public string CodigoEtiqueta { get; set; } = "";

        public long? TarimaId { get; set; }

        public string TarimaCodigo { get; set; } = "";

        public string Articulo { get; set; } = "";

        public string Lote { get; set; } = "";

        public DateTime? FechaProduccion { get; set; }

        public decimal? PesoNeto { get; set; }

        public string UbicacionOrigen { get; set; } = "";

        public string Estatus { get; set; } = "";

        public string UsuarioBaja { get; set; } = "";

        public DateTime FechaBaja { get; set; }
    }
}
