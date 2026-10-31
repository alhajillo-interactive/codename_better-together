# 02 · «El Abrazo» v2 — diseño tras la crítica

> Sustituye a `01-propuesta-inicial.md`. Cifras calculadas, sin probar en juego. Primero se construye un **prototipo jugable** con formas simples; el arte fino viene después de jugarlo.

## 1. La idea
Un claro en el bosque con **dos árboles gigantes abrazados**: uno oscuro y vivo, otro pálido y muerto. Sus raíces parten el mapa en sur y norte. **Entre los dos troncos queda una rendija** que es a la vez el atajo de los gnomos y la mirilla por la que se espían los dos lados. La rampa que los rodea cambia altura por alcance: arriba ves más lejos, pero tu cámara llega menos al suelo.

## 2. Tamaño
76 × 70 m (x ±38, z ±35), dentro del límite actual de 75 × 75 m (se ajusta a 80 × 80). Superficie ≈ 5.300 m².

| Recorrido | Campista (paso / sprint) | Gnomo (paso / sprint) |
| --- | --- | --- |
| Rodear por una punta de raíz, de sur a norte (≈ 62 m) | 21 / 12 s | 31 / 18 s |
| Cruzar por la Rendija (20 m) | no cabe | 10 / 6 s |

## 3. El árbol
- **Dos troncos en D**: un círculo de radio 7 m en (0,0) partido por una banda diagonal. Oscuro al suroeste, pálido al noreste. Silueta trenzada sólo visual (sin colisión).
- **La Rendija (Gatera + Ojo)**: banda de 0,6 m de ancho y 0,8 m de alto, de (−4,6, 7) a (6,5, −10), unos 20 m. El campista no entra ni agachado (1 m), pero sí puede **agacharse en la boca y fotografiar hacia dentro**: quien la usa corre riesgo. Mide más que el alcance de la cámara, así que de punta a punta sólo se espía.
- **Raíces**: dos muros de 2,8 m de alto desde el tronco hasta x = ±24. Una torre de 3 con salto (≈ 2,14 m) no los supera. Túneles de gnomo en x = ±16 (0,7 alto, 0,6 ancho).
- **Rampa**: 22 tramos rectos de 2 m de ancho rodeando los troncos a radio ≈ 9 m, desde (9, −3) en Y0, por el norte (Y2,5) y el oeste (Y4,5) hasta el **Hombro** en (−6, −7) Y6, y sigue hasta la **Copa** en Y10. Rampa 7,6°.
- **Grieta** (sólo gnomos, capa `GnomeOnly`): 8 repisas de 0,4 m + un tramo inclinado dentro del tronco pálido, de Y0 al Hombro.
- **Repisa de torre**: a 1,6 m en (−10, −4): sólo una torre de gnomos llega, y es un atajo a la rampa (recompensa real).
- **Fogata**: (0, 0,4, −9,5), dentro de una U de raíces (x = ±5, z −7 a −12), boca al sur de 8 m. La salida de la Rendija queda **fuera** de la U, con una mirilla de 0,4 m hacia la fogata. A 8 m, el cono cubre 6,5 de los 8 m de boca; con la remontada, entera.
- **Humo**: al encender, columna visible en todo el mapa desde la Copa.

## 4. Sectores
| Sector | Centro, Y | Decisión que crea |
| --- | --- | --- |
| **Campamento** | (0, −24), 0 | Vigilar la boca o ir a por recursos. Tiendas, jaula en (12, −24), leño (−6, −26) |
| **Serrería** | (28, −14), 0–3 | Pasarelas para el campista, huecos bajo las tablas para gnomos. Leño arriba (30, −14, 1,5) |
| **Huerto** | (−28, −20), 0 | 10 señuelos: disparar a todo gasta placas. Leño (−30, −22), 2 escondites |
| **Risco** | (−30, 18), 0–4,5 | Terrazas en bucle. Leño arriba (−34, 22, 3), salchicha (−30, 28, 4,5) |
| **Hondonada** | (0, 26), −1 | Cobertura densa. Leño (6, 30), 3 escondites, boca norte de la Rendija |
| **Poza** | (28, 26), −1,5 | Agua (toda la orilla), leño en la orilla (16,5, 30), salchicha en el muelle (34, 28), **cubeta** al sur en (32, 6) |

El campista necesita 5 de 7 leños y sólo 3 están al sur: **tiene que cruzar al norte**, y al hacerlo deja la fogata sola.

## 5. Colocación
- **Leños (7)**: los seis de arriba + Hombro (−8, −5, 6).
- **Fósforos**: Hombro (−6, −7, 6).
- **Salchichas (3)**: Copa (arriba, la más cercana a la fogata: riesgo a cambio de tiempo), Risco, muelle.
- **Escondites (8)**: Huerto (−24, −14) (−34, −14); Serrería (34, −18); Hondonada (−8, 28) (10, 24) (0, 33); Risco (−36, 12); Poza (20, 31).
- **Señuelos**: 10 Huerto, 5 Hondonada.
- **Spawns**: campista (0, −30); gnomos (−34, 8), (0, 32), (34, 14). El más cercano, a más de 40 m.

## 6. Cambios de reglas
- **Cocción**: `_cookedAt` 15 → 30 s, `_burntAt` 25 → 42 s, para que el humo sea una alarma real.
- **La cubeta gotea**: una cubeta llena se vacía en 30 s si no se vierte. Así acaba la estrategia de esperar con ella junto a la fogata.
- Circuito de cubeta: (32, 6) → agua (26, 22) → punta de raíz → boca ≈ 74 m (21 s a sprint, 37 a paso).

## 7. Fuera de este prototipo (decidido por la crítica)
Lanzar leños, canoa empujable, tablón pateable, humo que cierra la Grieta, cubeta que no cabe en túneles. Si el mapa funciona, se valoran después.

## 8. Objetivos de la prueba
Primer contacto en < 45 s; cada sector norte visitado en ≥ 70 % de las partidas; partida de 5–8 min; campista < 40 % del tiempo a 10 m de la fogata.
