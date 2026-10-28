# Fase 1 — Loop y nivel (índice; las tarjetas se escriben al cerrar la fase 0)

Objetivo de la fase: que una partida de 4 minutos en un campamento con dos capas (rutas de gnomo y zonas altas del campista) tenga decisiones de ruta, escondites y un empate técnico en el último minuto. Métricas de salida: win rate del campista 40–55 %; primer sabotaje antes de 0:40; fuego encendido a los 60–90 s; ninguna zona sin visitas en el mapa de calor; < 10 % de tiempo inactivo por jugador.

| Tarjeta | Qué | Depende de |
| --- | --- | --- |
| C1.1 Greybox del campamento 18×14 m en dos capas | Bloques grises con ProBuilder/primitivas según «Bocetos de escenarios» + reglas métricas (abajo); sin arte | C0.6 |
| C1.2 Agacharse (acción `Crouch` ya existe) | Gnomo: silencia pasos y pasa por huecos de 0,3 m; campista: baja la cámara a 0,9 m para espiar y mirar por huecos de ≥ 0,6 m | — |
| C1.3 Escondites (`HidingSpot`) | `NetworkBehaviour` + `IPinSpot` con N ranuras; gnomo con objeto pulsa E sobre el arbusto → el objeto se fija dentro (oculto); campista pulsa E 1,5 s → se vuelcan todos; reutiliza `Carryable.Pin` | C0.2 |
| C1.4 Puntuación de gnomos y MVP | Eventos → puntos (+3 sacar leño, +2 esconder salchicha, +5 quemarla, +2 liberar compañero) en `MatchManager`; pantalla final con MVP | C0.4 |
| C1.5 Ping de mundo para gnomos | Tecla Q: marcador 3 s visible sólo para gnomos (RPC sin estado) | C0.7 |
| C1.6 Torre tótem y leño entre dos | Petrificar la base congela toda la torre; volver `Gnomes Required = 2` en `Wood` con línea de tensión | C0.3 |
| C1.7 Balance con datos | Aplicar la tabla de Notion (3→5 leños, cocción 12/22, 4 min) y medir con C0.4 | C0.4, C1.1 |
| C1.8 Harness de humo multi-peer | `ScriptedInput` que sustituye las acciones del Input System en pruebas y un test de Play Mode con 4 runners (multi-peer de Fusion) que recorre: tomar, soltar, colocar, prender, cocinar, comer; asserts de «nada flota» (y del objeto en reposo = y del suelo ± 2 cm en todos los peers) y de Animator coherente; capturas de pantalla por peer para el auditor | C0.2, C0.6 |

## Reglas métricas del nivel (ratio actual 4:1 — campista 1,8 m, gnomo 0,45 m; revisar si se pasa a 3:1)

- Pasadizos sólo de gnomo: altura libre 0,6–0,7 m, ancho ≥ 0,5 m (el `CharacterController` del campista tiene radio 0,3 y altura 1,5: no entra).
- Huecos para espiar/mirar desde fuera (campista agachado): ≥ 0,6 m.
- Escalones para el gnomo sin salto: ≤ 0,15 m (`stepOffset`); con salto (altura 0,5 m): ≤ 0,4 m. Huecos horizontales con salto a la carrera: ≤ 1,0 m.
- Zonas altas: mesa 0,8 m (torre de 2), hielera 0,5 m (torre de 2 o salto desde roca), techo de carpa 1,2 m (torre de 3). El campista alcanza con la mano hasta ~2 m.
- Un bloqueador de visión cada 3–4 m en toda ruta de gnomos; pasillos ≥ 1,2 m donde pase un leño entre dos; nada escondible por encima de 0,6 m ni dentro de geometría.
- Spawns de gnomos a ≥ 12 m del campista; cruce del mapa: campista ~3,6 s, gnomo ~5 s, gnomo con leño ~7 s.
