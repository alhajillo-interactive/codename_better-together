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

Rama de integración: `feature/fase-0`. V0 de `feature/fase-0` (1f21fed): compila sin errores ni warnings C# (`Tools/compile-check.ps1`, 28 oct). Ninguna tarjeta tiene V2 hecha; nadie ha jugado todavía esta rama en el editor.

| Tarjeta | Estado | Dónde | Falta |
| --- | --- | --- | --- |
| C0.1 PerfProbe | aprobada (falta V2) | `feature/fase-0` (531bff1) | V2 y rellenar `fase-0/C0.1-resultados.md` |
| C0.2 Carryable proxies + respawn | aprobada (falta V2) | `feature/fase-0` (531bff1) | V2; poner `LevelBounds` en `CampBryan` |
| C0.3 Estado de red ligero | aprobada (falta V2) | `feature/fase-0` (531bff1) | V2 |
| C0.5 Voz | aprobada (falta V2) | `feature/fase-0` (1f21fed) | V2 (umbral final 0,02, no 0,04) |
| C1.0 Sostener en la vista | corrigiendo → falta 2.ª auditoría | rama `worktree-agent-a9e279c0734809bfb` | veredicto de Opus, integrar, V2 |
| C1.9 Auditoría de flotantes | corrigiendo → falta 2.ª auditoría | rama `worktree-agent-a775cf6f135587aa9` | veredicto de Opus, integrar, V2 |
| C1.H Hitos y agua | en auditoría (1.ª vuelta sin hacer) | rama `worktree-agent-a89ddd20f12bdfd54` | auditoría, integrar, prefab `Items/Bucket` y `WaterSource` en escena (persona), 2 vueltas de playtest |
| C0.4, C0.6, C0.7, C0.8 | pendiente (lote C) | — | — |

Las tres ramas del lote B fusionan sin conflictos con `feature/fase-0` y entre sí (`git merge-tree`, 28 oct). Orden de integración propuesto: C1.9 → C1.0 → C1.H, para que la V2 de las dos últimas ya pueda usar la auditoría de flotantes.
