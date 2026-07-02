using System.Collections.Generic;

namespace Plataforma_CG.Models
{
    // ==========================================
    // MODELOS DE VISTA Y PETICIONES (USADOS POR EL CONTROLADOR)
    // ==========================================

    // Se usa para dibujar las filas en la tabla HTML (GET)
    public class EmpaqueRowVM
    {
        public string Sku { get; set; }
        public string Descripcion { get; set; }
        public string EmpaqueInt { get; set; }
        public string EmpaqueIntDesc { get; set; }
        public string EmpaqueExt { get; set; }
        public string EmpaqueExtDesc { get; set; }
        public int PzMin { get; set; }
        public int PzDef { get; set; }
        public int PzMax { get; set; }
        public decimal PesoMin { get; set; }
        public decimal PesoMax { get; set; }
        public int BolsaMin { get; set; }
        public int BolsaDef { get; set; }
        public int BolsaMax { get; set; }
        public decimal PesoExtMin { get; set; }
        public decimal PesoExtMax { get; set; }
        public string Fecha { get; set; }
    }

    // Se usa para recibir los datos de Javascript al guardar (POST)
    public class EmpaqueRequestVm
    {
        public bool ModoAlta { get; set; }
        public string NuevoSkuDesc { get; set; }
        public List<string> Skus { get; set; }

        public string EmpaqueInterno { get; set; }
        public decimal? PzaMin { get; set; }
        public decimal? PzaDef { get; set; }
        public decimal? PzaMax { get; set; }
        public decimal? PesoMin { get; set; }
        public decimal? PesoMax { get; set; }

        public string EmpaqueExterno { get; set; }
        public decimal? BolsaMin { get; set; }
        public decimal? BolsaDef { get; set; }
        public decimal? BolsaMax { get; set; }
        public decimal? PesoExtMin { get; set; }
        public decimal? PesoExtMax { get; set; }
    }

    public class EmpaqueArticuloLogVM
    {
        public long LogId { get; set; }
        public string Planta { get; set; }
        public string Sku { get; set; }
        public int? EmpaqueId { get; set; }
        public string NombreEmpaque { get; set; }
        public string Operacion { get; set; }
        public string ValoresAnteriores { get; set; }
        public string ValoresNuevos { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaHora { get; set; }
    }

}