using Microsoft.Extensions.Configuration;
using Plataforma_CG.Models;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;

namespace Plataforma_CG.Services
{
    public class ActiveDirectoryService
    {
        private readonly string _domain;
        private readonly string _ldapUrl;
        private readonly string _user;
        private readonly string _password;

        public ActiveDirectoryService(IConfiguration config)
        {
            var ad = config.GetSection("AD");
            _domain = ad["Domain"];
            _ldapUrl = ad["LdapUrl"];
            _user = ad["User"];
            _password = ad["Password"];
        }

        public List<UsuarioAD> ObtenerUsuarios()
        {
            var usuarios = new List<UsuarioAD>();

            var dcHost = _ldapUrl
                ?.Replace("ldap://", "", StringComparison.OrdinalIgnoreCase)
                ?.Replace("ldaps://", "", StringComparison.OrdinalIgnoreCase);

            Console.WriteLine(" Intentando conexión AD con:");
            Console.WriteLine($"  Domain: {_domain}");
            Console.WriteLine($"  DC Host: {dcHost}");
            Console.WriteLine($"  User: {_user}");

            using var context = new PrincipalContext(ContextType.Domain, dcHost ?? _domain, _user, _password);

            Console.WriteLine("PrincipalContext creado correctamente.");

            if (!context.ValidateCredentials(_user, _password))
                throw new Exception("No se pudo autenticar en Active Directory.");

            var searcher = new PrincipalSearcher(new UserPrincipal(context));

            foreach (var result in searcher.FindAll())
            {
                if (result is not UserPrincipal user) continue;

                // Este objeto nos da acceso a TODAS las propiedades de AD
                if (result.GetUnderlyingObject() is DirectoryEntry de)
                {
                    // Usamos una función auxiliar para obtener el valor de forma segura
                    string puesto = GetProperty(de, "title");
                    string departamento = GetProperty(de, "department");
                    string company = GetProperty(de, "company"); // Ejemplo con otro campo útil

                    usuarios.Add(new UsuarioAD
                    {
                        UsuarioAd = user.UserPrincipalName ?? "",
                        Nombre = user.DisplayName ?? "",
                        Puesto = puesto // Aquí asignamos el valor de "title"
                        // También podrías añadir Departamento = departamento, etc. a tu modelo
                    });
                }
            }

            return usuarios;
        }

        // Función auxiliar para leer una propiedad de forma segura
        private string GetProperty(DirectoryEntry directoryEntry, string propertyName)
        {
            if (directoryEntry.Properties.Contains(propertyName))
            {
                return directoryEntry.Properties[propertyName].Value?.ToString() ?? "";
            }
            return "";
        }
    }
}