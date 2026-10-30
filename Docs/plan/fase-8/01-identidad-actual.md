# 01 · La identidad que ya tenemos (29 oct 2026)

> Fase 8 sustituye a la exploración dark fantasy de la fase 7 (descartada por el equipo: se alejaba de nuestro tono). Aquí no se inventa un estilo: se **extrae** el que ya existe en las piezas del equipo para que todo lo nuevo salga de él.

Piezas analizadas: logo (`Sprites/UI/GnomeLogo.PNG`), ilustración del menú (`BG/MainMenuBG.png`), botón (`ButtonBG`), papel (`tareas-bg`), placa (`InputHeader`), marco (`Credits`/`Frame`), sellos (`Stamp`, `StampFail`), texturas de suelo en acuarela (`Sprites/Acuarela_*`), modelos `campista` y `gnomo1/2`, tokens de `UiTheme` y la UI aplicada en la fase 6 («Campamento de madera y papel»).

## 1. Qué contamos
**Una travesura de bosque contada como caricatura.** Un zorro campista gruñón intenta pasar la noche (leños, fósforos, fogata, salchicha) y tres gnomos de jardín, pequeños y descarados, se lo sabotean. Nadie muere: se tropieza, se roba, se congela en el sitio y se escapa. El conflicto es **campamento contra bosque**, y el tono es **comedia física**, no miedo.

La ilustración del menú lo resume: el zorro con un tronco al hombro y cara de enfado, un gnomo colgando de su mano, otro asomando tras él y un tercero en primer plano, de espaldas, con las mejillas sonrojadas. Persecución, no terror.

## 2. Cómo lo contamos (rasgos observados)
| Rasgo | Dónde se ve | Qué comunica |
| --- | --- | --- |
| **Letras talladas en madera** con veta, bordes irregulares y hojas brotando | Logo | Hecho a mano; el bosque crece sobre lo del campamento |
| **Contorno grueso y oscuro** alrededor de todo | Logo, personajes, botones | Caricatura legible; se lee a cualquier tamaño |
| **Volúmenes redondos y chatos**, cabezas grandes, mejillas sonrojadas | Zorro, gnomos | Ternura, juguete; permite humor físico sin crueldad |
| **Cálidos saturados** (naranja, terracota, crema) | Logo, zorro, botón, placa | Campamento, fuego, comida, nosotros |
| **Fríos de bosque** (verde azulado, verde hoja, sombra teal) | Fondo del menú, acuarelas de suelo | El territorio de los gnomos |
| **Gorros de color** (rojo, amarillo, morado) y barbas blancas | Gnomos, logo (gorro sobre la G) | Cada gnomo es un personaje; el gorro es su firma |
| **Materiales de campamento**: tabla, papel rasgado, sello de tinta | `ButtonBG`, `tareas-bg`, `Stamp` | La interfaz es «lo que llevas en la mochila» |
| **Acuarela suave en el suelo, cel shading en personajes** | Texturas `Acuarela_*`, menú | Fondo pintado, figuras recortadas encima |

## 3. Pilares extraídos
1. **«Hecho en el campamento»**: toda pieza parece fabricada con lo que hay a mano —madera, papel, cordel, tinta de sello—. Imperfecta, gruesa, con veta.
2. **«Caricatura con contorno»**: formas redondas, contorno oscuro grueso, expresiones exageradas, contraste alto. Se lee primero la silueta, luego el color.
3. **«Campamento contra bosque»**: dos bandos de color y material. Campamento = cálido, madera, fuego. Bosque = fríos verdes, hojas que brotan y gorros de color. El bosque **invade** lo del campamento (las hojas del logo).

Lo que el juego tiene y la identidad **todavía no cuenta**: el poder del campista (hoy una «mirada» abstracta con viñeta morada), el estado petrificado (un tinte gris), la noche, la jaula, y cualquier rasgo de **origen**: nada dice de dónde viene este bosque.

## 4. Qué cumple y qué no (estado de la UI tras la fase 6)
| Pieza | Pilar 1 | Pilar 2 | Pilar 3 | Nota |
| --- | --- | --- | --- | --- |
| Logo | ✓ | ✓ | ✓ | La referencia; no se toca |
| Botón de tabla, placa, papel, sello | ✓ | ~ (contorno fino) | ✗ (todo es campamento) | Base correcta; falta el bando del bosque |
| Fuente Lilita One | ✓ | ✓ | — | Se queda; falta una fuente de cuerpo para texto pequeño |
| HUD (notas, anunciador, retícula) | ✓ | ~ | ✗ | Sin iconos; todo texto |
| Viñetas de mirada (morada, azul, gris) | ✗ | ✗ | ✗ | Meta-UI genérica; se sustituye con la cámara |
| Barra de energía | ✗ | ✗ | ✗ | Barra plana; se sustituye por placas de cámara |
| Tinte de piedra del gnomo | ✗ | ~ | ✗ | Gris plano; se convierte en «figurilla» |
| Nombres sobre la cabeza, pings | ✗ | ✗ | ~ | Fuera de estilo |
| Iconos | — | — | — | No existen |

## 5. Lo que se conserva sí o sí
Logo, ilustración del menú, Lilita One, tokens cálidos (`wood*`, `paper`, `ink`, `cream`, `ember`), colores de rol (`roleCamper` #FFB547, `roleGnome` #3D6FA8), sellos ✓/✗, la placa elástica y el layout del HUD de C6.2a. Todo lo nuevo se **añade** a esto; nada lo sustituye salvo lo marcado ✗ en las tres columnas.
