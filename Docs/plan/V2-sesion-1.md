# V2 — Sesión 1 (checklist para la persona)

Rama `feature/fase-0`. Montaje ya hecho por CLI (HUD nuevo, cubeta, río, `LevelBounds`, gracia 4 s, fogata a ras de suelo). Humo de 1 jugador pasado sin errores (conecta, spawnea, PerfProbe escribe CSV). Los pasos detallados están al final de cada tarjeta; aquí va el orden y lo mínimo.

**Montaje previo:** Multiplayer Play Mode con editor + 3 jugadores virtuales, escena `CampBryan`, misma sala. F3 = PerfProbe, F4 = auditoría de flotantes en juego.

## Orden

1. **Partida de 3 min completa** (C0.1 + C0.4). Al salir: rellenar `fase-0/C0.1-resultados.md` y correr `python Tools/playtest-report.py %USERPROFILE%\AppData\LocalLow\DefaultCompany\codename_better-together\playtest`.
2. **HUD** (C0.7): prompt, mira, viñetas de gnomo y campista, barra de energía, petrificado, MIC con M. Consola sin `LogError` de HUD.
3. **Sostener y soltar** (C1.0 + C0.8): leño y salchicha en la vista; soltar pegado a pared y esquina; patear a quien lleva algo. Repetir con Network Conditions 120 ms (`NetworkProjectConfig`), y doble E frente a un montón de leños.
4. **Hitos y agua** (C1.H): encender → 4 s de gracia → llenar la cubeta en el río → verter → se apaga, salta un leño → reencender.
5. **Física de proxies y respawn** (C0.2): nada tiembla ni flota en clientes no master; tirar un leño fuera del mapa → vuelve a los 20 s.
6. **Mirada y liberación** (C0.3): petrificar, liberarse machacando E, patear al petrificado.
7. **Voz** (C0.5): hablar, pulsar-para-hablar, mute persistente.
8. **Los tres casos** (en cualquier momento): entrar tarde, cerrar el master a mitad de partida, que se vaya alguien con algo en la mano.
9. **F4 tras 1 min** de partida: sin hallazgos nuevos.

## Cómo reportar (por cada fallo, una línea)

`[Tarjeta] qué hiciste → qué esperabas → qué pasó (quién lo vio: master / cliente) + captura o línea de consola`

Ejemplo: `[C1.H] verter con la cubeta llena junto al fuego → se apaga → no pasa nada en el cliente 2; consola: NullReference en WaterBucket.cs:88`.

## Decisiones que conviene tomar en la sesión

- ¿Soltar la cubeta llena la derrama?
- CampIsma: ¿añadirle `MatchHUD` o sacarla de Build Settings?
- Sonidos definitivos: pop al tomar (hoy `SFX_LogHandOff`), llenar/vaciar cubeta, siseo del vertido.
- Ratio campista/gnomo, duración de partida, leños 3→5 o 5/5.
