# Arquitectura 1.1

## Composición y escalado

La bandeja y los HWND de mensajes siguen en WinForms. El selector y Configuración son ventanas WPF en el mismo hilo STA, con `ElementHost.EnableModelessKeyboardInterop` para recibir teclado dentro del message loop WinForms. `ApplicationHighDpiMode=PerMonitorV2`, el manifiesto y la inicialización del proceso están alineados.

El selector usa `WindowStyle=None`, `AllowsTransparency=true`, fondo de ventana transparente y un único scrim `Color.FromArgb(140,10,12,16)`. Los hijos conservan opacidad independiente. No hay barras, ventana contenedora centrada, color key ni captura del escritorio. El HWND se coloca con SetWindowPos usando coordenadas físicas de `Screen.Bounds`; tras WM_DPICHANGED, WPF actualiza el DPI y se vuelve a ajustar el rectángulo completo. Soporta orígenes negativos y usa el monitor del cursor.

La cuadrícula solo crea elementos para la página visible. Cada hexágono es un StreamGeometry; Cards usa RectangleGeometry redondeada. El hit testing respeta las geometrías transformadas. CoverFlow usa Viewport3D, cámara de perspectiva, planos redondeados, rotaciones Y y textura 16:9 con Stretch.Fill en el espacio UV. Esto evita deformar la imagen al aplicar UniformToFill sobre coordenadas UV cuadradas.

## Imágenes

La versión de caché forma parte del hash y del directorio: v2-960x540-q94. La miniatura es WebP 960×540 y calidad 94. Skia normaliza EXIF y usa `SKFilterQuality.High` en decode/resize/dibujo. Los BitmapSources resultantes se congelan antes de publicarlos a WPF. Los elementos y brushes WPF solicitan HighQuality.

`ThumbnailCache` mantiene watcher, reconciliación, canales acotados, generación fuera del hilo UI y LRU. El primer grupo de seis miniaturas se precalienta; el resto se solicita a medida que aparece. La memoria se contabiliza por píxeles residentes. Los elementos visibles retienen sus propias referencias hasta que salen de la página o se cierra el overlay, por lo que el límite LRU no equivale al total del proceso.

`SelectedPreview` es un worker separado con cola de capacidad uno y política latest-wins. Lee el original de la selección activa para producir una vista de 1280×720 como mínimo; aumenta su resolución según el tamaño físico previsto y DPI hasta 3840×2160. Solo publica un resultado cuya versión siga vigente. Un lock hace atómicas la publicación, la invalidación y la limpieza para que una carga vieja no reaparezca después de cerrar. El resultado HQ no sustituye permanentemente todas las miniaturas de la biblioteca.

Al cambiar de carpeta, se instala una caché/watch nueva y se retira la anterior fuera del hilo UI. No se conserva un control por cada archivo del directorio. Los límites de decodificación y formatos de v1 se mantienen: 32 megapíxeles por archivo y conversión de WebP a JPEG al asignarlo a Windows.

## Animación

`CompositionTarget.Rendering` proporciona el ritmo de actualización. Un Stopwatch mide deltaTime; `target + (current-target) * exp(-rate*dt)` hace converger posición, escala y opacidad sin depender del número de frames. El delta se acota tras pausas largas. Las suscripciones se eliminan al estabilizarse u ocultarse: no hay un timer de animación a 15 ms en el selector ni un bucle activo en reposo.

La rotación/perspectiva y profundidad de CoverFlow se derivan del índice de desplazamiento interpolado. Las tarjetas usan transformaciones retenidas. Llegar a 144/165 FPS reales exige medir en el monitor y driver de destino; el uso de CompositionTarget.Rendering no promete por sí solo esa frecuencia.

Las transiciones de wallpaper siguen usando Skia/WGL detrás de los iconos, con un reloj compartido y limpieza al finalizar. Se actualizaron los filtros a High y se respetan los cambios en el toggle Gaming.

## Configuración transaccional

La GUI XAML incluye controles oscuros propios, carpeta, atajo, diseño, transición, duración y tres toggles. F1/engranaje abre un modal sobre el overlay. La bandeja abre la misma GUI. No se ejecuta Bloc de Notas.

Los cambios se guardan de inmediato; el slider agrupa eventos durante 180 ms y vacía cualquier cambio pendiente al cerrar. Se valida la carpeta, enums, duración y combinación. El nuevo hotkey se registra con otro ID antes de liberar el anterior. Durante captura de teclas, se suspende temporalmente el registro para poder capturar también la combinación actual; al salir se restaura o se informa un conflicto. Si el atajo inicial falla, la bandeja sigue funcionando para corregirlo.

`ConfigurationController` coordina registro del atajo, autoinicio y JSON. Si falla la escritura del archivo, restaura el hotkey y el valor anterior de Run. `IStartupRegistration` permite probar la GUI sin tocar el registro real. La implementación real solo maneja el valor HexaWall en HKCU, usando la ruta actual del ejecutable entre comillas.

El estado de autoinicio se lee del registro al arrancar, en lugar de forzar un valor JSON antiguo. Cambiar una preferencia no modifica el registro si el toggle de autoinicio no cambia. El detector de pantalla completa excluye ventanas del propio proceso antes de consultar el estado del shell, evitando clasificar el overlay como juego.

## Iconos y ciclo de vida

ICO siempre es un contenedor de imágenes raster. IconFactory dibuja la misma geometría vectorial en ocho resoluciones (16,20,24,32,48,64,128,256), crea un ICO multirresolución en memoria y clona el Icon antes de cerrar el stream. No deja HICON sin liberar. Se asigna a NotifyIcon y a las ventanas; el mismo recurso está incrustado en el ejecutable y el instalador.

El menú usa ToolStripProfessionalRenderer y DarkColorTable, con fondo #181A20, hover #252A34, acento #2CE8C6 y texto blanco. Las imágenes y fuentes GDI del menú se liberan junto con la bandeja. El overlay se reutiliza con Hide/Show; Close real se reserva para Dispose. Los eventos, caches retiradas, tareas de decodificación y render subscriptions se limpian al salir.

## Fuentes de plataforma

- [Transparencia de Window en WPF](https://learn.microsoft.com/dotnet/api/system.windows.window.allowstransparency)
- [Teclado WPF dentro de WinForms](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.integration.elementhost.enablemodelesskeyboardinterop)
- [Interoperabilidad de entrada](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/windows-forms-and-wpf-interoperability-input-architecture)
- [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
- [SHQueryUserNotificationState](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shqueryusernotificationstate)
