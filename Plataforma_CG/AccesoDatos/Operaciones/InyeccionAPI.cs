using Microsoft.Extensions.Configuration;
using System.IO;

namespace Plataforma_CG.AccesoDatos.Operaciones
{
    public class InyeccionAPI
    {
        private readonly string _baseUrl;
        private readonly string _baseUrlWrite;

        public InyeccionAPI()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();
            _baseUrl = builder["InyeccionesApi:BaseUrl"] ?? "http://10.1.1.2:252/";
            _baseUrlWrite = builder["InyeccionesApi:BaseUrlWrite"] ?? _baseUrl;
        }

        public InyeccionAPI(IConfiguration configuration)
        {
            _baseUrl = configuration["InyeccionesApi:BaseUrl"] ?? "http://10.1.1.2:252/";
            _baseUrlWrite = configuration["InyeccionesApi:BaseUrlWrite"] ?? _baseUrl;
        }

        public string BaseUrl
        {
            get
            {
                if (!_baseUrl.EndsWith("/"))
                    return _baseUrl + "/";
                return _baseUrl;
            }
        }

        public string BaseUrlWrite
        {
            get
            {
                if (!_baseUrlWrite.EndsWith("/"))
                    return _baseUrlWrite + "/";
                return _baseUrlWrite;
            }
        }

        public HttpClient Client()
        {
            var http = new HttpClient();
            http.BaseAddress = new Uri(BaseUrl);
            return http;
        }

        public HttpClient ClientWrite()
        {
            var http = new HttpClient();
            http.BaseAddress = new Uri(BaseUrlWrite);
            return http;
        }
    }
}
