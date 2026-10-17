using UnityEngine;
using UnityEngine.AI;

public class CampistaAI : MonoBehaviour
{
    public enum Estado { Evaluando, Patrullando, YendoPorObjeto, Recolectando, LlevandoAFogata, DejandoEnFogata, Terminado }

    [Header("Referencias")]
    public Fogata scriptFogata; // Arrastra la fogata aquí
    private NavMeshAgent agente;

    [Header("Configuración del Bot")]
    public float radioDeVision = 15f; // Qué tan lejos puede ver/detectar objetos
    public float tiempoDeEspera = 3f; // Segundos que tarda en recoger o dejar algo

    [Header("Estado Actual (Solo visualización)")]
    public Estado estadoActual = Estado.Evaluando;
    public string objetoBuscado = "Lena";

    private Transform objetivoActual;
    private float timer = 0f;

    void Start()
    {
        agente = GetComponent<NavMeshAgent>();
    }

    void Update()
    {
        switch (estadoActual)
        {
            case Estado.Evaluando:
                // 1. Decide qué necesita buscar
                if (scriptFogata.lenasActuales < scriptFogata.lenasNecesarias)
                    objetoBuscado = "Lena";
                else if (!scriptFogata.tieneFosforo)
                    objetoBuscado = "Fosforo";
                else
                {
                    estadoActual = Estado.Terminado;
                    return;
                }

                // 2. Mira a su alrededor a ver si el objeto está cerca
                objetivoActual = BuscarObjetoEnRadio(objetoBuscado);

                if (objetivoActual != null)
                {
                    estadoActual = Estado.YendoPorObjeto;
                    agente.SetDestination(objetivoActual.position);
                }
                else
                {
                    // Si no lo ve, empieza a deambular para buscarlo
                    estadoActual = Estado.Patrullando;
                    MoverAPuntoAleatorio();
                }
                break;

            case Estado.Patrullando:
                // Mientras camina, va mirando si ya encontró lo que busca
                objetivoActual = BuscarObjetoEnRadio(objetoBuscado);
                if (objetivoActual != null)
                {
                    estadoActual = Estado.YendoPorObjeto;
                    agente.SetDestination(objetivoActual.position);
                }
                // Si llegó a su punto de patrulla y aún no ve nada, elige otro punto
                else if (!agente.pathPending && agente.remainingDistance < 1f)
                {
                    MoverAPuntoAleatorio();
                }
                break;

            case Estado.YendoPorObjeto:
                if (objetivoActual == null) { estadoActual = Estado.Evaluando; return; } // Si un jugador se lo robó mientras iba

                agente.SetDestination(objetivoActual.position);
                if (!agente.pathPending && agente.remainingDistance <= agente.stoppingDistance)
                {
                    estadoActual = Estado.Recolectando;
                    timer = tiempoDeEspera; // Inicia el reloj de espera
                }
                break;

            case Estado.Recolectando:
                timer -= Time.deltaTime;
                if (timer <= 0)
                {
                    // Finge agarrarlo destruyendo el objeto de la escena (tus compañeros lo animarán)
                    Destroy(objetivoActual.gameObject);

                    estadoActual = Estado.LlevandoAFogata;
                    agente.SetDestination(scriptFogata.transform.position);
                }
                break;

            case Estado.LlevandoAFogata:
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
                    // Entrega el objeto a la fogata
                    if (objetoBuscado == "Lena") scriptFogata.lenasActuales++;
                    else if (objetoBuscado == "Fosforo") scriptFogata.tieneFosforo = true;

                    // Vuelve a empezar el ciclo
                    estadoActual = Estado.Evaluando;
                }
                break;
        }
    }

    // Funciones Auxiliares
    Transform BuscarObjetoEnRadio(string tag)
    {
        GameObject[] objetos = GameObject.FindGameObjectsWithTag(tag);
        Transform masCercano = null;
        float distMinima = radioDeVision; // Solo detectará objetos menores a esta distancia

        foreach (GameObject obj in objetos)
        {
            float distanciaAlObjeto = Vector3.Distance(transform.position, obj.transform.position);
            if (distanciaAlObjeto <= distMinima)
            {
                distMinima = distanciaAlObjeto;
                masCercano = obj.transform;
            }
        }
        return masCercano; // Retorna null si no encontró nada cerca
    }

    void MoverAPuntoAleatorio()
    {
        // Busca un punto aleatorio en un radio de 20 metros para deambular
        Vector3 direccionAleatoria = Random.insideUnitSphere * 20f;
        direccionAleatoria += transform.position;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(direccionAleatoria, out hit, 20f, 1))
        {
            agente.SetDestination(hit.position);
        }
    }
}