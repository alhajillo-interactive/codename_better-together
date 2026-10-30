# 02 · Identidad visual — «Travesura de cajón» (versión aprobada)

> Decisión del equipo (29 oct 2026): se aprueba I3 **sin la capa andina** (cinta tejida, esquina escalonada, sello tipo tocapu, retablo, chullo). Queda nuestra identidad de siempre más la cámara. Historial de la exploración en `03-iteraciones.md`.

## 1. La idea en una frase
Un zorro campista con una **cámara de cajón de madera** pasa la noche en un bosque lleno de gnomos traviesos. Cada foto deja a un gnomo **convertido en figurilla de jardín**, de yeso pintado, hasta que se sacude y escapa. La interfaz está hecha con lo que hay en el campamento: tabla, papel, cordel y sello.

## 2. Pilares (los de `01-identidad-actual.md`)
1. **Hecho en el campamento**: tabla con veta, papel rasgado, sello de tinta, contorno grueso. Todo material se puede tocar: madera, papel, yeso. La cámara es de madera, del mismo mundo que los botones.
2. **Caricatura con contorno**: formas redondas, contorno oscuro (6 px a 64 px), expresiones exageradas. El gnomo congelado se ve como un **juguete**, no como una estatua de terror.
3. **Campamento contra bosque**: el campamento es cálido (madera, fuego, `roleCamper`); el bosque es frío (verdes, `roleGnome`) con gorros de color.

## 3. La cámara (sustituye a la mirada)
| Fase | Con cámara | Visual del campista | Lo que ve el gnomo |
| --- | --- | --- | --- |
| Guardada | Cámara al pecho | HUD normal; icono de cámara con sus placas | Nada |
| **Levantar** (mantener clic derecho) | Sube al ojo en 250 ms; paso y ratón más lentos. Levantarla no gasta; su coste es la lentitud | **Visor**: esquinas de madera dibujadas **sobre el cono real** (22°, más ancho con la remontada y de noche), cuadrícula fina, placas | Nivel 1: aviso bajo en el borde si está dentro del cuadro. Todos los gnomos ven un **brillo de lente** en la cámara del campista |
| **Enfocar** | Dos corchetes se cierran sobre el gnomo (bloqueo actual de 0,25 s) | Corchetes por estado: crema punteado (fuera de alcance), `hat` con aspa (tapado o inmune), crema → `ember` (enfocando → listo) | Nivel 2: destello de lente en el borde hacia el campista y un «tic» |
| **Disparar** (clic izquierdo) | Obturador + flash; solo congela si el gnomo está enfocado | Destello cálido (80 ms, ≤ 60 %), «clac», retroceso | Nivel 3: destello breve y borde sepia |
| **Revelar** | La foto entra al álbum | Miniatura sepia en la esquina (600 ms) | **Vista en vivo y HUD intacto**: borde sepia + «E · Sacúdete» con anillo |
| **Recargar** | 3 placas; cada disparo gasta una, que vuelve sola | Iconos de placa; corredera | — |

- **Variante B** (por defecto): el enfoque prepara y el clic dispara; fallar gasta placa. Un clic pulsado antes de completar el enfoque queda guardado y dispara al completarse. **Variante A** (opción de playtest): la foto sale sola al completar el enfoque.
- Ambas: **≥ 0,4 s entre disparos** (máx. 3 destellos/s) y opción «Reducir destellos».
- **Figurilla**: el cuerpo pasa a `plaster`; el gorro conserva su color al 50 % para saber a quién rescatar.
- **Estados**: sin placas (clic seco) · recargando · fuera de alcance · tapado · inmune tras liberarse · cámara bloqueada al cargar un objeto · cámara que baja si te patean · torre de gnomos (se enfoca al de arriba).
- **Red** (sin cambio de modelo): el campista es autoridad de su enfoque y su disparo; el dueño del gnomo publica que está congelado (regla 4). La miniatura del álbum es una captura local a `RenderTexture` en el disparo, nunca por frame.

## 4. Tipografía
- **Lilita One**: títulos, botones, números grandes (34–64 px a 1080p).
- **Fredoka SemiBold estática**: cuerpo y texto pequeño (22–28 px).

## 5. Paleta
Todos los tokens actuales de `UiTheme`, más:

| Token | Hex | Uso |
| --- | --- | --- |
| `photoSepia` | #E8D5AE | Foto revelada, borde del congelado |
| `photoInk` | #4A3220 | Tinta sobre sepia (8,2:1) |
| `plaster` | #EFE6D6 | Figurilla de yeso |
| `flash` | #FFF8E6 | Destello del obturador |

## 6. Iconografía
Rejilla de 64 px, contorno `ink` de 6 px, 2–3 colores planos, probado a 32 px. Set: cámara, placa, flash, enfoque, álbum, lente (amenaza) · leño, fósforo, salchicha (cruda, lista, quemada), fogata · gnomo, figurilla, jaula, patada · campista, micro, aviso, sol, luna · teclas. Un icono, un significado: el ojo desaparece; la amenaza es **la lente**.

## 7. Movimiento y sonido
Feedback ≤ 100 ms · transiciones 150–250 ms con rebote corto · levantar 250 ms · flash 80 ms · revelado 600 ms. Sonidos para el equipo de audio: madera al levantar, obturador, flash (*fump*), corredera de placa, clic seco sin placas, tintineo de cerámica al congelarse. Máx. un sonido de UI cada 500 ms.
