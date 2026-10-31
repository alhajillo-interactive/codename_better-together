# Debate A, ronda 1 (Ahorrador)

**Medido:** sólo cifras leídas de archivos. `C0.1-resultados.md` sigue vacío: no existe ninguna línea base. **Toda ganancia es estimación (E).** Base: 3 min con `PerfProbe`, 4 jugadores, en build.

1. **SSAO barato.** `PC_Renderer.asset`: `Source: 1→0` (Depth), `Downsample: 0→1`. Evidencia: SSAO con normales a resolución completa, lo que añade un prepass de opacos. E: −10-20 % GPU. Riesgo: contacto más burdo bajo árboles. Medir: `frame_ms_p99`.

2. **Copias de cámara sin uso.** `PC_RPAsset.asset`: `m_RequireOpaqueTexture: 1→0`. Evidencia: los 33 materiales de Abrazo usan URP Lit; sólo `WaterShader.shadergraph` lee escena, y falta confirmar si algo de Abrazo lo usa. E: −0,3-1 ms GPU. Riesgo: agua rota si se usa. Medir: `fps_avg`.

3. **Sombras.** `PC_RPAsset.asset`: `m_ShadowCascadeCount: 3→2`, `m_ShadowDistance: 50→35`, `m_AdditionalLightShadowsSupported: 1→0`. Evidencia: 494 MeshRenderers propios proyectan; 235 instancias ya apagadas; las 16 luces de escena y la fogata con `m_Type: 0` o direccional. El atlas de 2048 no tiene clientes conocidos (falta revisar luces creadas por script). E: −15-25 % de draws de sombra. Riesgo: sombras apareciendo a media distancia. Medir: `frame_ms_p99`.

4. **Audio.** Metas de `fuego_loop.wav`, `cascada.wav`, `avivar_llama.wav`, `ambiente_*.wav`: `forceToMono: 1` en los 3D, 24 kHz, `quality: 0.4`, `loadType: 2` (Streaming) en los largos. Evidencia: 100 MB de 102 en 5 WAV; `fuego_loop` 42 MB, 24 bits, 147 s, calidad 1, en memoria; `avivar_llama` 39 s con `DecompressOnLoad` (≈7,5 MB PCM, calculado); `forceToMono: 0` en 23/23. E: −15-25 MB RAM y build. Riesgo: artefactos; streaming consume hilo. Medir: `PerfProbe` no mide RAM; añadir Memory Profiler, y tirones en `frame_ms_max`.

5. **Instanciado.** `m_GPUResidentDrawerMode: 0→1`, con pino/arbusto como candidatos. Evidencia: 782 instancias de prefab de 55 fuentes (95 `SM_Pine_02`, 79 `SM_Pine_01`); 0 de 33 materiales con instancing; ya es Forward+; 541 objetos con batching estático, que duplica vértices. Compatibilidad con estático y coste en variantes: sin verificar. E: −20-40 % CPU de render. Riesgo: artefactos, build mayor. Medir: `fps_avg`, `frame_ms_max`.

6. **Lejanía.** Cámara `far clip 1000→140`, en mapa de 110 m; `Camera.layerCullDistances` de 35 m para hierba, flores, setas. Evidencia: `LODGroup: 0`, `m_OcclusionCullingData` nulo, sin niebla (`m_Fog: 0`). E: −10-25 % draws en tomas abiertas. Riesgo: `pop-in`, que perjudica a quien vigila a distancia. Medir: `fps_avg` en la torre.

7. **Terreno.** `AbrazoLevelBuilder.cs`: `drawInstanced = true`; `m_HeightmapPixelError: 5→8`. Evidencia: `m_DrawInstanced: 0`; 6 capas (supuesto: >4 fuerza segunda pasada); heightmap 257 ya modesto. Bajar a 4 capas es opcional y ahorra otra pasada. E: −0,3-0,8 ms. Riesgo: perder acuarela si se quitan capas. Medir: `frame_ms_p99`.

8. **Animadores y dos tiers.** `cullingMode: 0→1` en proxies; eliminar `Mobile` de `QualitySettings.asset` si sólo se publica PC; `PlayerCount: 10→4` con tarjeta. Evidencia: 7 SkinnedMesh por gnomo; todos `AlwaysAnimate`; 2 assets URP multiplican variantes. E: −0,2-0,5 ms y build menor. Riesgo: sockets de carga parados fuera de cámara. Medir: `frame_ms_max`, `in_bytes_s`.

**Descartado:** red (`Pitch` y `AnimationMovement` ya cuantizados), voz (20 kbps), texturas (1024), colliders estáticos (718: sin coste por frame, supuesto).
