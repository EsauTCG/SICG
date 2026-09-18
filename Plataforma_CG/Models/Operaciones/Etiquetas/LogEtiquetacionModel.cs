namespace Plataforma_CG.Models.Operaciones.Etiquetas
{
    public class LogEtiquetacionModel
    {

        public string Sucursal { get; set; }

        public string ArticuloId { get; set; }

        public string ProductoNombre { get; set; }

        public int? EtiqOrigen { get; set; }
        public string NomOrigen { get; set; }

        public int? EtiqNuevo { get; set; }
        public string NomNuevo { get; set; }

        public DateTime FechaHora { get; set; }
    }
}

