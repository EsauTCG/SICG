UPDATE SidebarModulos
SET Nombre = ISNULL(Nombre, 'Sin nombre'),
    Url = ISNULL(Url, '#'),
    Icono = ISNULL(Icono, 'circle')
WHERE Nombre IS NULL OR Url IS NULL OR Icono IS NULL;
