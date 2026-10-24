using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Sensor de ruido para gnomos. Detecta la voz del micrófono real de la PC o simula ruido con teclado.
/// El Campista bot escucha este componente para detectar y perseguir al gnomo.
/// </summary>
public class EmisorRuidoGnomo : MonoBehaviour
{
    private static readonly List<EmisorRuidoGnomo> _emisores = new();
    public static IReadOnlyList<EmisorRuidoGnomo> Emisores => _emisores;

    [Header("Detección de Micrófono Real")]
    [Tooltip("Usa el micrófono físico de la PC para detectar volumen de voz.")]
    [SerializeField] private bool _usarMicrofono = true;

    [Tooltip("Umbral de volumen (RMS) a partir del cual se considera que estás haciendo ruido.")]
    [Range(0.001f, 0.2f)]
    [SerializeField] private float _umbralVolumen = 0.02f;

    [Tooltip("Nombre del dispositivo de micro (deja vacío para usar el predeterminado).")]
    [SerializeField] private string _dispositivoMicrofono;

    [Header("Simulación con Teclado (Para pruebas sin micro)")]
    [Tooltip("Permite simular ruido manteniendo presionada una tecla.")]
    [SerializeField] private bool _habilitarTeclaRuido = true;
    [SerializeField] private Key _teclaRuido = Key.V;

    [Header("Control de Movimiento (Para pruebas en escena local)")]
    [Tooltip("Permite mover este personaje con WASD si no hay otro controlador asignado.")]
    [SerializeField] private bool _controlarConTeclado = true;
    [SerializeField] private float _velocidad = 4f;

    [Header("Recepción de Patadas y Aturdimiento")]
    [Tooltip("Tiempo en segundos que el gnomo queda aturdido e incapaz de moverse tras recibir una patada.")]
    [SerializeField] private float _duracionAturdimiento = 2.5f;

    [Header("Depuración")]
    [SerializeField] private bool _mostrarDebugUI = true;

    private AudioClip _micClip;
    private const int SampleWindow = 128;
    private CharacterController _characterController;
    private float _tiempoAturdido = 0f;
    private Vector3 _velocidadKnockback = Vector3.zero;

    public float VolumenActual { get; private set; }
    public bool EstaHaciendoRuido { get; private set; }
    public bool MicrofonoConectado { get; private set; }
    public bool EstaAturdido => _tiempoAturdido > 0f;

    /// <summary>
    /// Llamado cuando el Campista patea a este gnomo.
    /// </summary>
    public void RecibirPatada(Vector3 fuerza, Vector3 posicionCampista)
    {
        _velocidadKnockback = fuerza;
        _tiempoAturdido = _duracionAturdimiento;
        EstaHaciendoRuido = false;
        Debug.Log($"<color=orange>[EmisorRuidoGnomo] ¡El gnomo fue pateado!</color> Impulso: {fuerza}");
    }

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
    }

    private void OnEnable()
    {
        _emisores.Add(this);
        IniciarMicrofono();
    }

    private void OnDisable()
    {
        _emisores.Remove(this);
        DetenerMicrofono();
    }

    private void IniciarMicrofono()
    {
        if (!_usarMicrofono)
            return;

        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[EmisorRuidoGnomo] No se detectaron micrófonos conectados en el sistema. Puedes usar la tecla [" + _teclaRuido + "] para simular ruido.");
            MicrofonoConectado = false;
            return;
        }

        MicrofonoConectado = true;
        var device = string.IsNullOrEmpty(_dispositivoMicrofono) ? Microphone.devices[0] : _dispositivoMicrofono;
        _micClip = Microphone.Start(device, true, 1, 44100);
    }

    private void DetenerMicrofono()
    {
        if (Microphone.devices.Length > 0 && Microphone.IsRecording(null))
        {
            Microphone.End(null);
        }
    }

    private void Update()
    {
        if (EstaAturdido)
        {
            _tiempoAturdido -= Time.deltaTime;
            AplicarFisicasKnockback();
            return;
        }

        ActualizarRuido();

        if (_controlarConTeclado)
            ActualizarMovimiento();
    }

    private void AplicarFisicasKnockback()
    {
        var dt = Time.deltaTime;
        var paso = (_velocidadKnockback + Vector3.down * 9.81f) * dt;

        if (_characterController != null && _characterController.enabled)
        {
            _characterController.Move(paso);
        }
        else
        {
            transform.position += paso;
        }

        _velocidadKnockback = Vector3.Lerp(_velocidadKnockback, Vector3.zero, dt * 5f);
    }

    private void ActualizarRuido()
    {
        VolumenActual = 0f;
        var ruidoPorMic = false;

        if (_usarMicrofono && MicrofonoConectado && _micClip != null)
        {
            VolumenActual = CalcularVolumenRMS();
            if (VolumenActual >= _umbralVolumen)
                ruidoPorMic = true;
        }

        var ruidoPorTecla = false;
        if (_habilitarTeclaRuido && Keyboard.current != null)
        {
            if (Keyboard.current[_teclaRuido].isPressed || Keyboard.current.spaceKey.isPressed)
            {
                ruidoPorTecla = true;
                VolumenActual = Mathf.Max(VolumenActual, 0.5f);
            }
        }

        EstaHaciendoRuido = ruidoPorMic || ruidoPorTecla;
    }

    private float CalcularVolumenRMS()
    {
        var micPos = Microphone.GetPosition(null) - SampleWindow + 1;
        if (micPos < 0)
            return 0f;

        var waveData = new float[SampleWindow];
        _micClip.GetData(waveData, micPos);

        float sum = 0f;
        for (int i = 0; i < SampleWindow; i++)
        {
            sum += waveData[i] * waveData[i];
        }

        return Mathf.Sqrt(sum / SampleWindow);
    }

    private void ActualizarMovimiento()
    {
        if (Keyboard.current == null)
            return;

        float moveX = 0f;
        float moveZ = 0f;

        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) moveZ += 1f;
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) moveZ -= 1f;
        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) moveX -= 1f;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) moveX += 1f;

        var move = new Vector3(moveX, 0f, moveZ).normalized;
        if (move.sqrMagnitude > 0.001f)
        {
            var delta = move * (_velocidad * Time.deltaTime);
            if (_characterController != null && _characterController.enabled)
            {
                _characterController.Move(delta + Vector3.down * (9.81f * Time.deltaTime));
            }
            else
            {
                transform.position += delta;
            }
            transform.rotation = Quaternion.LookRotation(move);
        }
    }

    private void OnGUI()
    {
        if (!_mostrarDebugUI)
            return;

        var boxWidth = 330f;
        var boxHeight = 115f;
        var rect = new Rect(10f, Screen.height - boxHeight - 10f, boxWidth, boxHeight);

        GUI.Box(rect, "Gnomo: Sensor de Micrófono");
        GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 22f, rect.width - 20f, rect.height - 25f));

        var estiloAlerta = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize = 13,
            normal = { textColor = EstaHaciendoRuido ? Color.red : Color.green }
        };

        if (EstaAturdido)
        {
            var estiloAturdido = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 13,
                normal = { textColor = new Color(1f, 0.55f, 0f) }
            };
            GUILayout.Label($"💫 ¡PATEADO! Aturdido: {_tiempoAturdido:F1}s", estiloAturdido);
        }
        else if (EstaHaciendoRuido)
            GUILayout.Label("🔴 ¡HACIENDO RUIDO! (El Campista te oye)", estiloAlerta);
        else
            GUILayout.Label("🟢 En silencio (Seguro)", estiloAlerta);

        // Barra de nivel de volumen
        var pct = Mathf.Clamp01(VolumenActual / Mathf.Max(0.0001f, _umbralVolumen * 2f));
        GUILayout.Label($"Volumen: {VolumenActual:F4} / Umbral: {_umbralVolumen:F4}");

        var barRect = GUILayoutUtility.GetRect(rect.width - 20f, 10f);
        GUI.Box(barRect, "");
        var fillRect = new Rect(barRect.x, barRect.y, barRect.width * pct, barRect.height);
        var oldColor = GUI.color;
        GUI.color = EstaHaciendoRuido ? Color.red : Color.green;
        GUI.DrawTexture(fillRect, Texture2D.whiteTexture);
        GUI.color = oldColor;

        GUILayout.Label("WASD: Moverse | Habla o mantén [V] para hacer ruido", GUI.skin.label);

        GUILayout.EndArea();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = EstaHaciendoRuido ? Color.red : Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 1f);
    }
}
