using System;
using System.Collections.Generic;

namespace Plataforma_CG.ViewModels
{
    public class CosteoFiltroVM
    {
        public string Source { get; set; } = "P1";
        public string TipoProceso { get; set; } = "CAJAS";
        public string Modo { get; set; } = "DIA";

        public DateTime FechaInicial { get; set; } = DateTime.Today;
        public DateTime FechaFinal { get; set; } = DateTime.Today;

        public int TipoCosteoId { get; set; } = 1;
        public int? LoteId { get; set; }

        public bool BrincarSinCosto { get; set; } = true;
        public bool ContinuarConError { get; set; } = true;

        public bool Automatico { get; set; }
        public string HoraProgramada { get; set; } = "18:00";

        public List<CosteoBitacoraRowVM> Resultados { get; set; } = new();
    }

    public class CosteoBitacoraRowVM
    {
        public long Id { get; set; }
        public DateTime FechaEjecucion { get; set; }

        public DateTime? FechaInicioReal { get; set; }
        public DateTime? FechaFinReal { get; set; }

        public string Source { get; set; }
        public string TipoProceso { get; set; }
        public string SpEjecutado { get; set; }

        public DateTime? FechaInicial { get; set; }
        public DateTime? FechaFinal { get; set; }

        public int? LoteId { get; set; }
        public int? TipoCosteoId { get; set; }

        public string HoraProgramada { get; set; }
        public bool EsAutomatico { get; set; }

        public bool? BrincarSinCosto { get; set; }
        public bool? ContinuarConError { get; set; }

        public bool Ok { get; set; }
        public string Mensaje { get; set; }
        public string Usuario { get; set; }
        public string Parametros { get; set; }
    }

    public class CosteoProgramadoVM
    {
        public int Id { get; set; }
        public string Source { get; set; }
        public string TipoProceso { get; set; }
        public int TipoCosteoId { get; set; }
        public string HoraProgramada { get; set; }
        public bool BrincarSinCosto { get; set; }
        public bool ContinuarConError { get; set; }
        public bool Activo { get; set; }
        public string UsuarioAlta { get; set; }
        public DateTime FechaAlta { get; set; }
    }
}