# C11 — Misiones secundarias y variación por partida

Fase 11 · Auditor: Opus (red y autoridad) · Iteraciones máx: 3, con playtest

## Por qué
Bryan quiere misiones secundarias y que cada partida sea distinta. Regla: las misiones compiten con el objetivo principal y nunca ganan la partida.

## Estado de red (todo en `MatchManager`, master)
`MissionSeed` (int), `PhotosDone` (byte, un bit por encargo), `ButterfliesCaught` (int, un bit por punto), `RevealTimer`, `SwarmTimer`. Sin objetos de red nuevos. Sin `MissionLayout` en la escena (`CampBryan`, `CampIsma`) la semilla queda en 0 y todo funciona como hoy.

## Cambios exactos
1. **Variación**: `MissionLayout` (componente de escena sin red) guarda los puntos posibles de cada objeto. Se elige uno por objeto y sector, nunca azar total: se conserva que falten leños al norte. Todos los peers actualizan el `_startPosition` de cada `Carryable` al ver la semilla; el master además mueve los objetos. Gnomos: 6 puntos de salida, 3 usados según la semilla.
2. **Fotos**: tres encargos por partida: uno del norte, uno del sur y uno de gnomo (en piedra o con bomba). Se cumplen al gastar una placa. La foto que petrifica no cumple «gnomo ya en piedra»: hace falta otra placa. El dueño del campista comprueba cono y línea de visión; el master valida con el predicado espejo de distancia.
3. **Mariposas**: 5 de 11 puntos por partida; basta con 3 entre todo el equipo. `ButterflySpot` es `IHoldInteractable` (0,8 s), sin red, en capa `NoCollision`; por eso `Gnome.prefab` pasa `_interactionMask.m_Bits` de 3072 a 35840. 1 punto al que atrapa.
4. **Ventajas**: automáticas al completar. Campista: los gnomos salen en el plano 6 s. Gnomos: enjambre sobre el visor del campista 6 s (`TickTimer`, sólo presentación).
5. **HUD**: `MissionHUD` a la izquierda y marcadores en el plano. Sonidos en `Tools/generate-sfx.py`.

## Decisión del campista y puntos
El marcador sólo lista gnomos, así que las fotos no dan puntos: su valor es la ventaja.

## Casos multijugador
- **Late joiner**: recibe semilla y máscaras; `Render` aplica la semilla.
- **Cambio de master**: no se sortea de nuevo; los `_startPosition` ya estaban aplicados en todos.
- **Jugador que se va**: lo conseguido por su equipo se mantiene. Si el campista se va, sus encargos quedan como están.

## Validación
- V0 compila; V1 checklist; constructor registra los puntos rechazados.
- V2: dos partidas seguidas tienen objetos y encargos distintos; todo toca el suelo; objeto perdido vuelve al punto de esa partida; foto sólo cuenta con el objetivo a la vista; prompt de mariposa coincide con lo que acepta el master; enjambre y plano duran 6 s; cambio de master a mitad; `CampBryan` igual que antes.
