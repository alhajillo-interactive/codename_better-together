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
