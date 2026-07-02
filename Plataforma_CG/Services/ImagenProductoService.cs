using Microsoft.AspNetCore.Hosting;
using System.IO;
using System.Collections.Generic;
using System;

namespace Plataforma_CG.Services
{
    public class ImagenProductoService
    {
        private readonly IWebHostEnvironment _env;
        private readonly Dictionary<string, string> _imagenesProductos;

        public ImagenProductoService(IWebHostEnvironment env)
        {
            _env = env;

            // Catálogo centralizado
            _imagenesProductos = new Dictionary<string, string>
            {
                { "ARRACHERA", "arrachera.png" },
                { "PALETA", "Paleta.jpeg" },
                { "CHULETON", "Chuleton.png" },
                { "CHULETÓN", "Chuleton.png" },
                { "PECHO", "Pecho.png" },
                { "PESCUEZO", "Pescuezo.jpg" },
                { "PLATANILLO", "Platanillo.png" },
                { "RECORTE", "Recorte-80-20.png" },
                { "AGUJA REGIA", "Aguja Regia.jpeg" },
                { "BRISKET", "Brisket.png" },
                { "CHAMBERETE", "Chamberete.png" },
                { "LOMO", "Lomo.png" },
                { "CLOD", "Clod.jpeg" },
                { "COSTILLA CARGADA", "Costilla Cargada.jpg" },
                { "COSTILLA", "Costilla.jpeg" },
                { "CUÑA", "Cuña.jpeg" },
                { "DESHEBRADA", "Deshebrada.png" },
                { "DIEZMILLO", "Diezmillo.jpg" },
                { "NEW YORK", "new york.png" },
                { "PULPA BLANCA", "Pulpa Blanca.jpeg" },
                { "PULPA SELECTA BLANCA", "Pulpa Blanca.jpeg" },
                { "PULPA SELECTA BOLA", "Pulpa Bola.jpg" },
                { "PULPA BOLA", "Pulpa Bola.jpg" },
                { "PULPA NEGRA", "Pulpa Negra.jpeg" },
                { "PULPA SELECTA NEGRA", "Pulpa Negra.jpeg" },
                { "RIB EYE", "ribeye.jpg" },
                { "SHORT RIB", "short rib.jpg" },
                { "RIB", "Rib con Grasa.jpeg" },
                { "SIRLOIN", "Sirloin.jpeg" },
                { "T BONE", "tbone.jpg" },
                { "T-BONE", "tbone.jpg" },
                { "TOMAHAWK", "tomahawk.png" },
                { "HUESO", "Hueso.png" },
                { "FALDA", "Falda.jpg" },
                { "SUADERO", "suadero.jpeg" },
                { "SEBO", "Sebo.jpg" },
                { "RIÑON", "riñon.png" },
                { "GIBA", "giba.png" },
                { "COLA", "Cola.png" },
            };
        }

        public string ObtenerRutaImagen(string nombre, string sku)
        {
            if (string.IsNullOrWhiteSpace(nombre) && string.IsNullOrWhiteSpace(sku))
                return Path.Combine(_env.WebRootPath, "images/CarneDefault.png");

            nombre ??= "";
            sku ??= "";
            nombre = nombre.ToUpper();
            sku = sku.ToUpper();

            foreach (var registro in _imagenesProductos)
            {
                if (nombre.Contains(registro.Key) || sku.Contains(registro.Key))
                {
                    string filePath = Path.Combine(_env.WebRootPath, "images", registro.Value);
                    if (File.Exists(filePath))
                        return filePath;
                }
            }

            return Path.Combine(_env.WebRootPath, "images/CarneDefault.png");
        }
    }
}
