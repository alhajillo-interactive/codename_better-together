# 02 · Identidad visual — «Travesura de cajón»

> Etiquetas: **[Ref]** dato de una fuente (lista al final) · **[Nuestro]** decisión para GnoMore Campers.
> Punto de partida: los tres pilares de `01-identidad-actual.md`. Añadidos: **la cámara** que sustituye a la mirada y **una capa de folclore andino** en detalles.
> Este documento recoge el **resultado**. Las dosis (cuánto andino, qué cámara, dónde va el retablo) salieron de las tres iteraciones de `03-iteraciones.md`.

## 1. La idea en una frase
**[Nuestro]** Un zorro del pueblo, pícaro y fotógrafo de plaza (no un turista: el zorro, *atoq*, ya es un personaje del folclore quechua), acampa con su **cámara de cajón de madera** en un bosque andino lleno de gnomos traviesos. Cada foto deja a un gnomo **convertido en figurilla pintada**, como las de un retablo, hasta que se sacude y escapa. La interfaz está hecha con lo que hay en el campamento: tabla, papel, cordel, sello. El bosque la va **bordando** con detalles andinos: una cinta tejida, una esquina escalonada, un sello cuadrado.

## 2. Pilares reimaginados
Mismos tres pilares de nuestra identidad, cada uno con su capa andina. **Lo andino nunca es un cuarto estilo: es una costura dentro de los tres.**

### Pilar 1 · «Hecho en el campamento» → *hecho a mano, con oficio*
- **Se mantiene**: tabla con veta, papel rasgado, sello de tinta, contorno grueso.
- **Capa andina**: la artesanía de oficio. La cámara es **de cajón**, de madera, como la de los fotógrafos de plaza **[Ref]** (se llamaban «de cajón» por la forma de la cámara y «minuteros» por el tiempo de revelado; en La Paz y en Lima siguieron trabajando durante el siglo XX). Las líneas de sombreado de los iconos imitan el **mate burilado** **[Ref]** (calabaza grabada a buril, Cochas, Huancayo): una o dos rayas, no una trama.
- **Regla**: todo material se puede tocar —madera, papel, lana, yeso—. Nada de vidrio, metal pulido ni neón.

### Pilar 2 · «Caricatura con contorno» → *figurilla con contorno*
- **Se mantiene**: formas redondas, contorno oscuro de 6 px a 64 px, expresiones exageradas.
- **Capa andina**: las figuras del **retablo ayacuchano** **[Ref]**, hechas con pasta de papa y yeso y pintadas con colores planos. El gnomo fotografiado no se vuelve «piedra gris»: se vuelve **figurilla de yeso pintada**, rígida y algo desteñida. Encaja con el gnomo de jardín (cerámica), con el retablo y con las **illas y conopas** **[Ref]**, figurillas de piedra que guardan la «provisión» de la casa.
- **Regla**: el estado congelado se ve como un **juguete**, no como una estatua de terror.

### Pilar 3 · «Campamento contra bosque» → *campamento contra bosque andino*
- **Se mantiene**: el campamento es cálido (madera, fuego, `roleCamper`) y el bosque es frío (verdes, teal, `roleGnome`), con gorros de color.
- **Capa andina**: el bosque es un **bosque de neblina** con hojas que brotan. Su firma gráfica es la **cinta tejida** de franjas, tomada de la estructura del **aguayo** **[Ref]**: franjas de grosor alterno, y cada franja decorada (*pallay*) con solo 2 o 3 colores. La cinta es **fría** (`roleGnome`, `weaveTeal`, `cream`) para que de verdad marque al bando del bosque.
- **Regla contable**: la cinta va en **tres sitios** y en ninguno más: la cabecera del HUD del gnomo, la revelación de rol y la pantalla final. Nunca de fondo ni detrás de texto. Donde el bando importa de verdad es en las pantallas compartidas (sala, revelación, final), porque en partida cada jugador solo ve su propio HUD.

## 3. La fantasía de la cámara (sustituye a la mirada)
**[Nuestro]** El juego ya tiene casi todo lo necesario: enfocar reduce el campo de visión, ralentiza el ratón y el paso (×0,5), gasta un recurso, y el gnomo queda bloqueado tras 0,25 s dentro del cono. Lo que cambia es **qué cuenta** cada fase.

| Fase | Hoy (mirada) | Con cámara | Visual del campista | Lo que ve el gnomo |
| --- | --- | --- | --- | --- |
| Guardada | — | Cámara colgada al pecho | HUD normal; icono de cámara con sus placas | Nada |
| **Levantar** (mantener clic derecho) | Zoom de FOV + viñeta morada | La cámara sube al ojo en 250 ms (madera que cruje, el paso se hace pesado). Levantarla no gasta nada; su coste es la lentitud | Marco del **visor**: esquinas de madera **dibujadas sobre el cono real** (22°, más ancho con la remontada y de noche), cuadrícula fina, placas. Sin inversión ni desenfoque | Nivel 1: aviso bajo en el borde, solo si está dentro del cuadro. Todos los gnomos ven en el mundo un **brillo de lente** sobre la cámara del campista |
| **Encuadrar** | Bloqueo en 0,25 s | **Enfoque**: dos corchetes se cierran sobre el gnomo | Corchetes según estado replicado: crema punteado (fuera de alcance), `hat` con aspa (tapado o inmune), crema → `ember` (enfocando → listo) | Nivel 2: destello de lente en el borde, hacia el campista, y un «tic» |
| **Disparar** (clic) | Se petrifica al llenarse | Obturador + **polvo de flash**; solo petrifica si el gnomo está enfocado | Destello cálido (80 ms, ≤ 60 %), «clac», retroceso de cámara | Nivel 3: destello breve y borde sepia |
| **Revelar** | — | La foto «se revela» en el álbum del campista | Miniatura sepia que aparece en 600 ms en la esquina (premio y chiste: la pose del gnomo) | **Vista en vivo y HUD intacto**: solo un borde sepia y **«E · Sacúdete»** con su anillo. Puede ver venir al compañero que lo rescata |
| **Recargar** | Barra de energía | **Placas**: 3 placas; cada disparo gasta una, que se recarga sola | Iconos de placa; recarga con sonido de corredera | — |

**Figurilla**: el cuerpo pasa a `plaster`, pero el gorro conserva su color al 50 %, para que los compañeros sepan a quién rescatar. En su HUD, cada compañero congelado aparece con su color.

**Estados que el arte debe cubrir**: sin placas (clic seco, sin flash, placas vacías) · recargando (la placa se llena) · gnomo fuera de alcance · gnomo tapado · gnomo inmune tras liberarse · cámara bloqueada mientras cargas un objeto · cámara que baja de golpe si te patean · torre de gnomos (se enfoca al de arriba).

Dos variantes de regla para decidir en playtest (las dos usan el mismo arte):
- **A · Obturador automático**: al completarse el enfoque, la foto sale sola (cambio mínimo sobre el código actual).
- **B · Clic para disparar** (recomendada para la fantasía): el enfoque solo prepara; el clic dispara y gasta placa; fallar gasta placa igual. Si se pulsa antes de completar el enfoque, el clic queda guardado y dispara al completarse (sin sensación de retraso). Añade puntería y decisión.
- Las dos con **al menos 0,4 s entre disparos** (nunca más de 3 destellos por segundo, WCAG 2.3.1) y con un ajuste para alternar A y B en la misma build de playtest.
- Pregunta abierta de balance: hoy una sola pulsación encadena congelaciones; con 3 placas y recarga, ¿se mantiene el ritmo? Se mide en playtest.
- Licencia de ficción: las cámaras de cajón trabajaban con luz de día; nuestro zorro les añade **polvo de flash**, que existió en la fotografía de estudio. Se cuenta como invento del personaje.

Red (sin cambio de modelo): el campista sigue siendo autoridad de su bloqueo y del disparo; el dueño del gnomo publica que está congelado (regla 4). La miniatura del álbum es una captura local de una sola cámara a una `RenderTexture` en el momento del disparo, no por frame.
Accesibilidad: el flash nunca pasa del 60 % de opacidad, dura ≤ 100 ms y hay una opción «Reducir destellos».

## 4. Tipografía
- **Lilita One** (se queda): logo de apoyo, títulos, botones, números grandes. 34–64 px a 1080p.
- **Fredoka SemiBold, instancia estática** (ya descargada; la fase 6 falló porque se importó el archivo variable): cuerpo y texto pequeño, 22–28 px.
- Números del reloj y contadores: Lilita One (cifras gruesas y anchas).
- **Prohibido**: fuentes «pseudo-incas» o de escalones decorativos. Lo andino va en los gráficos, nunca en la letra.

## 5. Paleta
Se conservan todos los tokens de `UiTheme`. Se añaden tokens de detalle, que ningún texto usa como fondo:

| Token nuevo | Hex | Uso |
| --- | --- | --- |
| `weaveTeal` | #1E9C8F | Franja de la cinta del bosque, con `roleGnome` y `cream` (máximo 3 colores) |
| `photoSepia` | #E8D5AE | Fondo de foto revelada y borde del estado congelado |
| `photoInk` | #4A3220 | Tinta de la foto (sobre sepia: 8,2:1) |
| `plaster` | #EFE6D6 | Base de la figurilla de yeso (shader del gnomo congelado) |
| `flash` | #FFF8E6 | Destello del obturador |

Proporción por pantalla: 70 % escena · 20 % madera y papel · ≤ 10 % color de estado. Lo andino se dosifica con reglas que se pueden contar (sitios fijos), no con porcentajes.

## 6. Formas y marcos
- Placa de tabla de siempre, con **esquina escalonada** opcional (dos escalones de 8 px) en piezas del bosque. Solo como forma geométrica: no usamos la chakana como adorno.
- **Sello cuadrado** al estilo de un tocapu **[Ref]**: la geometría va solo en el marco y el interior es liso, para que el ✓, el ✗, la figurilla o la jaula se lean a 32 px (unos 21 px reales a 720p). El significado de los tocapus sigue sin descifrarse **[Ref]**, así que no copiamos ningún diseño concreto: los nuestros son propios.
- **Retablo** solo en el menú y en la pantalla final: ilustración estática con dos puertas animadas. Nunca en el HUD ni sobre la pantalla del gnomo congelado.
- Redondo = seguro o recurso; anguloso = peligro (el destello de lente).

## 7. Iconografía
- Rejilla de 64 px, contorno `ink` de 6 px, 2 o 3 colores planos, una o dos rayas de «burilado» como sombra. Se prueba a 32 px.
- **Set mínimo (20)**: cámara, placa, flash, enfoque, álbum · leño, fósforo, salchicha (cruda, lista, quemada), fogata · gnomo, gnomo congelado (figurilla), jaula, patada · campista, micro, ping, sol y luna · teclas (E, Q, clic izquierdo, clic derecho, Espacio).
- Un icono, un significado. El ojo desaparece del juego: la amenaza para el gnomo es **la lente**.

## 8. Movimiento y sonido
- Feedback ≤ 100 ms · transiciones 150–250 ms con rebote corto (caricatura) · eventos: levantar cámara 250 ms, disparo 80 ms de flash + 300 ms de congelado, revelado 600 ms, revelación de rol 2,5 s.
- Sonidos de UI (para el equipo de audio): madera, papel, corredera de placa, obturador mecánico, polvo de flash (*fump*), un tintineo de cerámica al quedar congelado. Como máximo un sonido de UI cada 500 ms.

## 9. Respeto cultural (reglas)
1. **Detalle, no disfraz**: ningún personaje lleva traje típico completo; como mucho, un accesorio (p. ej. orejeras de **chullo** en el gorro de un gnomo, como variante).
2. **Nada sagrado como chiste**: ni la chakana, ni la Pachamama, ni apus en gags. El **Ekeko** y las Alasitas **[Ref]** (feria de miniaturas, Patrimonio Inmaterial de la UNESCO desde 2017) solo como guiño en créditos o en un objeto de fondo, nunca como enemigo.
3. **Cada motivo, con su fuente** en el documento y con crédito en el juego. Que lo revise alguien andino ajeno al equipo **antes de producir el arte**, no al publicar.
4. **Un solo origen regional**, para no mezclar todo lo «andino» en un batido. Propuesta: sierra sur y central del Perú (retablo de Ayacucho, mates de Huancayo, fotografía de Cusco). El tejido se toma de la *lliclla* de esa zona. Decisión del equipo.
5. **El retablo nació del cajón de San Marcos**, un altar portátil **[Ref]**. Por eso dentro solo van figurillas cómicas en pose, nunca escenas que lo parodien.

## Fuentes
- Retablo ayacuchano: [Andina, patrimonio cultural](https://andina.pe/agencia/noticia-retablo-ayacuchano-ya-es-patrimonio-cultural-de-nacion-755491.aspx) · [Andina, historia (Quinua)](https://andina.pe/ingles/noticia-ayacucho-conoce-historia-del-retablo-arte-quinua-es-patrimonio-cultural-807873.aspx) · [peru.info](https://peru.info/es-pe/TALENTO/Noticias/6/24/retablo-ayacuchano--el-arte-andino-que-expresa-pasion)
- Conopas e illas: [Brooklyn Museum, Camelid Conopa](https://brooklynmuseum.org/opencollection/objects/124698) · [BCRP, revista Moneda 203](https://www.bcrp.gob.pe/docs/Publicaciones/Revista-Moneda/moneda-203/moneda-203-12.pdf) · [Sillar, UCL Discovery](https://discovery.ucl.ac.uk/id/eprint/1503884/)
- Aguayo y pallay: [Ciencia Latina, aguayo y chuspa de Vilavila](https://ciencialatina.org/index.php/cienciala/article/view/323) · [Threads of Peru](https://threadsofperu.com/blogs/blog/5-must-knows-about-andean-weaving) · [Wikipedia, Aguayo](https://www.Wikipedia.com/wiki/Aguayo_(cloth))
- Tocapu: [PUCP, tocapus en Guamán Poma](https://repositorio.pucp.edu.pe/index/handle/123456789/113556) · [World History, tejidos incas](https://www.worldhistory.org/trans/es/2-791/tejidos-incas/)
- Mate burilado: [Tesis PUCP, mates de Cochas](https://tesis.pucp.edu.pe/items/dd6dfca2-1292-4f5c-b9f3-0bc96800433e) · [El Peruano](https://www.elperuano.pe/noticia/296053-del-mate-burilado-nace-la-fuerza-de-una-mujer-andina)
- Fotografía andina: [Andina, Martín Chambi](https://andina.pe/agencia/noticia-martin-chambi-memoria-grafica-del-pais-772124.aspx) · [Ciencia y Cultura UCB, fotógrafos de cajón en La Paz](https://cienciaycultura.ucb.edu.bo/a/article/download/313/277/541) · [Antropología Visual, fotografía minutera](https://www.antropologiavisual.cl/sites/default/files/rav_2020_res_01_rodriguez_2.pdf) · [Andina, el fotógrafo de la Plaza de Armas](https://andina.pe/AGENCIA/noticia-suplemento-cronica-universitaria-fotografo-se-resiste-al-olvido-1077655.aspx)
- Alasitas y Ekeko: [UNESCO, Alasita](https://ich.unesco.org/en/RL/ritual-journeys-in-la-paz-during-alasita-01182) · [Wikipedia, Ekeko](https://en.Wikipedia.com/wiki/Ekeko)
- Gigantes de piedra de Viracocha (mito de petrificación, contexto): [World History, Viracocha](https://www.worldhistory.org/trans/es/1-12477/viracocha/) · [Redalyc, Armas Asín](https://www.redalyc.org/pdf/355/35501119.pdf)
