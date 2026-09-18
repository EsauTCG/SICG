namespace Plataforma_CG.ViewModels.ComparativaCosteo
{
    public class ComparativaCosteoRowVM
    {
        //Tipo de datos String
        public string CodigoEtiqueta { get; set; }

        public string LoteTIF { get; set; }

        public string LoteP1 { get; set; }

        public string SKUTIF { get; set; }

        public string SKUP1 { get; set; }

        public string ProductoTIF { get; set; }

        public string ProductoP1 { get; set; }




        //Tipo de datos numéricos
        public decimal? PesoTIF { get; set; }

        public decimal? PesoP1 { get; set; }

        public decimal? DiferenciaPeso { get; set; }

        public decimal? CostoTIF { get; set; }

        public decimal? CostoP1 { get; set; }

        public decimal? DiferenciaCosto { get; set; }




        // Estado resultante de la comparación (Coincide, Diferente, etc.)
        public string EstadoCosto { get; set; }
        


        // Identificadores de tipo entero
        public long? ProduccionIdTIF { get; set; }

        public long? ProduccionIdP1 { get; set; }


        

    }
}