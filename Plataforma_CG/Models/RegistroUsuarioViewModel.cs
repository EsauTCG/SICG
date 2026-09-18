using System.ComponentModel.DataAnnotations;

public class RegistroUsuarioViewModel
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(80)]
    [Display(Name = "Nombre")]
    public string Nombre { get; set; } = string.Empty;


    [Required(ErrorMessage = "Los apellidos son obligatorios.")]
    [StringLength(120)]
    [Display(Name = "Apellidos")]
    public string Apellidos { get; set; } = string.Empty;


    [Required(ErrorMessage = "El correo electrónico es obligatorio.")]
    [EmailAddress(ErrorMessage = "Ingresa un correo electrónico válido.")]
    [StringLength(150)]
    [Display(Name = "Correo electrónico")]
    public string Correo { get; set; } = string.Empty;


    [Required(ErrorMessage = "El usuario es obligatorio.")]
    [StringLength(
        50,
        MinimumLength = 4,
        ErrorMessage = "El usuario debe contener entre 4 y 50 caracteres."
    )]
    public string Usuario { get; set; } = string.Empty;


    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    [DataType(DataType.Password)]
    [StringLength(
        100,
        MinimumLength = 8,
        ErrorMessage = "La contraseña debe contener al menos 8 caracteres."
    )]
    [Display(Name = "Contraseña")]
    public string Password { get; set; } = string.Empty;


    [Required(ErrorMessage = "Confirma tu contraseña.")]
    [DataType(DataType.Password)]
    [Compare(
        nameof(Password),
        ErrorMessage = "Las contraseñas no coinciden."
    )]
    [Display(Name = "Confirmar contraseña")]
    public string ConfirmarPassword { get; set; } = string.Empty;
}