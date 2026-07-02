namespace Plataforma_CG.ViewModels
{
    public class CanalEstudioViewModel
    {
        public string TipoGanado { get; set; }
        public string TipoCanal { get; set; } // C/H o S/H
        public DateTime FechaEstudio { get; set; }
        public double RendimientoTotal { get; set; }
        public double MermaChiller { get; set; }
        public double MermaEstudio { get; set; }

        public string Cuadrante { get; set; }

        public string Master { get; set; }   // ✅ ahora cada producto tiene SKU

        public double PesoCanalCaliente { get; set; }

        public double PesoCanalFrio { get; set; }
        public List<ProductoEstudio> Productos { get; set; }
    }

    public class ProductoEstudio
    {
        
        public string Nombre { get; set; }
        public double Kg { get; set; }
        public double PorcentajeReal { get; set; }
        public double PorcentajeObjetivo { get; set; }
        public double Diferencia => PorcentajeReal - PorcentajeObjetivo;
    }
}
