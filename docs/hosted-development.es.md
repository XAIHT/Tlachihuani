# Verificación de desarrollo de Tlamatini-RC alojado

Esta guía verifica el cliente alojado, el BFF de la nube y el conector saliente
del PC dentro de un entorno de desarrollo aislado. No instala aplicaciones,
no cambia xaiht.org, no expone el PC a Internet ni modifica las cuentas activas
de Tlamatini. Torrenter conserva su funcionamiento independiente.

## Ejecutar la verificación

Utiliza una consola de PowerShell ya abierta en el escritorio de Angela.
Ponla en primer plano y déjala abierta: el programa comprueba su visibilidad
antes de cada paso. No crea una segunda consola.

1. Comprueba que ya estén instalados el SDK de .NET seleccionado en global.json,
   Node.js y OpenSSL (en PATH, Git para Windows u OpenSSL-Win64). Las pruebas de
   navegador/OIDC requieren Chrome. OIDC (su proveedor Keycloak aislado) y la
   opción Docker requieren Docker con contenedores Linux. El programa no instala estas herramientas.
2. En esa misma consola ejecuta:

       & 'C:\Development\XAIHT\repo\Tlachihuani\scripts\Test-HostedDevelopment.ps1' -IncludeBrowser -IncludeOidc -IncludeDocker

3. Lee el progreso visible. Las pruebas de navegador utilizan una ventana
   visible. Si la comprobación de primer plano espera después de una prueba,
   selecciona la misma consola. Si un puerto de pruebas ya está ocupado, el
   programa se detiene y lo indica; no cierra ningún proceso para liberarlo.
4. Al terminar, abre la carpeta de evidencias indicada en la consola. La consola
   permanece abierta. Un fallo genera un resumen fallido y un error de
   PowerShell; no se presenta como una prueba aprobada.

Para ejecutar solamente las pruebas de API y conector, utiliza el mismo comando
sin opciones. Añade -IncludeBrowser para la interfaz en inglés y español,
-IncludeOidc para el flujo real del protocolo OIDC con Keycloak aislado y -IncludeDocker para compilar
la imagen Linux, comprobar que rechaza un arranque sin configuración y verificar
HTTPS/BFF dentro del contenedor real. Las
opciones son independientes. Las pruebas omitidas no cuentan como aprobadas.

## Qué hace el programa

- Crea una carpeta única dentro de artifacts/rc-hosted-validation, fuera de los
  perfiles de Windows. Allí conserva el espacio temporal, las claves generadas
  y las cachés temporales.
- Restaura las dependencias de RC/server y cloud en modo bloqueado según sus
  archivos de dependencias y compila las salidas Release de desarrollo. No crea
  un instalador de publicación, una etiqueta Git ni una publicación.
- Genera un certificado TLS autofirmado RSA/SHA-256 de dos días, válido para las
  direcciones IP 127.0.0.1 y 127.0.0.2. Los clientes fijan la confianza a ese
  certificado de prueba; no se añade nada al almacén de confianza de Windows.
  Schannel utiliza una importación de clave privada MachineKeySet administrada
  por Windows, fuera de los perfiles y sin PersistKeySet; el proceso la libera
  al desechar el certificado. Linux mantiene la clave efímera. No existe una
  alternativa UserKeySet. La generación automática del certificado de desarrollo
  del SDK se desactiva durante la ejecución. La contraseña PFX se pasa a OpenSSL mediante una variable de entorno, sin
  imprimirla como argumento de la línea de comandos.
- Ejecuta un servidor RC y un BFF cloud reales y aislados, con cuentas sintéticas
  explícitas y un adaptador determinista de la aplicación principal. El conector
  del PC se autentica hacia la nube. Las pruebas comprueban el encaminamiento
  limitado y mantienen los permisos locales de cuenta y dispositivo.
- Opcionalmente ejecuta las pruebas visibles de interfaz en inglés/español y el
  flujo OIDC. Cada campaña conserva sus propios resultados y limpieza.
- Opcionalmente compila la imagen Linux y comprueba que, sin una configuración
  explícita de desarrollo, termina con código 2. También inicia la aplicación
  Linux real como usuario no root, con sistema de archivos raíz de solo lectura,
  y comprueba HTTPS fijado, recursos de interfaz, rotación de sesión, CSRF, Origin,
  directorio y cierre de sesión dentro de su red aislada. No publica puertos del
  equipo anfitrión. Estas pruebas BFF no certifican el relé completo dentro del
  contenedor ni un despliegue de producción.
- Restaura las variables de entorno y la carpeta de trabajo originales al salir.
  Las pruebas cierran sus propios sockets, servidores auxiliares, navegadores y
  procesos. El proceso cloud nativo utiliza la opción de desarrollo
  --parent-stdin: la prueba propietaria envía la línea exacta stop (o cierra
  stdin) y espera un cierre normal para liberar el certificado y su clave en
  Windows. La terminación forzada solo se admite como alternativa al vencer
  el plazo de cierre del proceso propio y debe quedar indicada. No buscan ni cierran aplicaciones ajenas por su nombre.

PowerShell 5 convierte stderr de programas nativos en registros de error,
incluso cuando OpenSSL solamente muestra progreso normal. El programa utiliza
Continue durante cada comando nativo y comprueba LASTEXITCODE expresamente;
conserva el diagnóstico sin ocultar un código de salida fallido.

## Leer las evidencias

Cada ejecución guarda summary.json, transcript.txt y un registro por paso. La
campaña principal escribe relay-and-browser/results.json y cleanup.json. OIDC,
si se solicita, escribe oidc/results.json y cleanup.json. Docker, si se solicita,
escribe container/results.json y cleanup.json. Las capturas visibles
y los archivos descargados quedan dentro de la carpeta de la campaña cuando se
ejecutan esas pruebas.

El resumen distingue las opciones solicitadas, los códigos reales de salida y
los fallos. Las respuestas del adaptador sintético comprueban el transporte y
los permisos ejercitados; no demuestran una respuesta de un modelo real, una
versión instalada, un teléfono físico ni una conexión desde Internet. Conserva
la procedencia y los fallos al comparar ejecuciones distintas.

## Identidad y límites de seguridad

La nube es una aplicación .NET multiplataforma en src/Tlachihuani.RC.Cloud. El
único perfil habilitado es development-loopback: HTTPS, escucha y clientes en
loopback, y configuración explícita. Informa public_remote_enabled:false y
post_quantum:false. Rechaza direcciones públicas, HTTP sin cifrar, configuración
ausente y perfiles poscuánticos que todavía no están implementados.

El relé de laboratorio no tiene cifrado de extremo a extremo. El JSON de la
aplicación viaja por dos conexiones WSS cuyo TLS termina en el relé cloud, por
lo que el relé puede inspeccionar ese tráfico. Encaminamiento opaco significa
que el relé no interpreta los mensajes; no significa que su contenido esté
oculto para él. La negociación autenticada y cifrada entre el navegador y el PC
sigue siendo un requisito de producción.

El acceso de prueba utiliza credenciales sintéticas. El modo OIDC opcional
realiza el flujo estándar de código de autorización confidencial con PKCE,
nonce/state, certificado del proveedor fijado y validación de firma, emisor,
audiencia y vigencia. No cambia silenciosamente al acceso de prueba. El emisor
y el identificador inmutable subject deben estar vinculados explícitamente a
un usuario cloud; coincidir en correo o nombre visible nunca basta. Los tokens
no se guardan en el almacenamiento del navegador.

El proveedor OIDC utiliza otra IP loopback (por ejemplo 127.0.0.2 frente a
127.0.0.1), porque las cookies no se aíslan por puerto. La devolución fija del
proveedor es el origen cloud seguido de /signin-oidc. El navegador recibe la
misma sesión cloud opaca y HttpOnly con cualquiera de los dos modos de acceso.

Eliminar un vínculo cloud es persistente: la petición DELETE autenticada del
propietario guarda la denegación antes de confirmar el resultado, cierra sus
conexiones y mantiene la denegación después de reiniciar. Iniciar sesión en el
sitio no vuelve a conceder permisos del dispositivo en el PC. Un archivo de
denegaciones dañado o ilegible impide el arranque. El almacenamiento distribuido,
la protección frente a restauraciones antiguas y el despliegue público de
identidad requieren verificación independiente.

## Antes de publicar

El archivo del contenedor permite compilar una imagen; no autoriza ni certifica
su publicación. El diseño remoto más estricto todavía requiere el perfil de
sesión revisado ML-KEM-1024 / ML-DSA-87 y una distribución del cliente con
confianza independiente, o la aceptación explícita de otro perfil de confianza
en el código alojado. Un sitio o una PWA instalada no puede garantizar por sí
misma que su JavaScript mutable sea independiente del servidor que lo publica.

Las imágenes base del SDK y del entorno de ejecución ya están fijadas mediante
sus valores SHA-256 exactos. El artefacto final de publicación todavía requiere
firma y fijar el digest de la imagen publicada como parte de su aceptación
para despliegue.

DNS/TLS públicos, el proveedor real de identidad, secretos de producción,
aislamiento cloud, pruebas desde redes externas y teléfonos físicos, revisión
independiente de seguridad y aceptación de las aplicaciones instaladas son
requisitos separados. No expongas este servidor de desarrollo debilitando sus
comprobaciones loopback ni añadiendo un proxy sin restricciones.

Consulta el [contrato cloud](../src/Tlachihuani.RC.Cloud/README.md), el
[diseño alojado](design/tlamatini-rc-internet-design.html) y la
[política de ejecución visible](development-execution.md).

Verificación del 15 de noviembre de 2026: [443 pruebas distintas aprobadas, evidencias y límites pendientes](verification/2026-11-15-rc-hosted-development.md).
