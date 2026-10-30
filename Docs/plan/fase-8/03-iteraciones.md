# 03 · Iteraciones — de «postal» a «travesura de cajón»

> Las tres iteraciones parten de la misma base (pilares de `01`, tokens actuales, Lilita One) y solo varían **cómo entra la cámara** y **cuánto folclore andino** se ve. Maquetas comparables en `travesura-de-cajon.html`.
> Proceso: I1 → autocrítica → I2 → **crítica del experto UX/UI** (agente Opus, revisó I1 e I2) → I3.

## Iteración 1 — «Postal del campamento»

**Concepto**: el zorro es un turista con una **cámara instantánea** (tipo polaroid). Lo andino entra como **recuerdo de viaje**: el HUD lleva bandas de aguayo y cada foto sale como una postal con marco tejido.

| Pieza | Decisión |
| --- | --- |
| Cámara | Instantánea de plástico color crema; la foto sale por abajo y se agita para revelarla |
| Visor | Rectángulo con esquinas redondeadas y un indicador LED rojo |
| HUD campista | Notas de papel de siempre sobre una **banda de aguayo de 5 colores** a lo ancho, arriba |
| HUD gnomo | Igual, con banda azul |
| Congelado | Gnomo gris (tinte actual) dentro de un marco de postal |
| Menú | Ilustración actual con un aguayo extendido como mantel bajo los botones |
| Iconos | Planos, sin contorno, estilo «sticker» |

### Autocrítica (por qué no basta)
- **Invasiva**: la banda de 5 colores a lo ancho es lo más saturado de la pantalla. Compite con la fogata y con los colores de rol, y rompe la regla de «detalle, no disfraz».
- **Mirada de turista**: lo andino como souvenir (postal, mantel) es justo el estereotipo que hay que evitar.
- **La cámara no es nuestra**: plástico y LED contradicen el pilar 1 (materiales que se tocan, de campamento). Una polaroid es genérica; podría estar en cualquier juego.
- **Los iconos sin contorno** rompen el pilar 2.
- **El congelado sigue siendo gris**: la cámara no cambió el estado.
- **Lo bueno**: el álbum de fotos como premio y como gag funciona; la foto que se revela da un buen ritmo (disparo → espera corta → recompensa).

## Iteración 2 — «Retablo de madera»

**Concepto**: corrige I1 desde el oficio. La cámara pasa a ser **de placas sobre fuelle**, como la de un estudio de los años veinte (Martín Chambi usaba placas de 18 × 24 cm **[Ref]**). Lo andino entra por el **retablo ayacuchano**: cada panel es una caja con puertas pintadas, y el gnomo congelado se vuelve **figurilla de retablo**.

| Pieza | Decisión |
| --- | --- |
| Cámara | Madera y fuelle de cuero; placa de vidrio que se inserta; flash de magnesio en bandeja |
| Visor | Vidrio esmerilado con cuadrícula y **imagen invertida** (como en las cámaras de placas) |
| HUD campista | Cada pieza (objetivos, placas, anunciador) es una **cajita de retablo** con puertas abiertas pintadas de flores |
| HUD gnomo | Igual, con flores frías |
| Congelado | **Figurilla de yeso pintada** (shader `plaster` + colores desteñidos); el HUD se cierra como un retablo y queda «E · Sacúdete» |
| Recurso | 3 placas de vidrio; recarga lenta |
| Menú | Gran retablo: las puertas se abren y dentro está la ilustración en dos pisos (campamento arriba, bosque abajo) |
| Iconos | Con contorno, pintados como figurillas |
| Sellos | **Cuadrados tipo tocapu** para ✓ / ✗ / congelado |

### Mejoras sobre I1
Lo andino aparece como **oficio** y no como souvenir. La cámara es de madera (pilar 1). El congelado por fin cambia de material y cuenta un chiste (pilar 2). Los sellos cuadrados se leen bien.

### Crítica del experto (agente Opus, resumen)
**Veredicto**: volver a madera, papel y contorno es acertado, y la figurilla de yeso es la mejor idea de la fase. Pero I2 pone el decorado por delante de la puntería.
- **[Alta] Visor invertido**: en primera persona con ratón equivale a invertir el control. No se puede apuntar y marea. El esmerilado borra a un gnomo de pocos píxeles a 15–20 m.
- **[Alta] El retablo que se cierra sobre el HUD congelado** quita reloj, objetivos y compañeros durante 14–26 pulsaciones: castigo sin agencia. Contradice el «layout fijo».
- **[Alta] Carga visual**: cajitas con puertas en cada pieza del HUD duplican el ancho. A 720p las flores se vuelven grano. Los sellos tocapu rellenos compiten con el ✓/✗ a 32 px.
- **[Media] Producibilidad**: las puertas pintadas no admiten 9-slice; harían falta sprites por tamaño, bando y estado.
- **[Media] Cultural**: el retablo nace de un altar portátil (cajón de San Marcos); meter enemigos dentro roza la regla 2. Además mezcla Ayacucho, Cochas, aguayo boliviano y tocapu inca.
- **Sobre I1**: una banda en sitio fijo se lee mejor que la decoración pieza a pieza; el fallo era la saturación, no el sitio. El LED rojo («te están grabando») era la mejor señal de amenaza para el gnomo. «Agitar la foto» le roba al gnomo su verbo «Sacúdete».
- **Sobre la identidad (02)**:
  - **[Alta]** Un «zorro turista» que fotografía a seres andinos y los convierte en objetos es justo la mirada colonial que queríamos evitar.
  - **[Alta]** El visor tiene que dibujar el cono real; si no, promete lo que la autoridad no acepta.
  - **[Alta]** El gnomo congelado no puede ver la pantalla congelada: necesita ver venir al compañero que lo rescata, y la figurilla debe conservar el color del gorro.
  - **[Media]** Faltaban estados (sin placas, tapado, inmune…). La cinta «del bosque» era cálida en dos de sus tres franjas. Faltaba un límite de destellos por segundo.
  - **[Baja]** Un contraste mal calculado y un porcentaje que no se puede medir.

**Todo esto ya está corregido en `02-identidad-visual.md`** y aplicado en I3.

## Iteración 3 — «Travesura de cajón» (recomendada)

**Concepto**: la identidad de siempre, sin tocar su núcleo (madera, papel, contorno, Lilita One, colores de rol). La cámara entra como **objeto de oficio local**: el zorro es un pícaro del pueblo con su cámara de cajón de madera. Lo andino entra en **tres detalles con sitio fijo**.

| Pieza | Decisión | Viene de |
| --- | --- | --- |
| Personaje | Zorro del pueblo, fotógrafo de plaza (no turista) | Crítica |
| Cámara | Cajón de madera con polvo de flash; sube al ojo en 250 ms; levantarla no gasta, su coste es la lentitud | I2 (madera) |
| Visor | Esquinas de madera **sobre el cono real**; sin inversión ni desenfoque; corchetes por estado (fuera de alcance, tapado o inmune, enfocando, listo) | Crítica |
| Disparo | Variante B con clic guardado; ≥ 0,4 s entre disparos; A como opción de playtest | Crítica |
| Recurso | 3 placas con recarga | I2 |
| Premio | Álbum: miniatura sepia de cada foto | I1 |
| Amenaza para el gnomo | 3 niveles (en cuadro, enfocado, disparo) y **brillo de lente** visible en el mundo | Crítica + LED de I1 |
| Congelado | Figurilla `plaster` con gorro al 50 %; **vista en vivo y HUD intacto**; solo borde sepia + «E · Sacúdete» con anillo | I2 + crítica |
| Cinta tejida | Fría (`roleGnome`, teal, crema) y en 3 sitios: cabecera del HUD gnomo, revelación de rol, final | I1 (sitio fijo) + crítica |
| Sello cuadrado | Geometría solo en el marco, interior liso | I2 + crítica |
| Retablo | Solo menú (puertas que se abren una vez, 600 ms) y final (dos pisos: campamento arriba, bosque abajo) | I2 |
| Esquina escalonada | En piezas del bando del bosque (botón «Listo», fila de gnomo en la sala) | Nuevo |
| Variante de personaje | Orejeras de chullo opcionales en el gorro de un gnomo | Nuevo |

### Ventajas
- Se reconoce como **nuestro juego** a primera vista: nada del núcleo cambia.
- La cámara cuenta la mecánica: levantar, enfocar, disparar y revelar tienen cada uno su imagen y su sonido.
- La puntería se lee y el visor coincide con lo que acepta la autoridad.
- Lo andino se nota sin invadir. Son tres detalles con sitio fijo, más el oficio: la cámara de cajón y la figurilla de retablo.
- Producible en uGUI: 9-slice, sprites por estado, un shader `plaster` y una captura a `RenderTexture` por foto.

### Desventajas / riesgos
- El álbum y la figurilla necesitan arte y un shader nuevos.
- Las placas cambian el balance y hay que medirlas en playtest.
- Falta la revisión cultural y la decisión del origen regional.

## Comparación

| Criterio (1–5) | I1 Postal | I2 Retablo | I3 Travesura de cajón |
| --- | --- | --- | --- |
| Coherencia con nuestra identidad | 3 | 3 | 5 |
| Legibilidad y usabilidad (1080p y 720p) | 3 | 1 | 4 |
| Fantasía de cámara y game feel | 3 | 3 | 5 |
| Identidad andina sin invadir | 1 | 3 | 4 |
| Respeto cultural | 1 | 2 | 4 |
| Originalidad | 2 | 4 | 4 |
| Producibilidad (uGUI, equipo pequeño) | 4 | 1 | 3 |
| **Total** | **17** | **17** | **29** |

I1 e I2 empatan con perfiles opuestos: I1 se produce fácil pero es genérica e invasiva; I2 tiene identidad pero no se puede jugar ni producir. **I3 toma lo bueno de cada una**: de I1, el álbum, la banda en sitio fijo y la señal de «te graban»; de I2, la madera, la figurilla y el retablo en momentos grandes. Corrige además los fallos que señaló el experto.

## Decisiones para el equipo
1. ¿Variante A o B de disparo para el primer playtest? (recomendada: B, con A como opción).
2. Origen regional (propuesta: sierra sur y central del Perú) y quién hace la revisión cultural.
3. ¿Orejeras de chullo en un gnomo o en ninguno?
4. ¿Quién dibuja los iconos y la figurilla?
