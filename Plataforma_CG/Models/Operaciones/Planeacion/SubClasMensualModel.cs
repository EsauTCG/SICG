namespace Plataforma_CG.Models.Operaciones.Planeacion
{
    public class SubClasMensualModel
    {
        public int Id { get; set; }
        public string SkuClasificacion { get; set; }
        public string Nombre { get; set; }
        public List<PlaneacionMensualModel> DetalleMensual { get; set; }
        public int Canales { get; set; }
        public double PesoTotal { get; set; }
        public double PesoPromedio { get; set; }
        public void CalcCanales()
        {
            int res=0;
            foreach (var item in DetalleMensual)
            {
                res += item.Canales;
            }
            Canales = res;
        }
        public void CalcTotal()
        {
            double res = 0.0;
            foreach (var item in DetalleMensual)
            {
                res += item.PesoTotal;
            }
            PesoTotal= res;
        }
        public void CalcPromedio()
        {
            double res = 0.0;
            res = PesoTotal / Canales;
            PesoPromedio = res;
        }
    }
}
