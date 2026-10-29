# Fase 5 — Pulido tras el playtest de Bryan (29 oct 2026)

| Tarjeta | Estado |
| --- | --- |
| C5.1 Animación del campista | Hecha por el orquestador (CLI): `Apply Root Motion` desactivado en el Animator de `Camper/Mesh` y `Gnome/Mesh02` (la animación desplazaba la malla y la dejaba subida tras el salto); el blend tree del campista usa `campista_running` al esprintar (antes repetía `Walk` a 1,5×). |
| C5.2 Salto: coyote time, salto desde la torre y no quedarse encima de otro jugador | Integrada (Opus, 1 vuelta + correcciones del orquestador). Coyote 0,12 s y búfer 0,12 s; salto de la torre a 0,9 m (6 m/s); se resbala de una cabeza sólo si el otro jugador es el único apoyo. |
| C5.3 Tronco pesado: anclado en solitario, en pareja uno mueve y otro gira | Integrada (Opus, 1 vuelta + correcciones). Solo: no se desplaza, tirón/temblor local y aviso. Pareja: líder = primero que lo cogió; el otro orbita con A/D. Holgura de suelta en pareja 0,3 m por latencia. Falta asignar `_strainSfx` en el prefab Gnome (audio). |
| C5.5 Salto en fases y golpe de cámara | Integrada (Sonnet, 2 vueltas). `GnoMore/Construir fases de salto` ejecutada: JumpUp / Fall (pose del pico) / Land (tramo final, ~0,15 s) en ambos Animator; golpe de cámara al aterrizar a más de 5,5 m/s. `_landSfx` vacío (audio). |
| C5.6 Suelta del gnomo cargado | Integrada (Opus, 2 vueltas). Soltar con E: punto calculado por el campista (`GnomeCarrier.DropPoint`); escape: salida a un lado desde la posición extrapolada del campista; blend visual 0,15 s y espera en el punto de suelta hasta que llega la pose real. |
| Tinte de piedra | Corregido por el orquestador: `Petrifiable` tiñe `_WatercolorBaseColor` (shader de acuarela de los gnomos) por submaterial; antes sólo `_BaseColor` y no se veía. |
| C5.4 Interfaz que escala con la pantalla | Integrada (Sonnet). `GnoMore/Ajustar escalado de UI` y `GnoMore/Revisar escalado de UI` ejecutadas: todos los Canvas a 1920×1080 match 0,5; botones del lobby anclados abajo; informe sin elementos fuera en 16:9, 16:10, 21:9, 4:3 y 720p. OnGUI de debug escalado. |

V2 de C5.1: como campista, caminar, esprintar, saltar y patear; la malla vuelve siempre a los pies. Un gnomo que salta tampoco se queda «flotando».

V2 de C5.2–C5.4: saltar justo al salir de un borde; pulsar salto antes de aterrizar; saltar desde lo alto de una torre de 3; campista cae sobre un gnomo y resbala; gnomo solo con el tronco no se mueve (tira y avisa); con dos, el primero mueve y el segundo gira con A/D; líder esprintando lejos del otro sin que se caiga el tronco enseguida; menú y HUD en Game view 1280×720, 2560×1080 y 1024×768.

V2 de C5.5/C5.6: saltar quieto y corriendo (sube, cae, aterriza), caer de un borde sin saltar, caer desde la torre (golpe de cámara); con dos instancias, correr cargando a un gnomo y soltarlo con E (aparece delante) y que se escape machacando E (sale a un lado); petrificar a un gnomo y ver el barrido gris.
