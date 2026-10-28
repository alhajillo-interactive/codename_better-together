# Plan de implementación por fases — protocolo de trabajo

Resumen ejecutable del plan que está en Notion («Técnico → Plan por fases y orquestación con agentes»). Aquí viven las **tarjetas** que un worker implementa y un auditor valida. Léelo junto con `CLAUDE.md` (reglas del proyecto).

## Roles

| Rol | Modelo | Qué hace | Qué entrega |
| --- | --- | --- | --- |
| Orquestador / game designer | Fable (esta sesión) | Diseña, escribe tarjetas explícitas, decide orden y paralelismo, integra ramas, actualiza Notion | Tarjetas, decisiones, merge |
| Worker | Haiku 5.5 (tareas mecánicas) o Sonnet 5.5 (tareas con semántica de red delicada) | Implementa **una** tarjeta en un worktree, sin salirse del alcance | Diff + reporte de worker |
| Auditor | Opus 5.5 | Revisa el diff contra la tarjeta y la checklist V1; no reescribe, señala | Informe de auditoría |
| Persona (Bryan / equipo) | — | Ejecuta V2 en el editor con Multiplayer Play Mode, playtest, decide balance | Resultado de V2, notas de playtest |

Ciclo por tarjeta: worker → auditor → worker corrige → auditor aprueba (máx. 3 vueltas) → orquestador integra → persona ejecuta V2. Las tarjetas marcadas **pulido** exigen al menos 2 vueltas con playtest antes de darse por hechas.

## Estados de una tarjeta

`pendiente` → `en worker` → `en auditoría` → `corrigiendo` → `aprobada (falta V2)` → `hecha` | `bloqueada (motivo)`.

## Formato del reporte del worker

```
TARJETA: C0.2
ARCHIVOS TOCADOS: lista con ruta y qué cambió en cada uno
QUÉ HICE: 5-10 líneas
DECISIONES: dudas que resolví y cómo (con la alternativa descartada)
FUERA DE ALCANCE / NO HECHO: lo que la tarjeta pedía y no hice, y por qué
CASOS MULTIJUGADOR: qué pasa con late joiner / migración de master / jugador que se va
VALIDACIÓN V0: compiló sí/no (cómo lo comprobé); si no pude compilar, decirlo
RIESGOS: lo que el auditor debería mirar con lupa
```

## Formato del informe del auditor

Hallazgos ordenados por severidad (**Bloqueante** → **Importante** → **Menor** → **Sugerencia**), cada uno con archivo:línea, qué falla, cómo reproducirlo o por qué es incorrecto, y la corrección propuesta en una frase. Termina con `VEREDICTO: APROBADA | CORREGIR` y la lista de puntos V2 que la persona debe probar en el editor.

## Checklist V1 (auditoría estática)

1. **Autoridad**: toda escritura `[Networked]` ocurre con `HasStateAuthority`; acciones sobre objetos ajenos van por RPC a `StateAuthority` y la autoridad valida con el mismo predicado que muestra el prompt.
2. **Guardas de spawn**: ninguna lectura `[Networked]` antes de `Spawned` ni después de `Despawned` (`_isSpawned`); `Update`/`LateUpdate` protegidos.
3. **Nada networked se escribe en `Update`**; nada que cambie cada tick pudiendo derivarse (ancla + tick / `TickTimer`).
4. **Física**: Rigidbody de proxies kinemático; sólo la autoridad aplica fuerzas; colisionadores habilitados/deshabilitados de forma consistente en todos los peers; capas y máscaras explícitas (no `~0` nuevo).
5. **Asignaciones por frame**: cero `new`, LINQ, `Find*`, `GetComponent*InChildren` en `Update`/`Render`/`FixedUpdateNetwork`/`LateUpdate`.
6. **Casos multijugador** cubiertos y descritos: late joiner, migración de master, jugador que se va con un objeto en la mano o en una torre.
7. **Nada flota ni atraviesa**: posiciones de objetos derivadas de un `Transform`/`IPinSpot` o de física real, nunca de offsets mágicos; escalas de prefabs respetadas (FBX en cm con escala 100).
8. **Animaciones**: parámetros del Animator se derivan del estado replicado en `Render`; estados excluyentes (stun, piedra, baile, salto) no se solapan.
9. **Feedback**: sonidos y efectos disparados por cambio de estado replicado (no por RPC) salvo eventos sin estado; late joiners no reciben feedback de eventos pasados.
10. **Alcance**: el diff sólo toca lo que la tarjeta pide; nada en `Assets/Photon`, escenas ni `ProjectSettings` salvo instrucción explícita.
11. **Legibilidad**: nombres y tooltips claros, sin duplicar lógica que ya existe (`Carryable`, `IPinSpot`, `SoundCue`, `CameraPunch`).

## Checklist V2 (prueba en editor, por una persona)

Multiplayer Play Mode: editor + 3 jugadores virtuales, misma sala. Siempre:

- **Colisiones**: ningún objeto atraviesa suelo, muros o jugadores; nada flota (objeto en reposo apoyado, distancia al suelo < 2 cm) ni tiembla en los clientes que no son autoridad.
- **Interacción**: el prompt que ve el jugador coincide con lo que la autoridad acepta (si dice «[E] Tomar», tomar funciona a la primera; si no se puede, el prompt explica por qué).
- **Animaciones**: el personaje remoto camina/corre/salta/se aturde/se petrifica igual que el local; no hay deslizamiento (pies que patinan) ni poses congeladas.
- **Rendimiento**: `PerfProbe` (tarjeta C0.1) sin caídas de FPS por debajo del objetivo y bytes/s dentro de meta.
- **Los tres casos**: entrar tarde, que se vaya el master, que se vaya un jugador con algo en la mano.
- **Auditoría**: `GnoMore/Auditar flotantes en escena` sin hallazgos nuevos y F4 en juego sin hallazgos tras 1 min de partida.

## Fases (resumen; detalle en Notion)

- **Fase 0 — Diagnóstico y cimientos** (`fase-0/`): medir, corregir física de proxies, estado de red ligero, telemetría, voz, capas, HUD sin IMGUI, predicción de presentación al agarrar.
- **Fase 1 — Loop y nivel** (`fase-1/`): greybox del campamento en dos capas, agacharse, escondites, respawn, puntuación de gnomos, balance con datos.
- **Fase 2 — Game feel**: fuego, salchicha, patada, petrificación, cámara, día/noche, audio.
- **Fase 3 — Usabilidad y onboarding**: tarjetas de rol, controles, lobby, pantalla final, encuesta y métricas.
- **Fase 4 — Extras**: camuflaje, torre tótem, pings, bots, Steam.

## Plantilla de tarjeta

```
# Cx.y — Título
Fase · Prioridad · Worker: modelo · Auditor: Opus · Iteraciones máx · Pulido: sí/no
## Objetivo (qué y por qué, con la métrica que mejora)
## Lee antes (archivos y páginas)
## Cambios exactos (archivo → función → qué)
## Qué NO hacer
## Validación: V0 / V1 (puntos específicos) / V2 (pasos y resultado esperado)
## Reporte esperado (además del formato estándar)
```

## Estado (28 oct 2026)

Rama de integración: `feature/fase-0`. V0 tras integrar el lote B y C0.4/C0.7 (6d9f4e8): compila sin errores ni warnings C# (`Tools/compile-check.ps1`). **Ninguna tarjeta tiene V2 hecha**: nadie ha jugado todavía esta rama en el editor. Ése es el siguiente paso.

| Tarjeta | Estado | Dónde | Falta |
| --- | --- | --- | --- |
| C0.1 PerfProbe | aprobada (falta V2) | 531bff1 | V2 y rellenar `fase-0/C0.1-resultados.md` |
| C0.2 Carryable proxies + respawn | aprobada (falta V2) | 531bff1 | V2; poner `LevelBounds` en `CampBryan` |
| C0.3 Estado de red ligero | aprobada (falta V2) | 531bff1 | V2 |
| C0.5 Voz | aprobada (falta V2) | 1f21fed | V2 (umbral final 0,02, no 0,04) |
| C1.9 Auditoría de flotantes | aprobada (falta V2), Opus vuelta 2 | a52e661 | V2 (pasos en la tarjeta) |
| C1.0 Sostener en la vista | aprobada (falta V2), Opus vuelta 3 | 62d4b2f | V2 (pasos y deuda en la tarjeta) |
| C1.H Hitos y agua | aprobada (falta V2), Opus vuelta 2 | 4e32333 | montaje en editor (cubeta, `WaterSource`, `Lit Grace Time` = 4) y 2 vueltas de playtest; decidir si soltar la cubeta la derrama |
| C0.4 Telemetría | aprobada (falta V2), Opus vuelta 2 | 094e711 | V2 (12 pasos en la tarjeta); añadir eventos nuevos a Notion |
| C0.7 HUD sin IMGUI | aprobada (falta V2), Opus vuelta 2 | 6d9f4e8 | ejecutar `GnoMore/Construir HUD de juego`, quitar componentes viejos de las escenas, decidir CampIsma; 2 vueltas de playtest |
| C0.8 Predicción de presentación | en worker | — | auditoría, integrar, V2 con Network Conditions |
| C0.6 Capas y máscaras | pendiente (persona en editor) | — | — |

Orden propuesto para la sesión de V2: montar en el editor (`GnoMore/Construir HUD de juego`, quitar HUD viejo, `LevelBounds`, cubeta, `WaterSource`, `Fireplace`) → auditoría de flotantes en escena (C1.9) → partida de 3 min con PerfProbe (C0.1) y PlaytestLogger (C0.4) + `Tools/playtest-report.py` → pasos de C0.2, C0.3, C0.5, C1.0 y C1.H.
