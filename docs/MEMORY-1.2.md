# Memoria de HEXAWALL 1.2 Lite

Mediciones locales en Windows 11, 26 de octubre de 2026. Colección aislada de 24 PNG de 3840×2160; apertura de Honeycomb, navegación, CoverFlow, Cards, cierre, reapertura y configuración. Muestreo cada 100 ms durante aproximadamente 32 segundos. No se modificó el wallpaper del usuario.

| Ejecución | Pico privado residente | Pico compromiso privado | Pico working set total |
|---|---:|---:|---:|
| Single-file sin comprimir, caché inicialmente vacía | 29,39 MiB | 34,13 MiB | 85,97 MiB |
| Desarrollo, caché caliente y configuración visual corregida | 27,25 MiB | 33,17 MiB | 84,14 MiB |

El reposo inicial de la primera ejecución fue 9,36 MiB privados residentes. Datos completos: [publicado, caché fría](memory/cold-published.json) y [desarrollo, caché caliente](memory/warm-development.json). Los picos de cada columna pueden ocurrir en instantes distintos.

**El objetivo de 50 MB se cumple en estas pruebas para memoria privada residente**, la métrica comparable con la pestaña Procesos del Administrador de tareas. El working set total incluye DLL compartidas y supera 50 MB: no se afirma lo contrario. Un MiB son 1.048.576 bytes. Son picos observados, no una garantía para todos los equipos, codecs, carpetas o imágenes ni para intervalos menores de 100 ms.

No se llama a EmptyWorkingSet ni se fuerza una reducción artificial del working set. No hay otro proceso de renderizado oculto. La aplicación mantiene un único proceso; usa el compositor normal de Windows.

## Cambios

Se retiraron WPF y Skia del ejecutable distribuido. WinForms/Win32 dibuja una superficie alfa acotada a 1440×860; otra ventana nativa oscurece el monitor completo. WIC reduce las imágenes antes de copiarlas al bitmap administrado. Hay como máximo 18 imágenes visibles, miniaturas residentes de hasta 320×180 e imagen seleccionada de hasta 960×540. El trabajo de decodificación es secuencial y descarta peticiones obsoletas. Al cerrar se destruyen imágenes, DIB, ventanas y watcher.

La caché JPEG en disco tiene un presupuesto de 128 MiB. El índice toma como máximo 20.000 archivos antes de ordenar, para evitar ordenar una carpeta ilimitada en RAM. Los originales mayores de 32 megapíxeles se rechazan. CoverFlow es 2D y las transiciones de escritorio están desactivadas. El single-file no comprime internamente los ensamblados; el ZIP sí se comprime.

## Verificación y límites

Compilación Release sin errores. Pruebas aprobadas de reducción WIC de PNG 4K, formato/stride, alfa nativo, índice, caché, conflicto de atajos, persistencia y captura de teclas. Apertura y reapertura completadas con salida 0.

El Control de aplicaciones de Windows bloqueó una recompilación local del ejecutable sin firma después del primer ensayo publicado. No se desactivó esa protección. La configuración visual final se verificó ejecutando la compilación de desarrollo con el SDK .NET, con salida 0. El portable final compila, pero su arranque como single-file final no está certificado por esa prueba de desarrollo. Una firma Authenticode válida sería necesaria para distribuir con identidad de editor verificable; no se incluye una firma inventada.

## Repetir

Usa una carpeta de datos aislada con settings.json y una carpeta de imágenes de prueba. No ejecutes este diagnóstico sobre tu configuración cotidiana: abre y cierra ventanas automáticamente. Copia tu settings.json en el directorio aislado, apunta Folder a las imágenes de prueba y asigna un atajo libre. Ejecuta desde PowerShell:

```powershell
$env:HEXAWALL_DATA_HOME = Join-Path $env:TEMP 'hexawall-memory-test'
New-Item -ItemType Directory -Force $env:HEXAWALL_DATA_HOME
# Coloca aquí el settings.json de prueba antes de iniciar.
$env:HEXAWALL_MEMORY_REPORT = Join-Path $env:HEXAWALL_DATA_HOME 'memory.json'
& .\HexaWall.exe --memory-test
```

El informe distingue PrivateWorkingSetMiB, PrivateCommitMiB y WorkingSetMiB. Revisa las etapas 0–15 y que Visible sea mayor que cero durante los selectores. El diagnóstico no aplica imágenes ni activa el autoinicio. Los temporizadores de muestreo solo existen con --memory-test.
