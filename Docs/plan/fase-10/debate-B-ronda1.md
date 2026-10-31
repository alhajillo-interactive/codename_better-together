# Debate B, ronda 1 (Escéptico)

**V** = verificado en archivos o fuente URP. **S** = supuesto. Ninguna ganancia está medida.

| # | Veredicto | Motivo |
| --- | --- | --- |
| 1 SSAO | Acepto con cambios | Sí `Downsample`. `Source` no quita el prepass (V): SSAO corre antes de opacos y `UniversalRenderer.cs:1879` lo exige. Sólo evita normales. |
| 2 Opaque | Acepto | `Water.mat` sólo está en `Bucket` y `CampSite`, sin referencias en `CampAbrazo` ni prefabs (V, en `_Project`). |
| 3 Sombras | Acepto con cambios | Luces confirmadas (V). El soporte adicional apenas ahorra por frame (S). A omitió `m_SoftShadowQuality: 3` sobre mapa de 512 (V): probar Medium antes que 35 m. |
| 4 Audio | Acepto con cambios | Cifras exactas, pero `ambiente_*` ya hace streaming y `cascada` ya es 24 kHz (V). Fuego a 24 kHz y 0,4 puede perder chisporroteo (S): aprobar de oído. `avivar_llama`: comprimido en memoria. |
| 5 Instanciado | Rechazo | `m_BrgStripping: 0`; `GPUResidentDrawer.Validator.cs:60` exige Keep All (V; enum: S): quedaría inerte. 95/95 pinos estáticos (V), probablemente no instanciables (S). Nadie demostró límite de CPU. |
| 6 Lejanía | Acepto con cambios | Manda `CinemachineCamera` (`FarClipPlane: 1000`, V). Diagonal 155,6 m > 140: usar 160. Fuera del mapa casi no hay nada (S): ≈ 0 draws. Vegetación en capa 11, dentro de `_occluders` (V): cambiarla altera máscaras. El cono (8+2 m) no sufre (V), pero un gnomo lejano pierde cobertura visual. |
| 7 Terreno | Acepto con cambios | `drawInstanced` sí. `PixelError` 8: falta medir; separa malla y collider, y algo puede flotar (S). Rechazo 4 capas. |
| 8 Mezcla | Partir | Animator: falta medir; sólo proxies fuera de cámara, ganancia marginal (S). `Mobile`: acepto si es sólo PC. `PlayerCount`: rechazo; `MaxPlayers = Min(8, PlayerCount)` (V): bajarlo cambia el aforo actual, decisión de diseño. |

**Base insuficiente:** `PerfProbe` no separa CPU y GPU; `enableFrameTimingStats: 0` (V). En Release, `in_bytes_s` sale −1 (V).

**LOD:** no; los FBX usados rondan 20-30 KB (V). **Oclusión:** sólo ocultaría el tronco (S); falta medir.

## Contrapropuestas

1. **Medir primero.** Añadir `cpu_ms`, `gpu_ms`, draws, SetPass, GC y memoria a `PerfProbe`. Ruta fija, build real, master y cliente por separado. Fijar hardware y presupuesto por sistema. Un cambio por prueba.
2. **Rentabilizar el prepass.** `m_DepthPrimingMode: 0` (V): se paga profundidad sin aprovecharla. Probar priming A/B contra el sobredibujado del follaje (S).
3. **Lo gratis y lo opcional.** Sacar `CampBryan`, `CampIsma` y `CampGizmos` del build: están activos y `MainMenu` sólo apunta a `CampAbrazo` (V; confirmar otras cargas). Añadir ajuste de escala de render: ningún script lo toca (V).

## Preguntas incómodas

1. Sin saber si limita CPU o GPU ni el hardware objetivo, ¿cuál es tu criterio de parada?
2. Si el `far` apenas excluye objetos y el Resident Drawer no arrancaría, ¿de qué base salen tus −10-25 % y −20-40 %?
