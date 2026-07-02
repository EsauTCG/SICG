namespace Plataforma_CG.AccesoDatos.Operaciones
{
    public class InyeccionAPI
    {
        public HttpClient Client(int op)
        {
            var http= new HttpClient();
            switch (op) //Switch por la posibilidad de acceder a distintas API's 
            {
                default:
                    {
                        http.BaseAddress = new Uri("http://10.1.1.2:252/");
                    }
                    break;
            }
            return http;
        }
    }
}
