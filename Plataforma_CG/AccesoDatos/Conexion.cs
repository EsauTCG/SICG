namespace Plataforma_CG.AccesoDatos
{
    public class Conexion
    {
        private string cadenaSQLVentas = string.Empty;
        private string cadenaSQLSIGO = string.Empty;
        private string cadenaSQLVentasViejo = string.Empty;
        private string cadenaSQLTIFVentas = string.Empty;
        private string cadenaODBCSAP = string.Empty;
        private string cadenaSQLP1 = string.Empty;
        public Conexion()
        {
            var builder = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json").Build();
            cadenaSQLVentas += builder.GetSection("ConnectionStringsVentas:CadenaSQL").Value;
            cadenaSQLSIGO += builder.GetSection("ConnectionStrings:CadenaSQLSIGO").Value;
            cadenaSQLVentasViejo += builder.GetSection("ConnectionStringsVentas:CadenaSQL.OLD").Value;
            cadenaSQLTIFVentas += builder.GetSection("ConnectionStringsVentas:CadenaSQLTIF").Value;
            cadenaSQLP1 += builder.GetSection("ConnectionStrings:CadenaMeatP1").Value;
            //cadenaODBCSAP = "Driver={HDBODBC 64-bit};ServerNode=172.120.80.3:30013;Database=NDB;Uid=SYSTEM;Pwd=B1ADMINhana;";
            //cadenaODBCSAP = "Driver={HDBODBC};ServerNode=172.120.80.3:30013;Database=NDB;Uid=SYSTEM;Pwd=B1ADMINhana;";
            cadenaODBCSAP = "DSN=HANA_LinkedServer;Uid=SYSTEM;Pwd=B1ADMINhana;";
        }
        public string GetCadenaSQLVentas()
        {
            return cadenaSQLVentas;
        }
        public string GetCadenaSQLP1()
        {
            return cadenaSQLP1;
        }
        public string GetCadenaSQLSIGO()
        {
            return cadenaSQLSIGO;
        }
        public string GetCadenaSQLVentasViejo()
        {
            return cadenaSQLVentasViejo;
        }
        public string GetCadenaSQLTIFVentas()
        {
            return cadenaSQLTIFVentas;
        }
        public string GetCadenaODBC()
        {
            return cadenaODBCSAP;
        }
        public HttpClient ConAPI(int op=0)
        {
            var http = new HttpClient();
            switch (op) // Este switch tiene proyección a posibles distintas conexiones de API
            {
                default:
                    {
                        http.BaseAddress = new Uri("http://10.1.1.2:252/");
                    }break;
            }

            return http;
        }
    }
}
