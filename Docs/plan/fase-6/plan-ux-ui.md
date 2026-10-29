# Plan de diseño UX/UI — GnoMore Campers (v2, 29 oct 2026)

> Objetivo: que la interfaz se vea **profesional** y que cada cosa esté donde está por una razón. Sustituye a `UI-estilo.md`. v2 incorpora la revisión del director UX/UI (agente `.claude/agents/ux-ui-critico.md`, Opus): §12 recoge cada objeción y la decisión.

## 0. Diagnóstico

Inventario: 24 superficies de UI (HUD, mundo 3D, sala, menús, final, encuesta). Problemas de fondo:

| # | Problema | Ejemplo real | Causa |
| --- | --- | --- | --- |
| P1 | Contraste roto en runtime | Líneas de objetivos en blanco/verde claro sobre papel (blanco/papel = **1,19:1**; mínimo 4,5:1) | `MatchHUD` pinta por código colores pensados para fondo oscuro |
| P2 | Contenedores fijos para textos variables | «Hito 1 · Sabotea al campista» (~420 px) en una placa de 240 px | Sin autoajuste |
| P3 | Sin color semántico | Verde = hecho, listo y «segundo aire»; rojo = fallo, campista y últimos 10 s | Cada script elige su color (`StartMatchUI` #FFB84D/#7CE38B/#9AA0A6, «MVP» #FFD94D…) |
| P4 | Información repetida | Objetivo en tarjeta de rol + objetivos + controles; «Leños 0/6» en objetivos y prompt | Sin jerarquía global |
| P5 | Texto sobre la escena sin protección | Anunciador blanco/amarillo sobre cielo o fuego | Sin placa ni contorno |
| P6 | Tipografías y tamaños mezclados | Nombres y pings en Liberation Sans; de 20 a 100 pt sin escala | Sin escala tipográfica |
| P7 | Estados sin feedback | Rol invisible tras 3 s, puntos sólo al final, desconexión sin aviso | Sin matriz de estados |
| P8 | Debug y textos de muestra visibles | «Sala», «MIC OFF», «0/0», «Connecting...» | Builders con textos de ejemplo |

## 1. Dirección artística: «Cuaderno de campamento»

Toda la interfaz es **material de un campamento de exploradores**: notas de papel con chincheta, placas de madera tallada, sellos de tinta, insignias de tela, hojas. Sale del logo (letras de madera con brotes) y de la ilustración del menú. Dos capas:

1. **Meta-UI (menús, sala, final)**: rica e ilustrada; no hay presión. Referencias de tono: Animal Crossing NH (paneles redondeados, aire, tipografía redonda), A Short Hike (pocos elementos y temáticos).
2. **HUD en partida**: **mínimo y legible en 200 ms**, mismo lenguaje reducido a las esquinas, alto contraste, sin decoración que compita con la escena. Referencias: Lethal Company / A Short Hike (HUD casi ausente en primera persona), Among Us (lista compacta arriba-izquierda con progreso), Dead by Daylight (HUD distinto por rol y estado de compañeros a la vista).

Taxonomía (Fagerholt y Lorentzon, 2009): **no diegética** para objetivos y reloj, **espacial** para lo que vive en el mundo (pings, iconos de objetos, nombres), **meta** para el peligro (viñeta, polvo de piedra), **diegética** donde ya existe (fogata, cono de mirada). Regla: si algo tiene posición en el mundo, se muestra en el mundo.

## 2. Principios

1. **Una sola fuente por dato**; los demás sitios sólo la señalan.
2. **Tres niveles**: (A) *ahora* — acción en < 1 s: centro, grande; (B) *pronto* — objetivo, reloj, equipo: esquinas superiores, estable; (C) *referencia* — controles, voz: esquinas inferiores, pequeño, se desvanece.
3. **Asimetría explícita**: cada rol ve su HUD (como DbD), con su color de rol.
4. **Color nunca solo**: todo estado lleva forma o icono (Game Accessibility Guidelines; XAG 102).
5. **Texto sobre escena siempre con placa** (papel o pastilla oscura) **+ contorno fino**; la sombra sola no basta (XAG 102 recomienda placa con opacidad ajustable).
6. **Contraste medido en el peor caso**: texto ≥ 4,5:1; texto grande (≥ 36 px en PC a 1080p) y elementos gráficos ≥ 3:1; se mide contra la zona de menor contraste del fondo (XAG 102).
7. **Tamaño**: cuerpo de HUD con **altura de cuerpo ≥ 26 px** medida en captura a 1080p (XAG pide 18 en PC; subimos por leerse sobre escena en movimiento); títulos ≥ 40 px.
8. **Zona segura**: 64 px laterales y 40 px arriba/abajo a 1080p.
9. **Contenedores elásticos**: toda placa y nota crece con su texto y tiene ancho máximo con salto de línea.
10. **Movimiento con propósito**: entra lo nuevo, se sella lo hecho, tiembla lo urgente.

## 3. Paleta semántica

Cada familia de color tiene **un solo significado**: verde = confirmar/listo; rojo = peligro o acción destructiva; ámbar = foco/progreso; los colores de rol no se usan para estados. Ratios WCAG; las placas semitransparentes en peor caso compuesto (sobre blanco).

| Token | Hex | Uso | Par · ratio |
| --- | --- | --- | --- |
| `paper` | #F9EAC8 | Notas, tarjetas, modales | `ink` 9,56 ✓ |
| `ink` | #5E2C12 | Texto sobre papel/madera clara; **sello ✓ de «hecho»** | — |
| `inkSoft` | #7A4A2E | Secundario y placeholders sobre papel | 6,19 ✓ |
| `woodDark` | #A8431A | Placas, bordes, pista de medidores | `cream` 5,53 ✓ |
| `wood` | #C2622F | Sólo superficie (nunca fondo de texto < 36 px) | `cream` 3,79 (sólo grande) |
| `woodLight` | #EFB577 | Botón neutro | `ink` 6,26 ✓ |
| `cream` | #FFF4DE | Texto sobre madera u oscuro | — |
| `night80` | #2B1A10 al 80 % | Pastilla del prompt y toasts | `cream` 8,06 peor caso ✓ |
| `leafDark` | #3F7A22 | **Un** botón primario por pantalla; chip «Listo» | `cream` 4,79 ✓ (el #6BA539 actual: 2,72 ✗) |
| `hatDark` | #9E2F28 | Peligro, fallo, «Salir del juego», «Abandonar» | `cream` 6,66 ✓ |
| `ember` | #F6C343 | Relleno de medidores y foco, siempre sobre pista `woodDark` con contorno `ink` 2 px | sobre `paper` sólo 1,38: nunca solo sobre papel |
| **Rol campista** | #FFB547 (luz de fogata, claro) | Insignia, retrato y bordes del HUD campista; siempre con icono de fogata | sobre `night` 9,50 ✓; `ink` encima 6,48 ✓ |
| **Rol gnomos** | #3D6FA8 (azul bosque nocturno, oscuro) | Insignia, retratos y bordes del HUD gnomo; siempre con icono de gorro | `cream` encima 4,77 ✓; sobre `night` 3,21 (gráfico ✓) |

Por qué: los roles tienen **luminancia opuesta** (claro/oscuro, 2,96:1 entre sí) y tono naranja/azul, la pareja más segura en deuteranopía y protanopía, y nunca van sin icono. El «hecho» de los objetivos es un **sello de tinta**, así el verde queda sólo para confirmar. El ámbar claro del campista no se confunde con la madera de la UI.

## 4. Tipografía

| Nivel | Fuente | Tamaño 1080p | Uso |
| --- | --- | --- | --- |
| Display | Lilita One | 84–110 px | Tarjeta de rol, final, cuenta atrás |
| Título | Lilita One | 44–56 px | Títulos de modal, placas |
| Subtítulo / botón | Lilita One | 32–36 px | Botones, hito actual |
| Cuerpo | **Atkinson Hyperlegible Bold** | 26–30 px (altura de cuerpo ≥ 26 px) | Objetivos, prompts, listas, ajustes |
| Datos | Atkinson Hyperlegible Bold | 40–84 px | Código de sala, temporizador, puntos |

Por qué: Lilita da la personalidad del logo; Atkinson Hyperlegible (OFL, Braille Institute, diseñada para baja visión) distingue 0/O e 1/l/I —clave en el código de sala y los contadores— y existe en TTF **estático** Bold en google/fonts (Nunito sólo como variable: TMP tomaría su peso fino). Mayúsculas sostenidas sólo en placas de 1–2 palabras; frases en *sentence case* (XAG 101).

## 5. Sistema de componentes

| Componente | Aspecto | Dónde y por qué |
| --- | --- | --- |
| **Nota** | `paper` rasgado con chincheta y sombra; rota ±1,5° **sólo el sprite**, nunca el texto | Objetivos y pistas: «lo que tengo apuntado» |
| **Placa** | `woodDark`, texto `cream`, **ancho automático** con 24 px de padding | Cabecera de nota y de modal |
| **Tarjeta** | Marco de madera + papel (`Frame.png`) | Modales: ajustes, públicas, encuesta, tarjeta de rol, final |
| **Botón** | Neutro madera clara; **primario** `leafDark` (uno por pantalla); destructivo `hatDark` sólo para salir del juego/abandonar; «Volver/Cerrar» = neutro o ✗ de la tarjeta | 64 px de alto mínimo; hover +4 %, pulsado −3 %, desactivado gris + candado |
| **Pastilla** | `night80`, texto `cream` | Todo lo que va sobre la escena en el centro |
| **Sello** | Círculo de tinta `ink` ✓ / `hatDark` ✗ que «cae» | Objetivo cumplido/fallido, opción elegida (encuesta), «Listo» (sala) |
| **Insignia de rol** | Escudo de tela con icono (fogata / gorro) en color de rol | Arriba-derecha siempre: responde «¿qué soy?» |
| **Medidor** | Pista `woodDark`, relleno `ember`, contorno `ink`, marca de umbral | Energía de mirada, vertido, liberarse |
| **Toast** | Pastilla grande con icono a la izquierda | Eventos de partida; cola de 3; bajo el reloj |
| **Glifo de tecla** | Sprite TMP generado del binding real (`GetBindingDisplayString`), escala con el texto, teclado y mando | Sustituye «[E]» en prompts y controles |
| **Iconos** | Set de trazo grueso redondeado, 2 tonos (`ink` + acento) | Leño, fósforo, salchicha, cubeta, fogata, ojo, piedra, gnomo, campista, micro, ping |

## 6. Distribución por pantalla (1920×1080, márgenes 64/40)

### HUD del gnomo
```
┌───────────────────────────────────────────────────────────────┐
│ [Placa: Hito 1 · Sabotea]        ☀━━━━━○━━━━━ 7:42    [🛡 Gnomo]│
│ ┌ Nota ─────────────────┐       ┌ toast: ¡Fuego! ─┐             │
│ │(✓) Roba leños y fósforos│                        [Ana   libre] │
│ │( ) Escóndelos       2 │                        [Leo  piedra]  │
│ │( ) Leños del campista 3/5│                     [Teo  jaula]   │
│ └───────────────────────┘                                        │
│                               ·  (retícula)                      │
│                       ╭ (E) Esconder leño ╮                      │
│ (micro)                                       (E) usar · (Clic) …│
└───────────────────────────────────────────────────────────────┘
```
- **Arriba-izquierda: nota de objetivos** (B): un solo hito, tareas con sello, el contador dentro de la línea. Donde Among Us y Untitled Goose Game ponen la lista; la lectura empieza ahí.
- **Arriba-centro: reloj día/noche** (B): ritmo común a los dos roles.
- **Arriba-derecha: insignia + compañeros** (B): retratos con estado (libre, piedra, jaula, cargado) como DbD; cooperar exige saber quién necesita ayuda sin hablar.
- **Centro: retícula + pastilla 120 px debajo** (A); **toasts bajo el reloj** (A), lejos del prompt.
- **Abajo-izquierda: voz; abajo-derecha: controles** (C), se desvanecen al usarse.

### Canal A: una sola cosa en el centro
Regla de prioridad: **petrificado/enjaulado > cargado por el campista > aturdido > observado > prompt**. Petrificado es **un** widget: glifo de tecla grande + anillo de progreso alrededor de la retícula (patrón del revive de Apex), sin contador numérico; el prompt se oculta mientras dura.

### Matriz estado × canal (gnomo)
| Estado | A centro | B esquinas | C pie |
| --- | --- | --- | --- |
| Normal | Prompt si hay objeto | Nota, reloj, insignia + compañeros | Controles, voz |
| Llevando objeto | Prompt de soltar/colocar | El objeto aparece en la insignia | — |
| Observado | Viñeta azul pulsante + icono de ojo | Igual | Oculto |
| Petrificado | Anillo de liberarse | Nota al 50 % | Oculto |
| Cargado / enjaulado | «Te llevan» / «En la jaula 0:14» en pastilla | Compañeros resaltan quién puede liberarte | Voz visible |
| Noche / últimos 10 s | Toast único | Reloj en `hatDark` + pulso | — |
| Desconexión / migración | Pastilla «Reconectando…» | — | — |

El campista usa la misma matriz con «Enfocando» (medidor de mirada + anillo de patada junto a la retícula), «Cargando gnomo» y «Fogata atacada» (toast + flecha en el borde hacia la fogata).

### HUD del campista
Mismo esqueleto (se aprende un juego, no dos) con borde e insignia ámbar. Arriba-derecha: **contador de gnomos** (libres / piedra / jaula) con iconos. Abajo-centro: medidor de mirada y anillo de patada junto a la retícula, sus dos recursos de acción. Salchicha: una línea con icono que cambia (cruda → lista → quemada).

### Tarjeta de rol
3 s: ilustración del rol, nombre en display, **una** frase de objetivo y **tres** glifos de control. Luego se encoge animada hasta la insignia de rol: el jugador ve dónde queda su rol.

### Sala de espera
Tarjeta central: código en Atkinson dentro de una placa con botón copiar (icono → «¡Copiado!»); filas de papel con avatar, nombre, insignia de rol si está decidido y sello «Listo»; «Estoy listo (R)» y, para el anfitrión, «Empezar (2/3 listos)» desactivado con candado. Estados: «Conectando…», «Esperando jugadores (1/4)», «Sala llena», «El anfitrión se fue: ahora eres el anfitrión» («Empezar» sólo lo ve el anfitrión actual). Fondo: bosque desenfocado y oscurecido.

### Menú principal
Ilustración + logo. Botones por intención: **Jugar** (Crear, Unirse, Partidas públicas) arriba con un primario; **Opciones** (Ajustes, Créditos) y **Salir** separados.

### Pantalla final
Se **oculta todo el HUD de partida**. Titular personal («¡Ganaste!» / «¡Perdiste!») y el rol ganador como subtítulo con su insignia; **podio** para todos (también el campista), tres estadísticas con icono, y «Revancha en 10» como medidor circular.

### Ajustes, partidas públicas, encuesta
Tarjeta con pestañas cuando crezca (Audio · Controles · Vídeo · Accesibilidad). Filas de 64 px. Partidas: filas alternas con estado como chip + icono. Encuesta: opciones tipo radio con sello al elegir; placeholder en `inkSoft`.

## 7. Movimiento (criterio propio, a validar en playtest)

| Evento | Animación | Duración |
| --- | --- | --- |
| Aparece nota/placa | Desliza 24 px + fade, ease-out | 220 ms |
| Objetivo cumplido | Sello cae (1,4 → 1) | 180 ms |
| Objetivo fallido | Sello ✗ + sacudida 6 px | 300 ms |
| Toast | Pop desde 0,8 + 1,2 s + fade | 200 / 1200 / 250 ms |
| Prompt | Fade sin escala | 120 ms |
| Peligro | Viñeta a 2 Hz mientras dura | continuo |
| Botón | Hover 4 %, pulsado −3 % | 80 ms |

## 8. Accesibilidad (mínimo para Steam)

Escala de UI **80–200 %** (XAG 101; a 200 % la nota trunca con «…» y se despliega con Tab); **alto contraste ≥ 7:1**; opacidad de placas ajustable; preset de daltonismo probado con Color Oracle; pings y retratos con **borde doble** claro+oscuro; opción de anclar el HUD al rectángulo 16:9 en 21:9; ningún dato sólo por color.

## 9. Qué se quita

«0/0» del aviso de piedra; el segundo «[E] usar» de controles; «MIC OFF [M]» en inglés y pegado al borde (→ icono de micro tachado + «Micro apagado» dentro de la zona segura); `Match Time Canvas` (duplica el reloj); objetivo largo de la tarjeta de rol; contadores repetidos en el prompt; textos de muestra de los builders; overlays de debug fuera de builds de desarrollo.

## 10. Pantallas que faltan

Carga/conexión, error de red y reconexión, «el anfitrión se fue / migrando», pausa en partida (Ajustes sin salir), espectador del gnomo enjaulado, y **onboarding por rol en los primeros 60 s**: pistas de una línea en contexto («Recoge un leño (E)») que desaparecen al cumplirse (patrón A Short Hike / Lethal Company).

## 11. Implementación por tarjetas

0. **C6.2a Hotfix (primero)**: placa elástica; líneas de objetivos en `ink`/`inkSoft` + sello; barrido de `<color=#…>` y `new Color(` en `Scripts/UI` con prueba de editor que falle si reaparecen; HUD oculto en el final; prioridad única del canal A.
1. **C6.2 Tokens semánticos** en `UiTheme` y lectura en runtime en todos los scripts.
2. **C6.3 Contenedores elásticos, escala tipográfica y Atkinson Hyperlegible**; escala de UI 80–200 %.
3. **C6.4 Nota de objetivos con sellos** y contadores únicos.
4. **C6.5 Insignia de rol, compañeros / contador de gnomos** y matriz de estados.
5. **C6.6 Toasts, pastilla y glifos de tecla**.
6. **C6.7 Iconos** (según decisión de arte).
7. **C6.8 Sala, menú, final y pantallas que faltan**.
8. **C6.9 Accesibilidad** (alto contraste, daltonismo, opacidad, 16:9 en 21:9).
9. **V2 automático**: las capturas de `Temp/ui` pasan a prueba (contraste por par y `TMP_Text.isTextOverflowing`), y prueba con 1+3 jugadores y late joiner (cada rol ve **sus** tareas).

## 12. Revisión del director UX/UI y decisiones

| Objeción | Decisión |
| --- | --- |
| Verde del gnomo = verde de «hecho» (1,06:1) | **Aceptada**: gnomo azul #3D6FA8; «hecho» = sello de tinta |
| Naranja del campista se pierde en la madera (1,38:1) | **Aceptada**: ámbar claro #FFB547, luminancia opuesta al gnomo, siempre con icono |
| Centro de pantalla saturado | **Aceptada**: prioridad única del canal A y anillo de liberarse |
| El orden no ataca el fallo visto | **Aceptada**: C6.2a hotfix primero + prueba anti colores fijos |
| El gnomo veía tareas del campista en la captura | **A verificar**: en edición es texto de muestra del prefab; V2 con 1+3 jugadores |
| Ratios de v1 mal calculados | **Aceptada**: tabla en peor caso compuesto |
| `ember` sobre papel 1,38 en sliders | **Aceptada**: pista `woodDark` + contorno |
| Rojo en «Volver/Cerrar» | **Aceptada**: rojo sólo destructivo |
| Dos botones verdes iguales | **Aceptada**: un primario por pantalla |
| Escala sólo hasta 150 % | **Aceptada**: 80–200 % |
| Sala sin estados; código en Lilita | **Aceptada**: estados + Atkinson |
| HUD visible en el final; titular redundante | **Aceptada** |
| Rotar texto TMP | **Aceptada**: sólo el sprite |
| Matriz de estados, glifos, pantallas que faltan, onboarding, alto contraste, borde doble, 21:9 | **Aceptadas** (§6, §8, §10) |
| ¿Qué sabe cada rol del otro? | **Pregunta para Bryan** (diseño de juego) |
| ¿Iconos en acuarela a mano o vectoriales? | **Pregunta para Bryan** (arte) |
| ¿Mando / Steam Deck? | **Pregunta para Bryan**: si sí, mínimos de consola y glifos de mando |

## Referencias

Dead by Daylight HUD de estados (deadbydaylight.wiki.gg/wiki/Status_HUD; thegamer.com sobre los cambios de HUD); Overcooked y el trabajo en equipo (superjumpmagazine.com/overcooked-how-design-creates-teamwork); UX de Among Us (uxdesign.cc/from-tasks-to-tricks-the-ux-of-among-us-d469b45dba22); interfaz de Lethal Company (indieklem.substack.com); HUD de The Long Dark (gamedeveloper.com/design/long-dark-survival-game-hud-ui); taxonomía de Fagerholt y Lorentzon (gamedeveloper.com/design/user-interface-design-in-video-games); WCAG 2.2 contraste (w3.org/WAI/WCAG22/Understanding/contrast-minimum.html); Xbox Accessibility Guidelines 101 y 102 (learn.microsoft.com/gaming/accessibility/xbox-accessibility-guidelines); zonas seguras de Xbox (devdocs.xbox.com/build/core-features/graphics/overviews/screen-areas); Game Accessibility Guidelines (gameaccessibilityguidelines.com/full-list); Color Oracle (colororacle.org); Game UI Database (gameuidatabase.com). Varias fuentes de juegos son wikis o reseñas (débiles); los análisis formales de UI de estos juegos casi no existen.
