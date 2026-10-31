# Conclusiones del debate de optimización

> Dos rondas entre el Ahorrador y el Escéptico, sobre lectura de archivos y fuente de URP. **Nada está medido**: no existe línea base (`C0.1-resultados.md` está vacío) y todas las cifras de ganancia iniciales se retiraron por no tener base.

## Criterio de parada
Con hardware mínimo fijado por el equipo: `frame_ms_p99` ≤ 16,6 ms y memoria bajo presupuesto, con 4 jugadores en build. Se para al cumplirlo, o cuando un cambio aporte menos de 0,3 ms.

## Paso 1: medir
Ampliar `PerfProbe` con `cpu_ms`, `gpu_ms`, draws, SetPass, GC y memoria (activar `enableFrameTimingStats`; hoy `in_bytes_s` sale −1 en Release). Ruta fija en build real, master y cliente por separado, un cambio por prueba.

## Paso 2: acordado, bajo riesgo
- Opaque texture apagada **sólo en la cámara de `CampAbrazo`**: ningún material de ese nivel la lee, pero `Water.mat` sí en las escenas antiguas (corregido en C10.1; apagarla en `PC_RPAsset` rompía su agua).
- SSAO `Downsample: 1`.
- `drawInstanced` en el terreno.
- `far` de cámara a 160 m (higiene, sin ganancia esperada).
- Ajuste de escala de render en Ajustes.

## Paso 3: acordado, necesita decisión del equipo
- Sacar `CampBryan`, `CampIsma` y `CampGizmos` del build, tras confirmar que nada los carga.
- Quitar el nivel de calidad `Mobile` si el juego es sólo PC.
- Audio de `fuego_loop` (42 MB) y `avivar_llama`: comprimir y aprobar de oído. 100 de los 102 MB de audio están en 5 WAV.

## Paso 4: en desacuerdo, lo resuelve una medida
| Tema | Ahorrador | Escéptico | Prueba |
| --- | --- | --- | --- |
| Prepass de profundidad | SSAO `AfterOpaque` sin prepass | Depth priming para aprovecharlo | A/B de `gpu_ms` y captura visual (son incompatibles) |
| Sombras | 2 cascadas y 35 m | Primero Soft Medium | `gpu_ms` y sombras en la ruta fija |
| Terreno | `PixelError` 8 | Falta medir; puede dejar objetos flotando | `gpu_ms` y `GroundingAudit` |
| Animator en proxies | Culling fuera de cámara | Ganancia marginal | `cpu_ms` con 3 gnomos fuera de cámara, comprobando sockets |

## Descartado
Resident Drawer (requiere variantes que hoy se eliminan, sin CPU medida), bajar `PlayerCount` (cambia el aforo: decisión de diseño), 4 capas de terreno (pierde la acuarela), `layerCullDistances` (la vegetación está en una capa que usa la cámara de fotos), LOD (mallas ya ligeras), red y voz (ya optimizadas).
