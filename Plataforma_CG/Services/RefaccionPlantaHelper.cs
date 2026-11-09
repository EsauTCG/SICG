using System;
using System.Collections.Generic;

namespace Plataforma_CG.Services
{
    /// <summary>
    /// Mapeo de almacenes SAP a plantas del inventario de refacciones.
    /// MAN001 y MAN003 pertenecen a TIF; MAN002 pertenece a P1.
    /// </summary>
    public static class RefaccionPlantaHelper
    {
        public static readonly string[] Plantas = { "TIF", "P1" };

        private static readonly Dictionary<string, string> AlmacenAPlanta =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["MAN001"] = "TIF",
                ["MAN003"] = "TIF",
                ["MAN002"] = "P1"
            };

        /// <summary>Devuelve TIF / P1 según el almacén de SAP, o null si no está mapeado.</summary>
        public static string? PlantaDeAlmacen(string? warehouseCode)
        {
            var code = (warehouseCode ?? "").Trim();
            if (code.Length == 0)
                return null;

            return AlmacenAPlanta.TryGetValue(code, out var planta) ? planta : null;
        }

        /// <summary>Normaliza un valor de planta a TIF / P1; devuelve null si no es válido.</summary>
        public static string? NormalizarPlanta(string? planta)
        {
            var p = (planta ?? "").Trim().ToUpperInvariant();
            return p == "TIF" || p == "P1" ? p : null;
        }

        /// <summary>Almacenes SAP que pertenecen a una planta (para mostrar en la vista).</summary>
        public static string AlmacenesDePlanta(string? planta)
        {
            var p = NormalizarPlanta(planta);
            return p switch
            {
                "TIF" => "MAN001, MAN003",
                "P1" => "MAN002",
                _ => ""
            };
        }
    }
}
