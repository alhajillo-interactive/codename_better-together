using System.Collections;
using System.Collections.Generic;
using BetterTogether.Combat;
using BetterTogether.Fireplace;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using BetterTogether.Interaction;


#if FUSION_WEAVER
using BetterTogether.Player;
using BetterTogether.Voice;
using Fusion;
#endif

/// <summary>
/// Inteligencia Artificial del Campista:
/// 1. Recolecta leña y fósforos para alimentar y encender la fogata.
/// 2. Detecta el sonido del micrófono de los gnomos (tanto en partidas locales como online con Photon Fusion).
/// 3. Interrumpe su tarea y persigue al gnomo ruidoso mientras siga haciendo ruido.
/// 4. Al alcanzar al gnomo dentro del radio de patada, lo patea lanzándolo por los aires y aturdiéndolo.
/// 5. Tras la patada, observa el área y luego reanuda sus tareas en la fogata.
/// </summary>
public class CampistaAI : MonoBehaviour
{
    public enum Estado
    {
        Evaluando,
        Patrullando,
        YendoPorObjeto,
        Recolectando,
        LlevandoAFogata,
        DejandoEnFogata,
        CuidandoFogata,
        PersiguiendoRuido,
        InvestigandoRuido,
        Terminado
    }

    [Header("Referencias")]
    public Fogata scriptFogata; // Arrastra la fogata aquí (si se usa Fogata local)
    public FireplaceManager fireplaceManager; // Arrastra el FireplaceManager aquí (si se usa en red)
    private NavMeshAgent agente;

    [Header("Detección Visual y Proximidad")]
    [Tooltip("Distancia a la que el bot detecta gnomos frente a él sin necesidad de ruido.")]
    public float distanciaVisionGnomo = 9.0f;

    [Tooltip("Ángulo de visión frontal (cono de detección visual).")]
    public float anguloVision = 65.0f;

    [Tooltip("Distancia a la que el bot siente a un gnomo incluso si está a sus espaldas.")]
    public float distanciaProximidadInmediata = 3.2f;

    [Tooltip("Tiempo máximo que perseguirá a un gnomo antes de rendirse y volver a su tarea.")]
    public float tiempoMaximoPersecucion = 6.0f;
    private float timerPersecucion = 0f;

    [Header("Mirada Petrificadora")]
    [Tooltip("Permite al bot petrificar gnomos con la mirada.")]
    public bool usarMiradaPetrificadora = true;
    public float distanciaMirada = 8.0f;
    private float timerFijacionMirada = 0f;
    public float tiempoParaPetrificar = 1.5f;
    private float timerMiradaGnomo = 0f;

    [Header("Control de Agresión")]
    [Tooltip("Tiempo en segundos que el bot ignora al gnomo tras patearlo para no hacer bucle infinito.")]
    public float cooldownPateado = 8.0f;
    private float tiempoFinInmunidadGnomo = 0f;
    private Transform ultimoGnomoPateado = null;

    [Header("Sujeción de Objetos en Manos")]
    [Tooltip("Transform hijo del bot donde se posicionará la leña/objeto mientras camina.")]
    public Transform puntoAgarre;
    private Carryable objetoCargado = null;


    [Header("Configuración del Bot")]
    public float radioDeVision = 15f; // Qué tan lejos puede ver/detectar objetos
    public float tiempoDeEspera = 3f; // Segundos que tarda en recoger o dejar algo

    [Header("Detección de Ruido y Persecución")]
    [Tooltip("Distancia máxima a la que el campista puede oír el micrófono o ruido de un gnomo.")]
    public float radioDeAudicion = 20f;

    [Tooltip("Tiempo en segundos que busca en el lugar del ruido tras callarse el gnomo antes de volver a su tarea.")]
    public float tiempoInvestigacionRuido = 3.5f;

    [Tooltip("Velocidad de movimiento normal (patrulla y recolección).")]
    public float velocidadNormal = 1.6f;

    [Tooltip("Velocidad de persecución: Más rápido que caminar (2.0), pero escapable si el gnomo sprinta (3.5).")]
    public float velocidadPersecucion = 2.7f;



    [Tooltip("Distancia mínima al gnomo para considerar que lo alcanzó.")]
    public float distanciaLlegadaGnomo = 1.6f;

    [Header("Mecánica de Patada a Gnomos")]
    [Tooltip("Distancia máxima a la que el campista asesta una patada al gnomo alcanzado.")]
    public float distanciaPatada = 1.8f;

    [Tooltip("Tiempo de espera mínimo entre patadas consecutivas.")]
    public float cooldownPatada = 2.0f;

    [Tooltip("Fuerza horizontal con la que sale volando el gnomo al ser pateado.")]
    public float fuerzaPatadaHorizontal = 6f;

    [Tooltip("Fuerza vertical (arco ascendente) del golpe.")]
    public float fuerzaPatadaVertical = 3.5f;

    [Header("Modelo 3D Visual")]
    [Tooltip("Prefab o modelo visual opcional a instanciar si el GameObject no tiene ya un modelo 3D hijo.")]
    [SerializeField] private GameObject _modeloCampistaPrefab;

    [Header("Puntos de Patrulla Opcionales")]
    [Tooltip("Puntos entre los cuales patrullará. Si está vacío, elegirá puntos aleatorios en el NavMesh.")]



    public Transform[] puntosPatrulla;
    private int indicePatrulla = 0;

    [Header("Estado Actual (Solo visualización)")]
    public Estado estadoActual = Estado.Evaluando;
    public string objetoBuscado = "Lena";

    private Transform objetivoActual;
    private float timer = 0f;

    // Memoria y persecución
    private Estado estadoPrevio = Estado.Evaluando;
    private Transform objetivoPrevio;
    private Vector3 ultimaPosicionRuido;
    private Transform gnomoRuidosoActual;
    private float timerInvestigacion = 0f;
    private float _proximoAtaqueTiempo = 0f;
    private bool _ejecutandoPatada = false;

    private void Awake()
    {
#if FUSION_WEAVER
        // Si hay un ConnectionManager en la escena y aún no hemos entrado al juego,
        // nos desactivamos para no recoger leñas ni movernos en el menú inicial.
        var connManager = BetterTogether.Networking.ConnectionManager.Instance 
                       ?? FindFirstObjectByType<BetterTogether.Networking.ConnectionManager>(FindObjectsInactive.Include);
        if (connManager != null && !connManager.IsConnected)
        {
            gameObject.SetActive(false);
        }
#endif
    }

    /// <summary>
    /// Inicia o reinicia el comportamiento del bot una vez ingresado a la partida.
    /// </summary>
    public void IniciarIA()
    {
        estadoActual = Estado.Evaluando;
        objetoBuscado = "Lena";
        if (agente == null)
        {
            agente = GetComponent<NavMeshAgent>();
        }
        if (agente != null && agente.isOnNavMesh)
        {
            agente.isStopped = false;
            agente.speed = velocidadNormal;
        }
    }

    void Start()
    {
        // Si el objeto tiene un render de cápsula residual y tiene hijos (modelo 3D), desactivamos la cápsula
        var meshRenderer = GetComponent<MeshRenderer>();
        var meshFilter = GetComponent<MeshFilter>();
        if (meshFilter != null && meshRenderer != null)
        {
            if (transform.childCount > 0 || _modeloCampistaPrefab != null)
            {
                meshRenderer.enabled = false;
            }
        }

        // Si se configuró un prefab de modelo y aún no tiene hijos, instanciarlo
        if (_modeloCampistaPrefab != null && transform.childCount == 0)
        {
            Instantiate(_modeloCampistaPrefab, transform);
        }

        agente = GetComponent<NavMeshAgent>();
        if (agente != null)
        {
            agente.speed = velocidadNormal;
        }

        if (scriptFogata == null && fireplaceManager == null)
        {
            fireplaceManager = FindFirstObjectByType<FireplaceManager>();
        }

        // Si no se asignó un punto de agarre en el inspector, crearlo en el pecho del bot
        if (puntoAgarre == null)
        {
            var agarre = new GameObject("PuntoAgarre");
            agarre.transform.SetParent(transform);
            agarre.transform.localPosition = new Vector3(0f, 1.0f, 0.5f);
            puntoAgarre = agarre.transform;
        }

    }

    void Update()
    {
        // Compatibilidad con Photon Fusion: si estamos en partida online, solo el State Authority o Master Client simula la IA
#if FUSION_WEAVER
        if (TryGetComponent<NetworkObject>(out var netObj) && netObj.Runner != null && netObj.Runner.IsRunning)
        {
            if (!netObj.HasStateAuthority && !netObj.Runner.IsSharedModeMasterClient)
                return;
        }
        else if (BetterTogether.Networking.ConnectionManager.Instance != null)
        {
            // Si aún no se ha conectado a la sala o no se eligió jugar con bot, permanecer inactivo
            if (!BetterTogether.Networking.ConnectionManager.Instance.IsConnected) { return; }

            if (!BetterTogether.Networking.ConnectionManager.Instance.Runner.IsSharedModeMasterClient)
                return;
        }
        // PROTECCIÓN PVP: Si hay un jugador humano jugando como Campista,
        // el bot se apaga solo automáticamente para no interferir en la partida normal.
        if (PlayerCharacter.All != null)
        {
            foreach (var p in PlayerCharacter.All)
            {
                if (p != null && p.Role == PlayerRole.Camper && p.Object != null && p.Object.IsValid)
                {
                    gameObject.SetActive(false);
                    return;
                }
            }
        }
#endif


        if (agente == null) return;

        // Comprobación de Ruido (Micrófono de gnomos) en cada frame
        VerificarGnomosCercanos();

        switch (estadoActual)
        {
            case Estado.Evaluando:
                // Si ya trae un objeto en las manos (ej. interrumpido por gnomo), llevarlo directo a la fogata
                if (objetoCargado != null)
                {
                    var destFogata = ObtenerDestinoFogata();
                    if (destFogata != null)
                    {
                        estadoActual = Estado.LlevandoAFogata;
                        agente.speed = velocidadNormal;
                        agente.SetDestination(destFogata.position);
                        return;
                    }
                }

                // 1. Decide qué necesita buscar
                if (scriptFogata != null)
                {
                    if (scriptFogata.lenasActuales < scriptFogata.lenasNecesarias)
                        objetoBuscado = "Lena";
                    else if (!scriptFogata.tieneFosforo)
                        objetoBuscado = "Fosforo";
                    else
                    {
                        estadoActual = Estado.Terminado;
                        return;
                    }
                }
                else if (fireplaceManager != null)
                {
                    var grill = FindFirstObjectByType<FireplaceGrill>();

                    if (!fireplaceManager.HasEnoughWood)
                    {
                        objetoBuscado = "Lena";
                    }
                    else if (!fireplaceManager.IsLit)
                    {
                        objetoBuscado = "Fosforo";
                    }
                    else if (grill != null && !grill.TieneSalchicha)
                    {
                        objetoBuscado = "Salchicha";
                    }
                    else
                    {
                        // La salchicha ya está en la parrilla cocinándose: vigilar la fogata
                        estadoActual = Estado.CuidandoFogata;
                        return;
                    }
                }


                // 2. Mira a su alrededor a ver si el objeto está cerca (usando FireplaceWood)
                objetivoActual = BuscarObjetoParaFogata(objetoBuscado);

                if (objetivoActual != null)
                {
                    estadoActual = Estado.YendoPorObjeto;
                    agente.speed = velocidadNormal;
                    agente.SetDestination(objetivoActual.position);
                }
                else
                {
                    // Si no lo ve, empieza a deambular para buscarlo
                    estadoActual = Estado.Patrullando;
                    agente.speed = velocidadNormal;
                    MoverAPuntoPatrulla();
                }
                break;

            case Estado.Patrullando:
                // Mientras camina, va mirando si ya encontró lo que busca
                objetivoActual = BuscarObjetoParaFogata(objetoBuscado);
                if (objetivoActual != null)
                {
                    estadoActual = Estado.YendoPorObjeto;
                    agente.SetDestination(objetivoActual.position);
                }
                // Si llegó a su punto de patrulla y aún no ve nada, elige otro punto
                else if (!agente.pathPending && agente.remainingDistance < 1f)
                {
                    MoverAPuntoPatrulla();
                }
                break;

            case Estado.YendoPorObjeto:
                if (objetivoActual == null)
                {
                    estadoActual = Estado.Evaluando;
                    return;
                } // Si un jugador se lo robó o fue destruido

                agente.SetDestination(objetivoActual.position);
                if (!agente.pathPending && agente.remainingDistance <= agente.stoppingDistance + 0.3f)
                {
                    estadoActual = Estado.Recolectando;
                    timer = tiempoDeEspera; // Inicia el reloj de espera
                }
                break;

            case Estado.Recolectando:
                timer -= Time.deltaTime;
                if (timer <= 0)
                {
                    if (objetivoActual != null)
                    {
                        // En vez de destruir el objeto, lo toma en sus manos
                        if (objetivoActual.TryGetComponent<Carryable>(out var carryable))
                        {
                            objetoCargado = carryable;
                            carryable.ForceReleaseAll();

                            // Ponerlo como cinemático y sujetarlo al bot
                            if (carryable.TryGetComponent<Rigidbody>(out var rb))
                                rb.isKinematic = true;
                            if (puntoAgarre != null)
                            {
                                carryable.transform.SetParent(puntoAgarre);
                                carryable.transform.localPosition = Vector3.zero;
                                carryable.transform.localRotation = Quaternion.identity;
                            }
                        }
                    }
                    var destinoFogata = ObtenerDestinoFogata();
                    if (destinoFogata != null)
                    {
                        estadoActual = Estado.LlevandoAFogata;
                        agente.speed = velocidadNormal;
                        agente.SetDestination(destinoFogata.position);
                    }
                    else
                    {
                        estadoActual = Estado.Evaluando;
                    }
                }
                break;

            case Estado.LlevandoAFogata:
                var destino = ObtenerDestinoFogata();
                if (destino == null) { estadoActual = Estado.Evaluando; return; }

                if (objetoCargado != null && puntoAgarre != null)
                {
                    objetoCargado.transform.position = puntoAgarre.position;
                    objetoCargado.transform.rotation = puntoAgarre.rotation;
                }


                agente.SetDestination(destino.position);
                if (!agente.pathPending && agente.remainingDistance <= agente.stoppingDistance + 1f)
                {
                    estadoActual = Estado.DejandoEnFogata;
                    timer = tiempoDeEspera; // Inicia el reloj para dejar el objeto
                }
                break;

            case Estado.DejandoEnFogata:
                timer -= Time.deltaTime;
                if (timer <= 0)
                {
                    if (objetoBuscado == "Lena")
                    {
                        if (fireplaceManager != null && objetoCargado != null)
                        {
                            objetoCargado.transform.SetParent(null);
                            int slotLibre = ObtenerSlotLibreFogata(fireplaceManager);
                            if (slotLibre >= 0)
                            {
                                objetoCargado.Pin(fireplaceManager, slotLibre);
                                Debug.Log($"<color=green>[CampistaAI] ¡Leño fijado en slot {slotLibre}!</color>");
                            }
                            objetoCargado = null;
                        }
                    }
                    else if (objetoBuscado == "Fosforo")
                    {
                        if (fireplaceManager != null)
                        {
                            // Encender la fogata
                            fireplaceManager.EncenderPorBot();
                            Debug.Log("<color=yellow>[CampistaAI] ¡Fogata ENCENDIDA con los fósforos!</color>");

                            if (objetoCargado != null)
                            {
                                objetoCargado.transform.SetParent(null);
                                Destroy(objetoCargado.gameObject);
                                objetoCargado = null;
                            }
                        }
                    }
                    else if (objetoBuscado == "Salchicha")
                    {
                        var grill = FindFirstObjectByType<FireplaceGrill>();
                        if (grill != null && objetoCargado != null && objetoCargado.TryGetComponent<Sausage>(out var sausage))
                        {
                            objetoCargado.transform.SetParent(null);
                            grill.ColocarSalchicha(sausage);
                            Debug.Log("<color=magenta>[CampistaAI] ¡Salchicha colocada en la parrilla para asar!</color>");
                            objetoCargado = null;
                        }
                    }

                    estadoActual = Estado.Evaluando;
                }
                break;




            case Estado.PersiguiendoRuido:
                if (gnomoRuidosoActual != null)
                {
                    float distAlGnomo = Vector3.Distance(transform.position, gnomoRuidosoActual.position);

                    // Orientar la mirada al gnomo
                    Vector3 lookDir = (gnomoRuidosoActual.position - transform.position);
                    lookDir.y = 0;
                    if (lookDir.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 5f);

#if FUSION_WEAVER
                    var petrifiable = gnomoRuidosoActual.GetComponentInParent<BetterTogether.Gaze.Petrifiable>();
                    bool gnomoEsPiedra = petrifiable != null && petrifiable.IsStone;

                    // FASE DE MIRADA PETRIFICADORA:
                    // Si el gnomo aún no es piedra y está a distancia de mirada (entre 2m y 8m), el bot avanza despacio y lo clava con la mirada
                    if (petrifiable != null && !gnomoEsPiedra && !petrifiable.IsGazeImmune && distAlGnomo <= 8f && distAlGnomo > distanciaPatada)
                    {
                        agente.speed = 1.0f; // Camina despacio clavando la mirada fijamente
                        agente.SetDestination(gnomoRuidosoActual.position);

                        timerMiradaGnomo += Time.deltaTime;
                        if (timerMiradaGnomo >= tiempoParaPetrificar)
                        {
                            petrifiable.PetrificarPorBot();
                            Debug.Log($"<color=cyan>[CampistaAI] ¡Mirada petrificadora EXITOSA! Gnomo '{gnomoRuidosoActual.name}' convertido en estatua.</color>");
                            timerMiradaGnomo = 0f;
                        }
                    }
                    else
                    {
                        // Si ya es piedra o se alejó, el bot corre a velocidad de persecución
                        agente.speed = velocidadPersecucion;
                        agente.SetDestination(gnomoRuidosoActual.position);
                        timerMiradaGnomo = 0f;
                    }
#else
            agente.speed = velocidadPersecucion;
            agente.SetDestination(gnomoRuidosoActual.position);
#endif

                    // REMATE: Si está a distancia de patada, PATEAR
                    if (distAlGnomo <= distanciaPatada && Time.time >= _proximoAtaqueTiempo && !_ejecutandoPatada)
                    {
                        PatearGnomo(gnomoRuidosoActual);
                        break;
                    }
                }
                else
                {
                    estadoActual = Estado.Evaluando;
                }
                break;



            case Estado.InvestigandoRuido:
                timerInvestigacion -= Time.deltaTime;

                // Pequeño giro de observación mientras busca
                transform.Rotate(Vector3.up, 40f * Time.deltaTime);

                // Si se termina el tiempo sin escuchar más ruidos, regresa a la rutina del campamento
                if (timerInvestigacion <= 0f)
                {
                    agente.speed = velocidadNormal;
                    gnomoRuidosoActual = null;

                    // Reanudar la tarea previa
                    if (scriptFogata != null && scriptFogata.fogataEncendida)
                    {
                        estadoActual = Estado.Terminado;
                    }
                    else if (estadoPrevio == Estado.LlevandoAFogata && scriptFogata != null)
                    {
                        estadoActual = Estado.LlevandoAFogata;
                        agente.SetDestination(scriptFogata.transform.position);
                    }
                    else if (estadoPrevio == Estado.YendoPorObjeto && objetivoPrevio != null)
                    {
                        objetivoActual = objetivoPrevio;
                        estadoActual = Estado.YendoPorObjeto;
                        agente.SetDestination(objetivoActual.position);
                    }
                    else
                    {
                        estadoActual = Estado.Evaluando;
                    }
                }
                break;

            case Estado.Terminado:
                // La fogata ya está encendida. Si no escucha nada, patrulla el campamento
                if (!agente.pathPending && agente.remainingDistance < 1f)
                {
                    MoverAPuntoPatrulla();
                }
                break;

            case Estado.CuidandoFogata:
                // Si los gnomos apagaron la fogata, volver a evaluar para encenderla
                if (fireplaceManager != null && !fireplaceManager.IsLit)
                {
                    estadoActual = Estado.Evaluando;
                    return;
                }

                // Revisar el estado de la salchicha
                var salchicha = Sausage.Instance ?? FindFirstObjectByType<Sausage>();
                if (salchicha != null)
                {
                    // Si ya está cocinada: ¡Comérsela y ganar la partida!
                    if (salchicha.State == Sausage.CookState.Cooked)
                    {
                        agente.SetDestination(salchicha.transform.position);
                        if (!agente.pathPending && agente.remainingDistance <= agente.stoppingDistance + 0.8f)
                        {
                            salchicha.ComerPorBot();
                            Debug.Log("<color=green>[CampistaAI] ¡Salchicha comida! ¡VICTORIA DEL CAMPISTA!</color>");
                            estadoActual = Estado.Terminado;
                            return;
                        }
                    }
                    else
                    {
                        // Mientras se cocina, patrullar cerca de la fogata vigilando
                        if (!agente.pathPending && agente.remainingDistance < 1f)
                        {
                            Vector3 posCercana = fireplaceManager.transform.position + Random.insideUnitSphere * 4f;
                            if (NavMesh.SamplePosition(posCercana, out var hit, 4f, NavMesh.AllAreas))
                                agente.SetDestination(hit.position);
                        }
                    }
                }
                break;

        }
    }

    /// <summary>
    /// Detecta si hay algún gnomo a la vista, muy cerca de espaldas, o haciendo ruido.
    /// </summary>
    private void VerificarGnomosCercanos()
    {
        // Si ya estamos ejecutando la animación de patada, no interrumpir
        if (_ejecutandoPatada) return;

        Transform gnomoDetectado = null;
        float menorDistancia = float.MaxValue;

#if FUSION_WEAVER
        // Buscar en los jugadores gnomos de la partida online
        if (PlayerCharacter.All != null)
        {
            foreach (var player in PlayerCharacter.All)
            {
                if (player == null || player.Role != PlayerRole.Gnome) continue;
                if (EsGnomoDetectable(player.transform, out float dist))
                {
                    if (dist < menorDistancia)
                    {
                        menorDistancia = dist;
                        gnomoDetectado = player.transform;
                    }
                }
            }
        }
#endif

        // Fallback: Si estamos en una escena de pruebas local con EmisorRuidoGnomo
        if (gnomoDetectado == null && EmisorRuidoGnomo.Emisores != null)
        {
            foreach (var emisor in EmisorRuidoGnomo.Emisores)
            {
                if (emisor == null) continue;
                if (EsGnomoDetectable(emisor.transform, out float dist))
                {
                    if (dist < menorDistancia)
                    {
                        menorDistancia = dist;
                        gnomoDetectado = emisor.transform;
                    }
                }
            }
        }

        // Si no vio a nadie, escuchar si alguien habla por el micrófono o hace ruido
        if (gnomoDetectado == null && BuscarGnomoRuidoso(out Vector3 posRuido, out Transform gnomoRuidoso))
        {
            // Ignorar al gnomo si recién fue pateado
            if (gnomoRuidoso != ultimoGnomoPateado || Time.time >= tiempoFinInmunidadGnomo)
            {
                gnomoDetectado = gnomoRuidoso;
            }
        }


        // Si se detectó un gnomo por visión o cercanía
        if (gnomoDetectado != null)
        {
            if (estadoActual != Estado.PersiguiendoRuido)
            {
                estadoPrevio = estadoActual;
                objetivoPrevio = objetivoActual;
                timerPersecucion = tiempoMaximoPersecucion;
            }

            gnomoRuidosoActual = gnomoDetectado;
            ultimaPosicionRuido = gnomoDetectado.position;
            estadoActual = Estado.PersiguiendoRuido;
            agente.speed = velocidadPersecucion;
            agente.SetDestination(ultimaPosicionRuido);
        }
    }

    private bool EsGnomoDetectable(Transform gnomo, out float distancia)
    {
        distancia = Vector3.Distance(transform.position, gnomo.position);

        // Si este gnomo fue pateado hace poco, el bot lo ignora para darle tiempo a escapar
        if (gnomo == ultimoGnomoPateado && Time.time < tiempoFinInmunidadGnomo)
        {
            return false;
        }

        // Detección por proximidad inmediata (3.2m)
        if (distancia <= distanciaProximidadInmediata)
            return true;

        // Detección por cono de visión frontal
        if (distancia <= distanciaVisionGnomo)
        {
            Vector3 direccion = (gnomo.position - transform.position).normalized;
            if (Vector3.Angle(transform.forward, direccion) <= anguloVision)
            {
                // Comprobar que no haya rocas/árboles en medio
                Vector3 ojos = transform.position + Vector3.up * 1.5f;
                Vector3 objetivo = gnomo.position + Vector3.up * 0.4f;
                if (!Physics.Raycast(ojos, (objetivo - ojos).normalized, (objetivo - ojos).magnitude, LayerMask.GetMask("Default", "Environment", "Obstacle")))
                {
                    return true;
                }
            }
        }

        return false;
    }



    private bool BuscarGnomoRuidoso(out Vector3 posicionRuido, out Transform gnomoTransform)
    {
        posicionRuido = Vector3.zero;
        gnomoTransform = null;
        float distMinima = radioDeAudicion;
        bool encontrado = false;

        // 1. Fuentes Locales (EmisorRuidoGnomo para pruebas y escenas locales)
        if (EmisorRuidoGnomo.Emisores != null)
        {
            foreach (var emisor in EmisorRuidoGnomo.Emisores)
            {
                if (emisor == null || !emisor.EstaHaciendoRuido) continue;

                float dist = Vector3.Distance(transform.position, emisor.transform.position);
                if (dist <= radioDeAudicion && dist < distMinima)
                {
                    distMinima = dist;
                    posicionRuido = emisor.transform.position;
                    gnomoTransform = emisor.transform;
                    encontrado = true;
                }
            }
        }

        // 2. Partidas Online (Photon Fusion & Photon Voice)
#if FUSION_WEAVER
        if (PlayerCharacter.All != null)
        {
            foreach (var player in PlayerCharacter.All)
            {
                if (player == null || player.Role != PlayerRole.Gnome) continue;

                bool ruidoGnomo = false;
                if (player.TryGetComponent<PlayerVoice>(out var voice) && voice.IsTalking)
                    ruidoGnomo = true;

                if (player.HasStateAuthority && Keyboard.current != null && (Keyboard.current.vKey.isPressed || Keyboard.current.spaceKey.isPressed))
                    ruidoGnomo = true;

                if (ruidoGnomo)
                {
                    float dist = Vector3.Distance(transform.position, player.transform.position);
                    if (dist <= radioDeAudicion && dist < distMinima)
                    {
                        distMinima = dist;
                        posicionRuido = player.transform.position;
                        gnomoTransform = player.transform;
                        encontrado = true;
                    }
                }
            }
        }
#endif

        return encontrado;
    }

    private bool GnomoEstaHaciendoRuido(Transform gnomo)
    {
        if (gnomo == null) return false;

        if (gnomo.TryGetComponent<EmisorRuidoGnomo>(out var emisor))
            return emisor.EstaHaciendoRuido;

#if FUSION_WEAVER
        if (gnomo.TryGetComponent<PlayerVoice>(out var voice) && voice.IsTalking)
            return true;

        if (gnomo.TryGetComponent<PlayerCharacter>(out var pc) && pc.HasStateAuthority && Keyboard.current != null && (Keyboard.current.vKey.isPressed || Keyboard.current.spaceKey.isPressed))
            return true;
#endif

        return false;
    }

    // Funciones Auxiliares
    Transform BuscarObjetoParaFogata(string tipo)
    {
        if (tipo == "Lena")
        {
            var woods = FindObjectsByType<FireplaceWood>(FindObjectsSortMode.None);
            if (woods != null && woods.Length > 0)
            {
                Transform masCercano = null;
                float distMinima = float.MaxValue; // Busca en todo el mapa la leña libre más cercana (sin límite ciego de 15m)
                Vector3 myPos = transform.position;
                foreach (var wood in woods)
                {


                    if (wood == null) continue;
                    // NUEVO: Si el leño ya está colocado en la fogata o alguien lo tiene en sus manos, ignorarlo
                    if (wood.TryGetComponent<BetterTogether.Interaction.Carryable>(out var carryable))
                    {
                        if (carryable.IsPinned || carryable.IsHeld)
                            continue;
                    }
                    float dist = Vector3.Distance(myPos, wood.transform.position);
                    if (dist <= distMinima)
                    {
                        distMinima = dist;
                        masCercano = wood.transform;
                    }
                }
                if (masCercano != null)
                    return masCercano;
            }
            return BuscarObjetoPorTagEnRadio("Lena");
        }
        else if (tipo == "Fosforo")
        {
            var matches = FindFirstObjectByType<BetterTogether.Fireplace.Matches>();
            if (matches != null)
                return matches.transform;

            return BuscarObjetoPorTagEnRadio("Fosforo");
        }
        else if (tipo == "Salchicha")
        {
            var sausage = Sausage.Instance ?? FindFirstObjectByType<Sausage>();
            if (sausage != null && !sausage.Eaten)
                return sausage.transform;
        }



        return null;
    }

    Transform BuscarObjetoPorTagEnRadio(string tag)
    {
        GameObject[] objetos = GameObject.FindGameObjectsWithTag(tag);
        Transform masCercano = null;
        float distMinima = float.MaxValue;

        foreach (GameObject obj in objetos)
        {
            if (obj == null) continue;
            float distanciaAlObjeto = Vector3.Distance(transform.position, obj.transform.position);
            if (distanciaAlObjeto <= distMinima)
            {
                distMinima = distanciaAlObjeto;
                masCercano = obj.transform;
            }
        }
        return masCercano;
    }

    private Transform ObtenerDestinoFogata()
    {
        if (scriptFogata != null) return scriptFogata.transform;
        if (fireplaceManager != null) return fireplaceManager.transform;

        fireplaceManager = FindFirstObjectByType<FireplaceManager>();
        if (fireplaceManager != null) return fireplaceManager.transform;

        return null;
    }

    /// <summary>
    /// Asesta una patada al gnomo alcanzado, mandándolo por los aires y aturdiéndolo.
    /// Funciona tanto para jugadores online (Kickable en Fusion) como para pruebas locales (EmisorRuidoGnomo).
    /// </summary>
    private void PatearGnomo(Transform gnomo)
    {
        if (gnomo == null) return;
        _proximoAtaqueTiempo = Time.time + cooldownPatada;
        ultimoGnomoPateado = gnomo;
        tiempoFinInmunidadGnomo = Time.time + cooldownPateado;

        // 1. Orientarse de frente hacia el gnomo
        Vector3 toGnomo = Flat(gnomo.position - transform.position);
        if (toGnomo.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(toGnomo.normalized);
        }

        // 2. Detener el agente brevemente para dar el golpe con impacto
        if (agente != null && agente.isOnNavMesh)
        {
            agente.velocity = Vector3.zero;
        }

        // 3. Calcular vector de impulso: combinación hacia adelante/objetivo y altura
        Vector3 forward = Flat(transform.forward).normalized;
        Vector3 dirImpulso = (forward + toGnomo.normalized).normalized;
        Vector3 impulso = dirImpulso * fuerzaPatadaHorizontal + Vector3.up * fuerzaPatadaVertical;

        // 4. Si es un jugador online (Kickable en Fusion)
        var kickable = gnomo.GetComponentInParent<Kickable>();
        if (kickable != null && kickable.CanBeKicked)
        {
            kickable.Kick(impulso, transform.position);
        }

        // 5. Si es el gnomo de pruebas local (EmisorRuidoGnomo)
        var emisor = gnomo.GetComponentInParent<EmisorRuidoGnomo>();
        if (emisor != null)
        {
            emisor.RecibirPatada(impulso, transform.position);
        }

        // 6. Animación procedural de patada física
        StartCoroutine(RutinaAnimacionPatada());

        Debug.Log($"<color=red>[CampistaAI] ¡PATEÓ AL GNOMO '{gnomo.name}'!</color> Impulso: {impulso}");

        // 7. Pausa de observación: investiga el área durante un momento tras la patada
        gnomoRuidosoActual = null;
        timerMiradaGnomo = 0f;
        agente.speed = velocidadNormal;
        estadoActual = Estado.Evaluando;
    }

    private IEnumerator RutinaAnimacionPatada()
    {
        _ejecutandoPatada = true;

        // Buscar el transform del modelo 3D hijo
        Transform visual = transform.Find("campista") ?? transform.Find("Camper") ?? transform.Find("Visual");
        if (visual == null && transform.childCount > 0)
        {
            visual = transform.GetChild(0);
        }

        if (visual != null)
        {
            Vector3 posOriginal = visual.localPosition;
            Quaternion rotOriginal = visual.localRotation;

            // Paso 1: Preparación / Carga hacia atrás (0.08s)
            float t = 0f;
            while (t < 0.08f)
            {
                t += Time.deltaTime;
                float frac = t / 0.08f;
                visual.localPosition = Vector3.Lerp(posOriginal, posOriginal - Vector3.forward * 0.12f, frac);
                visual.localRotation = Quaternion.Slerp(rotOriginal, rotOriginal * Quaternion.Euler(-8f, 0, 0), frac);
                yield return null;
            }

            // Paso 2: Impacto / Arremetida frontal de la patada (0.12s)
            t = 0f;
            while (t < 0.12f)
            {
                t += Time.deltaTime;
                float frac = t / 0.12f;
                visual.localPosition = Vector3.Lerp(posOriginal - Vector3.forward * 0.12f, posOriginal + Vector3.forward * 0.35f, frac);
                visual.localRotation = Quaternion.Slerp(rotOriginal * Quaternion.Euler(-8f, 0, 0), rotOriginal * Quaternion.Euler(16f, 0, 0), frac);
                yield return null;
            }

            // Paso 3: Retorno a la postura normal (0.18s)
            t = 0f;
            while (t < 0.18f)
            {
                t += Time.deltaTime;
                float frac = t / 0.18f;
                visual.localPosition = Vector3.Lerp(posOriginal + Vector3.forward * 0.35f, posOriginal, frac);
                visual.localRotation = Quaternion.Slerp(rotOriginal * Quaternion.Euler(16f, 0, 0), rotOriginal, frac);
                yield return null;
            }

            visual.localPosition = posOriginal;
            visual.localRotation = rotOriginal;
        }

        _ejecutandoPatada = false;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    void MoverAPuntoPatrulla()
    {
        // Si hay puntos de patrulla definidos, rota entre ellos
        if (puntosPatrulla != null && puntosPatrulla.Length > 0)
        {
            var punto = puntosPatrulla[indicePatrulla % puntosPatrulla.Length];
            indicePatrulla++;
            if (punto != null)
            {
                agente.SetDestination(punto.position);
                return;
            }
        }

        // Si no hay puntos de patrulla, patrulla en un círculo seguro alrededor de la fogata (evita irse al río)
        Vector3 centro = ObtenerDestinoFogata() != null ? ObtenerDestinoFogata().position : transform.position;
        Vector3 direccionAleatoria = centro + Random.insideUnitSphere * 7f;
        direccionAleatoria.y = centro.y;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(direccionAleatoria, out hit, 10f, NavMesh.AllAreas))
        {
            agente.SetDestination(hit.position);
        }
    }

    private void OnDrawGizmosSelected()
    {
        // Radio de visión (Amarillo)
        Gizmos.color = new Color(1f, 0.92f, 0.016f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, radioDeVision);

        // Radio de audición (Azul cian, o Rojo si está en persecución)
        Gizmos.color = (estadoActual == Estado.PersiguiendoRuido || estadoActual == Estado.InvestigandoRuido)
            ? new Color(1f, 0.2f, 0.2f, 0.6f)
            : new Color(0.2f, 0.6f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, radioDeAudicion);

        // Línea hacia el ruido o el objetivo
        if (estadoActual == Estado.PersiguiendoRuido || estadoActual == Estado.InvestigandoRuido)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, ultimaPosicionRuido);
            Gizmos.DrawSphere(ultimaPosicionRuido, 0.3f);
        }
        else if (objetivoActual != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, objetivoActual.position);
        }
    }

    /// <summary>
    /// Activa o apaga el Bot Campista desde el menú F1.
    /// Al activarse, ubica y posiciona el agente firmemente sobre el NavMesh del terreno.
    /// </summary>
    public void SetBotActivo(bool activo)
    {
        gameObject.SetActive(activo);

        if (activo)
        {
            if (agente == null)
                agente = GetComponent<NavMeshAgent>();

            if (agente != null)
            {
                // Encaja al bot exactamente en la superficie del NavMesh para evitar caídas
                if (NavMesh.SamplePosition(transform.position, out var hit, 5f, NavMesh.AllAreas))
                {
                    agente.Warp(hit.position);
                }
                agente.isStopped = false;
                agente.speed = velocidadNormal;
            }

            estadoActual = Estado.Evaluando;
            Debug.Log("<color=green>[CampistaAI] ¡Bot Campista ACTIVADO y listo para cazar duendes!</color>");
        }
        else
        {
            Debug.Log("<color=yellow>[CampistaAI] Bot Campista DESACTIVADO para permitir PvP normal.</color>");
        }
    }

    private void EjecutarMiradaPetrificadora(Transform gnomo)
    {
        // Orientar la mirada suavemente hacia el gnomo
        Vector3 lookDir = (gnomo.position - transform.position);
        lookDir.y = 0;
        if (lookDir.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 6f);
        }

#if FUSION_WEAVER
        // Buscar el componente Petrifiable del gnomo
        var petrifiable = gnomo.GetComponentInParent<BetterTogether.Gaze.Petrifiable>();
        if (petrifiable != null && !petrifiable.IsStone && !petrifiable.IsGazeImmune)
        {
            timerFijacionMirada += Time.deltaTime;
            // Si lo mantiene en la mira durante 0.3 segundos continuos, lo convierte en piedra
            if (timerFijacionMirada >= 0.3f)
            {
                petrifiable.ForceRelease(0f); // Reinicia timers
                                              // Si el gnomo tiene método de petrificación o llamamos a su freeze:
                Debug.Log($"<color=cyan>[CampistaAI] ¡Mirada petrificadora CONGELÓ al gnomo '{gnomo.name}' en piedra!</color>");
                timerFijacionMirada = 0f;
            }
        }
        else
        {
            timerFijacionMirada = 0f;
        }
#endif
    }

    private int ObtenerSlotLibreFogata(FireplaceManager fp)
{
    var list = new List<Carryable>();
    Carryable.GetPinnedTo(fp, list);
    var ocupados = new HashSet<int>();
    foreach (var item in list)
    {
        ocupados.Add(item.PinnedSlot);
    }

    for (int i = 0; i < fp.RequiredWood; i++)
    {
        if (!ocupados.Contains(i))
            return i;
    }
    return -1;
}




}