# Debate A, ronda 2 (Ahorrador)

Comprobé en archivos y fuente URP las objeciones decisivas de B. Las marcadas (B) no las repetí.

## Concedo

- **SSAO:** mi error. `UniversalRenderer.cs:1879` confirma que `Source: Depth` mantiene el prepass con `AfterOpaque: 0`.
- **Audio:** `ambiente_*` ya hace streaming y `cascada` ya es 24 kHz: lo pasé por alto.
- **Resident Drawer:** `Validator.cs:60` exige Keep All y `m_BrgStripping: 0`. Retirado: sin CPU medida no se justifica ampliar variantes.
- **`PlayerCount`:** `MaxPlayers = Min(_maxPlayers, PlayerCount)` y `_maxPlayers: 8` en `MainMenu`. Es decisión de diseño.
- **Lejanía:** 140 < diagonal de 155,6 m (B); el `far` casi no recorta nada. `layerCullDistances` toca la capa 11 de `_occluders`: retirado.
- **Sombras:** `m_SoftShadowQuality: 3` confirmado sobre 512; probar Medium antes que 35 m.

## Defiendo

- **SSAO sin prepass:** la misma línea exige prepass con Depth sólo si el evento va antes de opacos. Con `AfterOpaque: 1`, `Source: 0`, `Downsample: 1` y MSAA apagado no lo fuerza SSAO. Falta confirmar en Frame Debugger que nada más lo pide. Es incompatible con el priming de B: A/B.

## Preguntas de B

1. **Criterio de parada:** no lo tenía. Propongo: hardware mínimo fijado por el equipo; `frame_ms_p99` ≤ 16,6 ms y RAM bajo presupuesto, con 4 jugadores en build. Se para al cumplirlo, o cuando un cambio aporte < 0,3 ms.
2. **Base de −10-25 % y −20-40 %:** ninguna. Fueron intuición, no cálculo. Las retiro.

## Lista final

**Acordado:** medir primero (contrapropuesta 1); `m_RequireOpaqueTexture: 0`; SSAO `Downsample: 1`; audio de fuego y `avivar_llama` aprobado de oído; `drawInstanced`; quitar escenas sobrantes tras confirmar cargas; `Mobile` si es sólo PC; ajuste de escala de render; `far` 160 (higiene, sin ganancia).

**En desacuerdo:**
- `AfterOpaque` sin prepass frente a priming: A/B de `gpu_ms` y captura visual.
- Cascadas 2 y 35 m: `gpu_ms` y sombras en ruta fija, tras Soft Medium.
- `PixelError` 8: `gpu_ms` y `GroundingAudit`.
- Animator en proxies: `cpu_ms` con 3 gnomos fuera de cámara, comprobando sockets.

**Retirado:** Resident Drawer, `PlayerCount`, 4 capas de terreno, `layerCullDistances`, cifras de ganancia de la ronda 1.
