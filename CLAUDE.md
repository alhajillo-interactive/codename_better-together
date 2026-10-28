# GnoMore Campers (codename Better Together) — guía para agentes y personas

Multijugador asimétrico 1 campista vs. 3 gnomos. Unity 6000.3.15f1 + URP 17 + **Photon Fusion 2.1.3 en modo Shared** + Photon Voice.
La documentación de diseño y arquitectura vive en Notion (página «GnoMore Campers»); las tarjetas de trabajo viven en `Docs/plan/`.

## Dónde está cada cosa

- Código del juego: `Assets/_Project/Scripts/<Área>/`, namespace `BetterTogether.<Área>`. Nada va fuera de `_Project`.
- Prefabs: `Assets/_Project/Prefabs/` (`Camper`, `Gnome`, `Items/Wood|Matches|Sausage`, `Fireplace`, `GameManager`, `Interactuables`).
- Escena jugable: `Assets/_Project/Scenes/CampBryan.unity`. Menú: `MainMenu.unity`.
- SDK de Photon: `Assets/Photon/` — **nunca se modifica**. Config de red: `Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion`.
- Paquetes ya disponibles que se deben usar antes de escribir algo propio: Cinemachine 3 (cámara, impulsos, ruido), PrimeTween (tweens), URP Volumes (post-procesado), Input System (acciones en `Assets/InputSystem_Actions.inputactions`), `SoundCue` + `AudioManager` (audio), Multiplayer Play Mode (pruebas con varios jugadores), Unity Test Framework.

## Modelo de autoridad (Fusion Shared)

| Objeto | Autoridad (State Authority) | Quién simula | Quién presenta |
| --- | --- | --- | --- |
| Jugador (`Camper`, `Gnome`) | Su propio cliente | Su dueño en `FixedUpdateNetwork` | Todos en `Render`/`LateUpdate` |
| Objetos del mundo (`Carryable`, `FireplaceManager`, `MatchManager`, `Sausage`) | Master client (`MasterClientObject`, migra si el master se va) | Master en `FixedUpdateNetwork` | Todos |

Reglas obligatorias (se auditan en cada cambio):

1. **Acción sobre un objeto ajeno = RPC a `StateAuthority`** (`RpcSources.All, RpcTargets.StateAuthority`) pasando `NetworkBehaviourId`. La autoridad **valida** con el mismo predicado `CanX(...)` que el cliente usa para mostrar el prompt (patrón espejo: `CanInteract` / `CanPlace` / `CanDouse`).
2. **Leer `[Networked]` solo después de `Spawned()`**: flag `_isSpawned`; `Despawned` lo apaga. `LateUpdate` y `Update` también corren antes de `Spawned`, así que llevan guardia. `Object.IsValid` no basta.
3. **Simulación solo con `HasStateAuthority` en `FixedUpdateNetwork`. Presentación en `Render`/`LateUpdate`.** Nunca escribir `[Networked]` desde `Update`.
4. **Estado de red mínimo.** Nada que cambie cada tick si se puede derivar: usa `TickTimer` o «ancla + tick» (`valor en t0` + `Runner.Tick` en t0) y calcula el valor actual en cada cliente. Sin strings, listas ni floats que decaen por frame. Las referencias a `NetworkBehaviour` se replican como ids (barato). **Límite del patrón:** un valor derivado del tick local vale para presentación y para decisiones de la propia autoridad, nunca para una decisión que toma otro peer (p. ej. el gnomo congelándose por el bloqueo del campista): esa decisión necesita un bool/evento replicado por la autoridad que la toma, porque con latencia el otro peer extrapolaría.
5. **Física:** solo la autoridad simula Rigidbodies; en proxies el Rigidbody es **kinemático** y `NetworkTransform` manda. Jugadores usan `CharacterController`. Cuando la autoridad migra, se recalcula el estado kinemático.
6. **Feedback local a partir de estado replicado**: sonidos, partículas, punches de cámara y UI se disparan al detectar cambios en `Render` (o `[Networked, OnChangedRender]`), no con RPCs. Los RPC de presentación se reservan a eventos sin estado (impacto, animación de patada).
7. **Cero asignaciones por frame**: nada de LINQ, `FindObjectsByType`, `GetComponentsInChildren` o `new` en `Update`/`Render`/`FixedUpdateNetwork`. Cachear en `Awake`/`Spawned`; buffers `NonAlloc`.
8. **Robustez multijugador**: todo debe funcionar para quien entra tarde (late joiner), cuando el master cambia y cuando un jugador se va a mitad de partida. Cada cambio dice explícitamente qué pasa en esos tres casos.

## Estilo

- Campos privados `_camelCase`; `[SerializeField]` con `[Tooltip]` en inglés; propiedades públicas `PascalCase`. Comentarios y tooltips en **inglés**; textos para el jugador en **español**.
- Componentes pequeños, una responsabilidad, sin herencia innecesaria. Antes de crear un sistema, buscar si ya existe (`Carryable`/`IPinSpot` sirve para «colocar» cosas en cualquier sitio; `SoundCue` para audio; `CameraPunch` para FOV/roll).
- Los valores de balance van en `[SerializeField]` con tooltip, nunca como constantes mágicas repartidas.

## Qué no hacer

- No editar escenas `.unity` ni archivos de `Assets/Photon`, `Library`, `ProjectSettings` desde un agente salvo que la tarjeta lo pida con el campo exacto.
- No editar prefabs YAML a mano salvo campos serializados existentes y nombrados en la tarjeta.
- No ejecutar Unity en batchmode si existe `Temp/UnityLockfile` (el editor está abierto). Usa `Tools/compile-check.ps1`, que lo comprueba.
- No usar `Time.timeScale` para hit stop ni pausas: Fusion sigue simulando. El hit stop es visual (Animator/cámara).
- No cambiar el tick rate de Fusion sin una medición antes y después (tarjeta C0.1).

## Validación que pasa todo cambio

- **V0 Compila** sin errores ni warnings nuevos (`Tools/compile-check.ps1`, o el editor abierto sin errores en consola).
- **V1 Auditoría estática** (la hace el auditor con la checklist de `Docs/plan/README.md`): autoridad, `_isSpawned`, sin escrituras networked en `Update`, sin asignaciones por frame, late joiner / migración / salida de jugador, capas y máscaras correctas, nada «flotando» (posiciones derivadas de un `Transform` de referencia, no de números mágicos).
- **V2 Prueba en editor** (una persona, con Multiplayer Play Mode, 1 editor + 3 jugadores virtuales): pasos y resultado esperado escritos en la tarjeta. Incluye siempre: colisiones (nada atraviesa ni flota), animaciones (parámetros del Animator coherentes con el estado replicado), interacciones (prompt coincide con lo que la autoridad acepta), rendimiento (FPS y bytes/s con `PerfProbe`).

## Flujo con agentes

Orquestador (diseño, tarjetas, integración) → **Worker** (modelo barato, implementa una tarjeta en una rama/worktree y entrega el reporte) → **Auditor** (Opus, revisa contra la tarjeta y la checklist, devuelve hallazgos ordenados por severidad) → Worker corrige → Auditor aprueba → Orquestador integra y una persona hace V2. Máximo 3 iteraciones; tarjetas de pulido (`game feel`) exigen al menos 2 iteraciones con playtest. El formato de reporte está en `Docs/plan/README.md`.
