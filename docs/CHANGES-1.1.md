# Cambios completos de HEXAWALL 1.1

## Archivos nuevos

- `src/HexaWall/OverlayWindow.cs`: reemplaza la ventana opaca por WPF fullscreen transparente, HUD, engranaje, tres layouts y deltaTime.
- `src/HexaWall/SettingsWindow.xaml` y `.xaml.cs`: configuración gráfica oscura y captura de teclas.
- `src/HexaWall/SelectedPreview.cs`: worker prioritario HQ con cancelación por versión.
- `src/HexaWall/IconFactory.cs`: icono nativo generado en memoria y pictogramas.
- `src/HexaWall/DarkMenuRenderer.cs`: colores y renderer del tray.
- `src/HexaWall/StartupRegistration.cs`: autoinicio y controlador de guardado transaccional.
- `src/HexaWall/GlobalUsings.cs`: importación IO explícita para el proyecto híbrido.
- `assets/hexawall.ico`: ocho resoluciones, generado con IconFactory.
- `assets/previews/Settings.png`: captura de la GUI.

## Archivos modificados

- `HexaWall.csproj`: WPF, PerMonitorV2, versión 1.1.0 e icono incrustado.
- `Program.cs`: ciclo de vida híbrido, menú oscuro y aplicación de preferencias en tiempo real.
- `Settings.cs`: toggles, copias transaccionales y presupuesto de miniaturas actualizado.
- `HotKeyWindow.cs`: rebind seguro, captura suspendible y recuperación desde la bandeja.
- `Native.cs`: exclusión del propio proceso fullscreen y consulta DPI.
- `ThumbnailCache.cs`: miniaturas 960×540, WebP 94, fuentes congeladas y formato de caché v2.
- `ImageLoader.cs`: HighQuality, generación de previews y conversión a BitmapSource.
- `TransitionEngine.cs`, `TransitionForm.cs`: toggle Gaming y filtros High.
- Proyecto y programa de pruebas: configuración, cache HQ, iconos, alfa real, bounds, GUI y ciclo de vida.
- `installer/HexaWall.iss`, `.github/workflows/release.yml`: versión e icono.
- README, arquitectura, validación, SVG y capturas de los tres diseños.

## Archivo eliminado

`src/HexaWall/OverlayForm.cs`. No debe quedar en una integración manual: usa la API de caché antigua. El ZIP fuente ya lo excluye.

## Migración

La configuración y carpeta existente se conservan. Los campos nuevos toman sus valores por defecto y el autoinicio se consulta en HKCU. Las miniaturas de 400 px no se reutilizan: se genera `thumbnails-v2-960x540`. Los archivos de caché antiguos quedan intactos. No es necesario editar JSON ni reinstalar para cambiar la carpeta o el atajo.
