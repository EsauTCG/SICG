using System.Collections.Generic;

namespace Plataforma_CG.Models
{
    public class CanalViewModel
    {
        public string Id { get; set; }      // Aquí guardaremos el formato: ConsecutivoDia-Fecha
        public string Arete { get; set; }   // Nuevo campo para el arete
        public string Provider { get; set; }
        public string Status { get; set; }
        public string Date { get; set; }
        public string Shift { get; set; }
        public string Lot { get; set; }

        public bool RevisionRealizada { get; set; }
        public bool? RevisionCorrecta { get; set; }
        public string RevisionHallazgos { get; set; }
        public string RevisionCuadrantes { get; set; }
        public string RevisionObservaciones { get; set; }
        public string RevisionInspector { get; set; }
        public List<RegistroViewModel> Records { get; set; } = new List<RegistroViewModel>();
    }

    public class RegistroViewModel
    {
        public string Id { get; set; }
        public string Side { get; set; }
        public List<string> Findings { get; set; } = new List<string>();
        public List<int> Quadrants { get; set; } = new List<int>();
        public string CorrectiveAction { get; set; }
        public string Reinspection { get; set; }
        public string VerificationChannel { get; set; }
        public bool VerificationComplies { get; set; }
        public string Observation { get; set; }
        public string Inspector { get; set; }
        public string Datetime { get; set; }
        public List<int> CuadrantesVerdes { get; set; } = new List<int>();
        public List<int> CuadrantesAmarillos { get; set; } = new List<int>();
        public List<int> CuadrantesRojos { get; set; } = new List<int>();
    }
}