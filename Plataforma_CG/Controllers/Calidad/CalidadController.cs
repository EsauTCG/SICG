using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using Plataforma_CG.Data;
using Plataforma_CG.Filters;
using Plataforma_CG.Models;

namespace Plataforma_CG.Controllers
{
    public class CalidadController : Controller
    {
        public IActionResult Cuadrantes()
        {
            var listaCanales = new List<CanalViewModel>();

            // Canal 1: En inspeccion con hallazgos
            listaCanales.Add(new CanalViewModel
            {
                Id = "CNL-025481",
                Provider = "Ganadería Los Olivos",
                Status = "En inspección",
                Date = "04/08/2026",
                Shift = "Mañana",
                Lot = "LT-0810",
                Records = new List<RegistroViewModel>
                {
                    new RegistroViewModel {
                        Id = "rec-1", Side = "I", Findings = new List<string> { "MF", "Pelo" },
                        Quadrants = new List<int> { 1, 2 }, CorrectiveAction = "√", Reinspection = "SC",
                        VerificationChannel = "CNL-025481", VerificationComplies = true,
                        Observation = "Se retiro contaminacion visible desde backend C#.",
                        Inspector = "calidad", Datetime = "04/08/2026 08:15 a. m."
                    }
                }
            });

            // Canal 2: Pendiente
            listaCanales.Add(new CanalViewModel
            {
                Id = "CNL-025482",
                Provider = "Rancho El Paraíso",
                Status = "Pendiente",
                Date = "04/08/2026",
                Shift = "Mañana",
                Lot = "LT-0811"
            });

            // Canal 3: Conforme y terminada
            listaCanales.Add(new CanalViewModel
            {
                Id = "CNL-025483",
                Provider = "Ganadería San José",
                Status = "Conforme",
                Date = "04/08/2026",
                Shift = "Mañana",
                Lot = "LT-0811",
                Records = new List<RegistroViewModel>
                {
                    new RegistroViewModel {
                        Id = "rec-2", Side = "I", Findings = new List<string>(),
                        Quadrants = new List<int>(), CorrectiveAction = "N/A", Reinspection = "SC",
                        VerificationChannel = "CNL-025483", VerificationComplies = true,
                        Observation = "Sin hallazgos visibles.",
                        Inspector = "calidad", Datetime = "04/08/2026 08:20 a. m."
                    },
                    new RegistroViewModel {
                        Id = "rec-3", Side = "D", Findings = new List<string>(),
                        Quadrants = new List<int>(), CorrectiveAction = "N/A", Reinspection = "SC",
                        VerificationChannel = "CNL-025483", VerificationComplies = true,
                        Observation = "Sin hallazgos visibles.",
                        Inspector = "calidad", Datetime = "04/08/2026 08:22 a. m."
                    }
                }
            });

            // Canal 4: No conforme
            listaCanales.Add(new CanalViewModel
            {
                Id = "CNL-025484",
                Provider = "Agropecuaria del Norte",
                Status = "No conforme",
                Date = "04/08/2026",
                Shift = "Mañana",
                Lot = "LT-0812",
                Records = new List<RegistroViewModel>
                {
                    new RegistroViewModel {
                        Id = "rec-4", Side = "I", Findings = new List<string> { "A", "Víscera verde" },
                        Quadrants = new List<int> { 3 }, CorrectiveAction = "X", Reinspection = "NC",
                        VerificationChannel = "CNL-025484", VerificationComplies = false,
                        Observation = "Pendiente de correccion por parte de operaciones.",
                        Inspector = "calidad", Datetime = "04/08/2026 08:30 a. m."
                    }
                }
            });

            // Canal 5: En inspeccion lado derecho
            listaCanales.Add(new CanalViewModel
            {
                Id = "CNL-025485",
                Provider = "Los Prados",
                Status = "En inspección",
                Date = "04/08/2026",
                Shift = "Mañana",
                Lot = "LT-0812",
                Records = new List<RegistroViewModel>
                {
                    new RegistroViewModel {
                        Id = "rec-5", Side = "D", Findings = new List<string>(),
                        Quadrants = new List<int>(), CorrectiveAction = "N/A", Reinspection = "SC",
                        VerificationChannel = "CNL-025485", VerificationComplies = true,
                        Observation = "Lado derecho limpio.",
                        Inspector = "calidad", Datetime = "04/08/2026 08:35 a. m."
                    }
                }
            });

            // Canal 6: Pendiente
            listaCanales.Add(new CanalViewModel
            {
                Id = "CNL-025486",
                Provider = "Hacienda La Esperanza",
                Status = "Pendiente",
                Date = "04/08/2026",
                Shift = "Mañana",
                Lot = "LT-0813"
            });

            return View(listaCanales);
        }
    }
}