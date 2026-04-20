# POS Backend – .NET 8 Web API

Backend del sistema POS (multiempresa) con inventario, ventas y facturación electrónica (Ecuador).

---

## Tecnologías

- .NET 8
- ASP.NET Core Web API
- Entity Framework Core
- PostgreSQL
- Swagger

---

## Requisitos

- .NET 8 SDK
- PostgreSQL
- Visual Studio 2022 o VS Code

---

## Configuración

El archivo `appsettings.Development.json` contiene valores locales no sensibles y placeholders seguros.

Los secretos de desarrollo deben configurarse con `dotnet user-secrets`.

Ejemplo de estructura de configuración local:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": ""
  },
  "Jwt": {
    "Key": "",
    "Issuer": "PosBackend",
    "Audience": "PosFrontend",
    "ExpiresMinutes": 120
  }
}
```

## 🔐 Configuración de secretos (Desarrollo)

Este proyecto NO almacena secretos en el repositorio.

Los valores sensibles como:
- cadena de conexión a PostgreSQL
- clave JWT

deben configurarse usando .NET User Secrets.

### 📍 Ubicación de los secretos

Windows:
C:\Users\<TU_USUARIO>\AppData\Roaming\Microsoft\UserSecrets\<UserSecretsId>\secrets.json

WSL / Linux:
~/.microsoft/usersecrets/<UserSecretsId>/secrets.json

⚠️ No editar manualmente estos archivos.

### 🚀 Configuración inicial

Ejecutar dentro del proyecto:

dotnet user-secrets set "ConnectionStrings:DefaultConnection" "TU_CADENA_REAL"
dotnet user-secrets set "Jwt:Key" "TU_CLAVE_REAL"
dotnet user-secrets set "Jwt:Issuer" "PosBackend"
dotnet user-secrets set "Jwt:Audience" "PosFrontend"
dotnet user-secrets set "Jwt:ExpiresMinutes" "120"

### 🔍 Ver secretos

dotnet user-secrets list

### ⚠️ Importante

- Los secretos son locales a cada entorno
- Deben configurarse en WSL y Windows si se usan ambos

### 🧠 Orden de carga de configuración

1. appsettings.json
2. appsettings.Development.json
3. user-secrets
4. variables de entorno

---

## Levantar el proyecto localmente

Desde la carpeta del proyecto (`Pos.Backend.Api/Pos.Backend.Api`):

```bash
dotnet restore
```

Ejecutar migraciones (EF Core):

```bash
dotnet ef database update
```

Ejecutar la API:

```bash
dotnet run
```

---

## URLs (desarrollo)

Los perfiles locales definen los siguientes puertos:

- HTTP: `http://localhost:5125`
- HTTPS: `https://localhost:7096`
- Swagger: `https://localhost:7096/swagger`

---

## Probar endpoints (Auth)

- `POST /api/auth/login`
- `POST /api/auth/register`
- `GET /api/auth/me` (requiere JWT en `Authorization: Bearer <token>`)

### Credenciales demo (SeedDevelopmentAsync)

- admin / admin123 (ADMIN)
- super / super123 (SUPERVISOR)
- cashier / cashier123 (CASHIER)

Todos los usuarios demo se generan con contexto operativo válido y coherente en base de datos:
- Company: Demo Company (RUC 9999999999001)
- Establishment: 001 (Matriz)
- EmissionPoint: 001 (Caja Principal)

---

## Notas

- La API es **stateless** y usa **JWT** para autenticación.
- La configuración base vive en `appsettings.json` y `appsettings.Development.json`.
- Los secretos locales deben cargarse mediante .NET User Secrets o variables de entorno.
