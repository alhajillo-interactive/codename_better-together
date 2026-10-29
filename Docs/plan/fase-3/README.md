# Fase 3 — Usabilidad y onboarding

Meta: que un jugador nuevo entienda su rol y sus teclas en 10 segundos. Métricas (del análisis del 27 oct): primera interacción < 15 s; ≥ 80 % completa su primer objetivo en 45 s; < 2 prompts «no puedes» por minuto; «entendí mi objetivo» ≥ 85 %.

Reglas comunes: UI en uGUI + TMP + PrimeTween (nada de IMGUI); textos para el jugador en español; la UI nueva se construye con **herramientas de editor idempotentes** (`GnoMore/…`) que el orquestador ejecuta por CLI; cero asignaciones por frame; nada de estado de red nuevo salvo que una tarjeta lo justifique. Auditor: Sonnet.

| Tarjeta | Qué | Archivos |
| --- | --- | --- |
| C3.1 | Tarjeta de rol en la cuenta regresiva + controles persistentes que se atenúan | nuevo `UI/RoleCard.cs`, `UI/ControlsHint.cs` + herramienta (prefab `MatchHUD`) |
| C3.2 | Lobby: lista de jugadores, quién será el campista (rotación), estado «listo» | `UI/StartMatchUI.cs` + herramienta (prefab `StartMatch_Canvas`) |
| C3.3 | Encuesta post-partida y métricas de usabilidad en la telemetría | nuevo `UI/PostMatchSurvey.cs`, ganchos de telemetría, `Tools/playtest-report.py` |
| C3.4 | Ajustes: volúmenes, sensibilidad, bob de cámara, micrófono (pulsar para hablar / abierto) | `UI/MainMenuController.cs` o nuevo `UI/SettingsPanel.cs` + herramienta (escena `MainMenu`) |
