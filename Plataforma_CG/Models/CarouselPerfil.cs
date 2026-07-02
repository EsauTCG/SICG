namespace Plataforma_CG.Models
{
    public class CarouselPerfil
    {
        public int Id { get; set; }
        public int PerfilId { get; set; }
        public Perfil Perfil { get; set; }
        public string ImagenUrl {get; set;}
        public int Orden {get; set;}
        public bool Activo { get; set;}
    }
}
