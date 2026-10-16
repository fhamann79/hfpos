# DEV-531: diagnostico de identidad fiscal del certificado

## Alcance y limite de la evidencia

Correccion R3 del diagnostico de `SriFiscalReadinessService`, consumido por GET
de configuracion fiscal. No cambia firma XML/XAdES, provider, autorizacion SRI,
secuenciales, kill-switch, permisos, tenancy, contratos, EF ni migraciones.
El serial tecnico del certificado sigue siendo necesario para X509IssuerSerial;
no es el atributo de identidad `subject.serialNumber` (OID `2.5.4.5`).

No se examino ningun certificado real: se demuestra una clase de defecto,
no la causa del incidente humano ni la aceptacion del certificado HFS.
No se usaron datos, passwords, claves/certificados reales, archivos privados,
DB, API local, Contabo ni servicios SRI. Los certificados de pruebas se generan
en memoria y no se exportan con clave privada.

## Reproduccion previa a la correccion

Base: `e6be61073e62029802cce8d05a49690e3f39da3f`. Antes de modificar el servicio,
`git diff -- backend/Pos.Backend.Api/Pos.Backend.Api/Infrastructure/Services/SriFiscalReadinessService.cs`
no produjo diferencias. Se ejecuto una teoria temporal de 4 casos llamando la
decision del servicio y sus flags de readiness, sin DB/provider/cadena/red.
RUC de empresa sintetico: `1790012345001`.

| Evidencia no fiscal sintetica | Resultado original exacto | Blocking | Sandbox usable |
| --- | --- | --- | --- |
| Serial tecnico hexadecimal `0A1790012345001B` | `CERTIFICATE_RUC_MATCH` | false | true |
| Serial tecnico hexadecimal `0A1799999999001B` | `CERTIFICATE_RUC_MISMATCH` | true | false |
| SAN email `1790012345001@synthetic.invalid` | `CERTIFICATE_RUC_MATCH` | false | true |
| SAN email `1799999999001@synthetic.invalid` | `CERTIFICATE_RUC_MISMATCH` | true | false |

Comando previo: `dotnet test backend/Pos.Backend.Api/Pos.Backend.Api.Tests/Pos.Backend.Api.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~SriFiscalReadinessCertificateIdentityTests --logger "console;verbosity=detailed"`.
Resultado: 4/4 PASS reproduciendo el comportamiento incorrecto. La teoria se
convirtio despues en regresion: esos cuatro casos ahora exigen NOT_CONFIRMED.

## Allowlist y fuentes primarias

- Subject `2.5.4.97`: solo `TINEC-<13 digitos ASCII>` y
  `VATEC-<13 digitos ASCII>` exactos. Fuentes: SecurityData
  [persona natural V10, pp. 51-55](https://www.securitydata.net.ec/wp-content/downloads/Normativas/P_de_Certificados/politica_certificacion_persona_natural.pdf)
  y [representante legal V10, pp. 52-53](https://www.securitydata.net.ec/wp-content/downloads/Normativas/P_de_Certificados/politica_certificacion_representante_legal.pdf).
- SAN `2.5.29.17`, `directoryName [4]`, atributo exacto
  `1.3.6.1.4.1.43745.1.3.11`: RUC, UTF8String, 13 digitos ASCII.
  [ICERT persona natural V5, secciones 3.3.2 y 7.1](https://www.icert.fje.gob.ec/wp-content/uploads/2024/06/1-persona_natural_compressed.pdf).
- Subject `2.5.4.5`: solo `IDCEC-<CI10>` tipa cedula ecuatoriana;
  `PASEC-` es pasaporte. [ETSI EN 319 412-1 V1.5.1, 5.1.3-5.1.4](https://www.etsi.org/deliver/etsi_en/319400_319499/31941201/01.05.01_60/en_31941201v010501p.pdf)
  y perfiles SecurityData anteriores. `C` (`2.5.4.6`) debe ser un unico `EC`
  para la inferencia soportada, pero por si solo no tipa el documento.
- CI+001 solo se compara si la CI tipada y la base del RUC de empresa tienen
  forma natural conocida (ASCII, provincia 01-24, tercer digito 0-5, checksum CI),
  y el sufijo es exactamente `001`. No se usa si hay RUC legal VATEC (la CI del
  representante no es la identidad de la sociedad). No se aplica un checksum
  universal a RUC explicitos ni se rechaza numeracion asignada. Fuente:
  [SRI, Reglamento RUC, articulo 3, p. 2](https://www.sri.gob.ec/o/sri-portlet-biblioteca-alfresco-internet/descargar/9822eb12-84d9-4722-88b4-5c95cc3bdda7/TRIBUTAR-REGLAMENTO_A_LA_LEY_DE_REGISTRO_UNICO_DE_CONTRIBUYENTES_RUC-1.pdf).
  En este PDF la estructura consta en art. 3; art. 5 trata uso documental.

No se infiere CI desde bare10 aunque tenga checksum y C=EC/perfil natural:
ICERT `2.5.4.5` y `.43745.1.3.1` permiten cedula **o pasaporte**.
Bare13 en subject serial tampoco es RUC tipado. Estos campos ambiguos se ignoran,
sin invalidar un RUC explicito independiente. Nombres/CN/OU, telefono, serial
tecnico, SAN email, otherName, OIDs arbitrarios y extensiones formateadas no
aportan evidencia. No se agregaron OIDs BCE/SD por analogia ni ubicaciones ASN.1
no documentadas; la cobertura no es universal para todas las CA/versiones.

## Decisiones y defensas

- RUC reconocido diferente, o CI tipada contradictoria en una comparacion natural
  aplicable: `CERTIFICATE_RUC_MISMATCH`, error bloqueante. Se evalua antes de MATCH.
- Evidencia tipada consistente y completa: `CERTIFICATE_RUC_MATCH`, success local,
  no prueba confianza, titularidad real ni aceptacion SRI.
- Sin evidencia suficiente: `CERTIFICATE_RUC_NOT_CONFIRMED`, **warning no
  bloqueante**, preservando la semantica anterior. No acredita identidad ni SRI
  y no permite saltar los otros guards. No se introduce bypass para firmar.
- ASN.1 DER estructurado, sin regex, `extension.Format` ni FriendlyName.
  Limites: 16 KiB por subject/SAN, 64 extensiones, 64 GeneralNames por SAN,
  64 atributos acumulados y 128 caracteres por valor reconocido; sin recursion.
- GeneralNames no fiscales: solo se omiten formas primitivas context-specific
  validadas mediante su lector DER: email `[1]`, DNS `[2]`, URI `[6]` como
  IA5String no vacio de ASCII imprimible sin espacios/controles; IP `[7]` de
  exactamente 4/16 octetos; registeredID `[8]` como OBJECT IDENTIFIER valido.
  No se valida semantica completa de direccion/dominio/URI ni se usa como RUC.
  Tags universales/desconocidos, formas constructed incorrectas, SAN vacio y
  formas complejas no implementadas (otherName `[0]`, x400Address `[3]`,
  ediPartyName `[5]`) descartan todo ese SAN conservadoramente. No se pretende
  implementar un validador RFC 5280 universal ni agregar perfiles de identidad.
- Subject y cada SAN se decodifican atomicamente. Un contenedor malformado o
  fuera de limites no aporta evidencia parcial, ni siquiera MISMATCH.
  Una discrepancia de otro contenedor valido se conserva. Duplicados de OID
  reconocido/SAN, valores malformados o lectura incompleta nunca dan MATCH.
  Catch limitado a `AsnContentException`, sin registrar valores de identidad.

## Validacion y gate

Pruebas unitarias de decision del servicio (incluidos flags sandbox/produccion),
certificados sinteticos en memoria, positivos/negativos/conflictos/duplicados,
passport/bare IDs, ruido OID, unicode, limites y contenedores malformados.
Se reutilizan las unitarias XAdES sin modificar el signer. Build backend Release
y pruebas focalizadas; CI debe ejecutar la suite completa sobre HEAD publicado.
PostgreSQL/concurrencia/modelo no aplican al diff de diagnostico sin persistencia.

Ejecucion local (Release):

```powershell
dotnet build backend/Pos.Backend.Api/Pos.Backend.Api.sln --configuration Release --no-restore --verbosity quiet
dotnet test backend/Pos.Backend.Api/Pos.Backend.Api.Tests/Pos.Backend.Api.Tests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~SriFiscalReadinessCertificateIdentityTests|FullyQualifiedName~SriXadesBesSignerTests" --logger "console;verbosity=minimal"
git diff --check
```

Resultado anterior sobre `344ad2e`: build PASS, 0 errores, 116 warnings en archivos
preexistentes; tests **81/81 PASS** (79 identidad, 2 XAdES); diff-check PASS.
Revision independiente Galileo encontro B0/M1: un GeneralName invalido junto a
directoryName no invalidaba el SAN. Antes de corregir el parser se reprodujeron
los 6 casos `0500`/`A100`/`8900` por RUC coincidente/ajeno: **6/6 FAIL esperados**,
obteniendo MATCH o MISMATCH en lugar de NOT_CONFIRMED. Se agregan regresiones en
ambos ordenes, preservacion de mismatch desde subject valido y casos de formas
DER validas/malformadas/no soportadas. CI/review de `344ad2e` no cubren este delta.
Tras la correccion MAJOR: build Release PASS (0 errores; primera compilacion con
116 warnings preexistentes, verificacion incremental exit 0 sin warnings nuevos),
**112/112 PASS** (110 identidad, 2 XAdES), diff-check PASS. Los seis casos tambien
comprueban el elemento invalido antes/despues de directoryName y que un mismatch
de subject valido independiente siga bloqueando. No se ejecuto la suite completa
local ni se presenta esta evidencia como CI de un HEAD nuevo.

**VALIDACION HUMANA PRE-MERGE REQUERIDA**, pendiente y coordinada fuera de este
implementer: comprobar los tres estados con material sintetico, que un mismatch
siga bloqueando readiness y que NOT_CONFIRMED siga warning sin afirmar identidad.
La aceptacion real HFS/SRI es otra validacion humana separada, no realizada aqui.
Revision independiente y CI exact-HEAD pendientes. Estado: **PENDIENTE, NO MERGE**.
