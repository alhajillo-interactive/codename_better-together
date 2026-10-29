# Fase 4 — Extras

## Estado (29 oct 2026)
| Tarjeta | Estado |
| --- | --- |
| C4.1 Camuflaje de gnomo de jardín | Integrada (Opus, 2 vueltas). `GardenDisguise` en el prefab Gnome. Agachado y quieto 1 s → pose de adorno con tinte cerámico; la mirada no puede tomarlo como objetivo nuevo; patada lo revela; cooldown 3 s. El tinte lo compone sólo `Petrifiable` (overlay + piedra). |
| C4.2 Comeback | Integrada (Opus, 2 vueltas). Al 2.º apagado: fósforos recolocados delante del campista (con línea de visión y espacio libre) y cono de mirada 22° → 26° durante 10 s (`FireplaceManager.ComebackTimer` replicado), anuncios por rol. |
| C4.3 Partidas públicas | Integrada (Sonnet, 2 vueltas). Panel en el menú: lista de salas públicas, crear pública, unión rápida (si no hay sala, crea «Campamento NNNN» — verificado en Play Mode). Salas por código ahora invisibles. |
| Bots | **Decisión pendiente**: la rama `origin/feature/BOT` (Deivis, bot campista) reescribe ~600 líneas de `Petrifiable` y choca con fases 0–4. Opciones: portarla como tarjeta nueva sobre el código actual, o diseñar un bot desde cero (gnomo o campista). |
| Steam | **Decisión pendiente**: requiere cuenta de Steamworks, App ID y elegir librería (Steamworks.NET o Facepunch.Steamworks); página «Próximamente» es trabajo de la persona. |

V2: camuflarse junto a un arbusto y que el campista no pueda bloquearlo; patearlo; apagar el fuego 2 veces y ver los fósforos delante del campista y el cono más ancho 10 s; desde dos instancias, crear pública / ver en la lista / unirse / unión rápida.
