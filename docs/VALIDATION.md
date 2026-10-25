# Validación de HEXAWALL 1.1.0

Fecha: 2026-10-25. Windows local, .NET SDK 8.0.426, configuración Release / win-x64.

## Verificado

- Restauración con lockfile, compilación y publicación Single-File autónoma completadas.
- Pruebas funcionales: geometría hexagonal, interpolación deltaTime equivalente a 60/144/165 Hz, ICO de ocho tamaños y colores oscuros.
- Caché asíncrona 960×540, preview seleccionado desde original de al menos 1280×720, descarte de resultados antiguos, vaciado para juegos y eventos de creación/cambio/eliminación del watcher.
- RegisterHotKey real: colisiones conservan el registro anterior; cambio, suspensión y reanudación funcionan.
- Configuración persistente y rollback ante fallo de escritura. El autoinicio se probó mediante un servicio simulado, sin modificar el registro personal.
- UI real: Bounds físicos completos del monitor de 1920×1200, estilo layered y alfa por píxel de 55 %. Capturas de Honeycomb, CoverFlow, Cards y configuración revisadas visualmente.
- Controles reales de configuración guardan cambios; el selector puede ocultarse y reabrirse; CompositionTarget.Rendering se desconecta al ocultarse. Tier WPF reportado: 2.
- El detector excluye el propio selector cuando Windows concede foco a la ventana.

## Límites de estas pruebas

La prueba matemática de deltaTime no es un benchmark de FPS: no se midieron 144/165 FPS en hardware con esas frecuencias. Tampoco se midieron latencia de apertura ni RAM de esta versión; no se garantiza <16 ms ni <35 MB. Las imágenes de las pruebas son sintéticas. Las capturas RenderTargetBitmap preservan transparencia y pueden verse sobre negro según el visor; el overlay no usa una captura del escritorio como fondo.

No se compiló Inno Setup localmente ni se ejecutó el workflow en GitHub. No se cambió el wallpaper personal ni se validó WorkerW en una matriz de versiones Windows/múltiples monitores en esta revisión. El código del instalador y workflow se incluye íntegro.

## Reproducción

Desde la raíz del repositorio:

```powershell
dotnet restore tests/HexaWall.Tests/HexaWall.Tests.csproj --locked-mode --configfile NuGet.Config
dotnet run --project tests/HexaWall.Tests -c Release --no-restore
dotnet run --project tests/HexaWall.Tests -c Release --no-restore -- --ui "$PWD/artifacts/ui-check"
dotnet restore src/HexaWall/HexaWall.csproj -r win-x64 -p:SelfContained=true --locked-mode --configfile NuGet.Config
dotnet publish src/HexaWall/HexaWall.csproj -c Release -r win-x64 --self-contained true --no-restore -p:Version=1.1.0 -o artifacts/publish
```

Las pruebas crean y eliminan una carpeta temporal aislada. La prueba UI abre ventanas visibles durante unos segundos y requiere una sesión de escritorio interactiva.
