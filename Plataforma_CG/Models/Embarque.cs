namespace Plataforma_CG.Models
{
    public class Embarque
    {
        public int Id { get; set; }
        public string Consecutivo { get; set; } = null!;
        public string? NombreEmbarque { get; set; }
        public DateTime FechaCreacion { get; set; }
        public string? UsuarioGenera { get; set; }
        public int Estatus { get; set; }
        public string? Observaciones { get; set; }
        public DateTime? FechaEntrada { get; set; }
        public DateTime? FechaSalida { get; set; }
        public ICollection<EmbarqueOrden> Ordenes { get; set; }
        public ICollection<EmbarqueArchivo> Archivos { get; set; } = new List<EmbarqueArchivo>();
        public EmbarqueQR? QR { get; set; }
        public DateTime? FechaLlegadaDestino { get; set; }
        public DateTime? FechaRetrasado { get; set; }
        public DateTime? FechaEntregado { get; set; }
        public DateTime? FechaDevuelto { get; set; }
        public bool? RequiereCartaPorte { get; set; }

        // DOCUMENTOS
        public string? CartaPorteArchivo { get; set; }
        public string? FichaTecnicaArchivo { get; set; }
        public string? CartaGarantiaArchivo { get; set; }

        // VALIDACIÓN DE DOCUMENTOS
        public bool? DocumentacionAprobada { get; set; }
        public DateTime? FechaValidacionDocumentacion { get; set; }
        public string? UsuarioValidaDocumentacion { get; set; }

        // VALIDACIÓN DE DOCUMENTOS - CALIDAD
        public bool? DocumentacionCalidadAprobada { get; set; }
        public DateTime? FechaValidacionDocumentacionCalidad { get; set; }
        public string? UsuarioValidaDocumentacionCalidad { get; set; }

        // CAMPOS EXISTENTES
        public decimal? TemperaturaUnidadCalidad { get; set; }
        public string? EstadoUnidadCalidad { get; set; }
        public string? EstadoProductosCalidad { get; set; }
        public string? ObservacionesCalidad { get; set; }
        public DateTime? FechaValidacionCalidad { get; set; }
        public string? UsuarioValidaCalidad { get; set; }
        public bool? CalidadAprobada { get; set; }

        // NUEVOS CAMPOS DE CALIDAD
        public string? SalidaTipo { get; set; }
        public string? PlacaTransporte { get; set; }
        public decimal? TemperaturaProgramacion { get; set; }
        public DateTime? HoraInicioEmbarque { get; set; }
        public DateTime? HoraTerminoEmbarque { get; set; }
        public decimal? TemperaturaUnidadInicio { get; set; }
        public decimal? TemperaturaUnidadTermino { get; set; }
        public string? CodigoTermograficador { get; set; }
        public string? NumeroTermometro { get; set; }
        public string? AccionesCorrectivasCalidad { get; set; }

        //Campo para Mapa de Carga
        public string? MapaCargaOrdenJson { get; set; }

        public List<EmbarqueDocumento> Documentos { get; set; } = new();

        public class EmbarqueCalidadFoto
        {
            public int Id { get; set; }
            public int EmbarqueId { get; set; }
            public string RutaArchivo { get; set; } = null!;
            public DateTime FechaRegistro { get; set; }
            public string? UsuarioRegistro { get; set; }

            public Embarque Embarque { get; set; } = null!;
        }

        public ICollection<EmbarqueCalidadFoto> FotosCalidad { get; set; } = new List<EmbarqueCalidadFoto>();

        public ICollection<EmbarqueProductoTemperatura> ProductosTemperatura { get; set; }
    = new List<EmbarqueProductoTemperatura>();
    }
}