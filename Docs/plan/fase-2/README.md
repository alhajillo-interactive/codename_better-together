# Fase 2 — Game feel

Todas las tarjetas son de **pulido**: presentación local derivada de estado replicado (regla 6), ≥ 2 vueltas con playtest. Auditor: Sonnet (no cambian red ni autoridad); si una tarjeta acaba tocando estado de red, pasa a Opus.

Reglas comunes:
- Nada de `Time.timeScale` (hit stop y cámara lenta son visuales: Animator/cámara/partículas).
- Herramientas instaladas primero: PrimeTween (tweens), Cinemachine 3 (noise, impulse), URP Volumes, `SoundCue` + `AudioManager`, `CameraPunch`.
- Partículas/objetos nuevos en prefabs: el worker escribe una **herramienta de editor idempotente** (`GnoMore/…`) que los construye; el orquestador la ejecuta por CLI. Nada de YAML a mano.
- Sonidos nuevos: campos `SoundCue` serializados que pueden quedar vacíos; si no hay clip adecuado, reutilizar uno existente como provisional y listarlo en el reporte para el equipo de audio.
- Cero asignaciones por frame; late joiners no reciben feedback de eventos pasados.

| Tarjeta | Qué | Archivos principales |
| --- | --- | --- |
| C2.1 | Fuego (3 estados, luz con parpadeo, humo) y cubeta (llenado, balanceo del agua, vertido con chorro y vapor) | `FireplaceManager`, `WaterBucket` + herramienta |
| C2.2 | Cámara: head bob por rol con Cinemachine Noise, FOV al correr, golpe al aterrizar | `FirstPersonCamera`, `CameraFocus`, `CameraPunch` |
| C2.3 | Salchicha: siseo creciente, «ding» al estar lista, alarma antes de quemarse, mordiscos | `Sausage` |
| C2.4 | Patada: anticipación, hit stop visual, FOV, estrellitas | `CamperKick`, `Kickable` |
| Después | Petrificación (Volume + Shader Graph), día/noche (Volume + avisos), siluetas como Renderer Feature, pase de audio | — |

## Estado (29 oct 2026)
C2.1–C2.4 aprobadas por Sonnet, integradas, herramientas ejecutadas por CLI y test multi-peer en verde. Arreglos del orquestador: C2.2 umbral de aterrizaje 0,6 m, sin caída al bajar de la torre, FOV sin acumular, builder sólo en cámaras de juego; C2.3 siseo apagado al quemarse.

**Sonidos provisionales para audio (Alejandro):** cubeta llenar/vaciar, vertido, apagado y siseo de vapor (todos con `cascada.wav`); siseo de salchicha (`fogata/fuego_loop.wav`), «lista» (`SFX_FireReady`), alarma (`SFX_Watched`), quemada (`SFX_Petrify`); pop al tomar (`SFX_LogHandOff`).

**Para confirmar en V2:** el fuego «crece con los leños» vía brasas (no tamaño de llama); bob y FOV al correr con ambos roles (mareo del gnomo); indicador de patada y estrellitas; tiempos de la salchicha audibles.

## Estado del segundo lote (29 oct 2026)
C2.5 petrificación (Volume local en el dueño, barrido de piedra pies→cabeza, polvo y fragmentos), C2.6 día/noche (noche en los últimos 90 s, atardecer 60 s antes, avisos a 30 s y 10 s, niebla y Volume de noche, pista nocturna opcional en `MatchMusic._playingNight`) y C2.7 siluetas (Renderer Features «Mask» + «Ink» sobre la capa `Silhouette` = 16; el gnomo ya no duplica 7 mallas; captura antes/después idéntica; el campista sigue sin contorno) — auditadas por Sonnet, integradas, montadas por CLI, test en verde.

Pendiente para audio: pista de noche (`_playingNight`). Para V2: tinte/Volume de petrificado, avisos de noche a 2:00 y 1:40 del final en partida de 10 min, comparar FPS con `PerfProbe` antes/después de C2.7.
