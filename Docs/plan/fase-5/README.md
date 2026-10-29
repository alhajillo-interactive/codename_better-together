# Fase 5 — Pulido tras el playtest de Bryan (29 oct 2026)

| Tarjeta | Estado |
| --- | --- |
| C5.1 Animación del campista | Hecha por el orquestador (CLI): `Apply Root Motion` desactivado en el Animator de `Camper/Mesh` y `Gnome/Mesh02` (la animación desplazaba la malla y la dejaba subida tras el salto); el blend tree del campista usa `campista_running` al esprintar (antes repetía `Walk` a 1,5×). |
| C5.2 Salto: coyote time, salto desde la torre y no quedarse encima de otro jugador | Pendiente |
| C5.3 Tronco pesado: anclado en solitario, en pareja uno mueve y otro gira | Pendiente |
| C5.4 Interfaz que escala con la pantalla | Pendiente |

V2 de C5.1: como campista, caminar, esprintar, saltar y patear; la malla vuelve siempre a los pies. Un gnomo que salta tampoco se queda «flotando».
