# Propuesta de interfaz — «Campamento de madera y papel» (29 oct 2026)

Basada en lo que ya tiene el equipo: el logo (letras de madera con hojas y gorro), `ButtonBG` (tabla de madera naranja), `tareas-bg` (papel crema rasgado), `InputHeader` (placa terracota), `Credits` (marco de madera con interior crema), `MainMenuBG` (ilustración del bosque) y la fuente Black Han Sans. El panel «Tu plan» del HUD es la referencia: es lo único que ya sigue este estilo; ajustes, encuesta, sala, partidas públicas, tarjeta de rol y pantalla final usan negro/blanco genérico con Liberation Sans y se rehacen.

## Paleta (tokens en `UiTheme`)
| Token | Hex | Uso |
| --- | --- | --- |
| `woodDark` | #A8431A | bordes, sombra de botones, pista de sliders |
| `wood` | #C2622F | botones, cabeceras |
| `woodLight` | #EFB577 | relleno de botón, filas resaltadas |
| `paper` | #F9EAC8 | paneles, tarjetas, campos de texto |
| `ink` | #5E2C12 | texto sobre papel o madera clara |
| `cream` | #FFF4DE | texto sobre madera o sobre el juego (con contorno `ink`) |
| `leaf` | #6BA539 | confirmación, «listo», opción elegida, gnomos |
| `hat` | #B8423A | peligro, salir, campista |
| `ember` | #F6C343 | foco, temporizador, relleno de sliders |
| `shade` | #2B1A10 al 55 % | fondo detrás de los modales |

## Tipografía
- Títulos y botones: Black Han Sans, `cream` con contorno `ink` 0,25 (o `ink` sobre papel). Tamaños a 1080p: título 64, subtítulo 40, botón 34, cuerpo 28, ayuda 22.
- **Problema**: Black Han Sans no trae bien las tildes ni «¿» (salen de otra fuente, más finas: «Leños», «fósforos», «públicas»). Propuesta: **Lilita One** (OFL, Google Fonts, mismo aire de letra gruesa de madera) para títulos/botones y **Fredoka SemiBold** (OFL) para el cuerpo. Hasta tener la fuente, `UiTheme` guarda la fuente en un solo campo y cambiarla es una línea.

## Piezas
- **Panel**: papel `tareas-bg` (9-slice) con sombra suave; modales grandes con el marco de `Credits` (9-slice) y fondo `shade` a pantalla completa.
- **Cabecera**: placa `InputHeader` con el título en `cream`, sobresaliendo del borde superior del panel (como «Tu plan»).
- **Botón**: tabla `ButtonBG` (9-slice). Normal blanco, encima 1,08 de brillo y escala 1,04, pulsado 0,85 y escala 0,97, desactivado gris al 50 %. Variantes de color por tinte: primario madera, confirmar `leaf`, salir `hat`.
- **Opción elegida** (encuesta, micrófono): el botón elegido pasa a `leaf` con texto `cream`.
- **Slider**: pista `woodDark` redondeada, relleno `ember`, mango redondo de papel con borde `woodDark`.
- **Campo de texto**: papel con borde `woodDark`, texto `ink`, sugerencia `ink` al 45 %.
- **Lista de salas**: filas de papel alternando `paper`/`woodLight` al 35 %.
- **Fondos de menú**: `MainMenuBG` con viñeta oscura; sala de espera igual (hoy negro plano).

## Textos que se corrigen
«Waiting to start match...» → «Esperando a que empiece la partida…»; valores de ejemplo de la escena («3:00», «Leños 0/6») que se ven un instante antes de que el juego los actualice.

## Estado (29 oct 2026)
Aplicada con `GnoMore/Aplicar estilo de UI` (idempotente: segunda pasada 0 cambios; `Revisar escalado de UI` sin elementos fuera). Menú principal, conexión, partidas públicas, créditos (con fondo atenuado y «Volver»), ajustes (etiquetas largas se encogen), sala de espera (fondo del bosque, textos crema con contorno), HUD (tarjeta de rol en marco, aviso de piedra, caja de interacción redondeada, barra de energía), encuesta y pantalla final. Marco nuevo `Frame.png` generado (no se reutiliza `Credits.png`, que trae los nombres pintados) y `ButtonTint.png` (botón neutro para los tintes verde/rojo).
**Fuente** (29 oct): Lilita One (OFL, `Fonts/LilitaOne`, con `OFL.txt`) en títulos y cuerpo, asset TMP dinámico con todo el español precargado; todos los textos que usaban Black Han Sans pasados a Lilita. Fredoka (OFL, `Fonts/Fredoka`) queda descargada pero sin usar: el archivo es variable y TMP sólo toma su peso fino, que se lee mal sobre el juego.
