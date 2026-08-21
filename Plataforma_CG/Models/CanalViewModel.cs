using System;
using System.Collections.Generic;

namespace Plataforma_CG.Models {  
    public class CanalViewModel
    {
        public string Id { get; set; }
        public string Provider { get; set; }
        public string Status { get; set; }
        public string Date { get; set; }
        public string Shift { get; set; }
        public string Lot { get; set; }
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
    }
}